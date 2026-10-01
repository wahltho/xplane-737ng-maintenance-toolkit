using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Platform;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Aircraft;

/// <summary>Moves bytes and existing ownership together; never adopts or repairs patch content.</summary>
public sealed class AircraftMoveOperation
{
    private readonly ToolStateStore _store;
    private readonly ToolkitSettingsStore _settings;
    private readonly Func<bool> _isRunning;
    private readonly Func<string, long> _freeSpace;
    private readonly Action<string>? _checkpoint;
    public string JournalPath => Path.Combine(_store.RootPath, "aircraft-move.json");
    public bool HasPendingMove => File.Exists(JournalPath);

    public AircraftMoveOperation(ToolStateStore store, ToolkitSettingsStore settings, Func<bool>? isXPlaneRunning = null)
        : this(store, settings, isXPlaneRunning, null, null) { }
    internal AircraftMoveOperation(ToolStateStore store, ToolkitSettingsStore settings, Func<bool>? isRunning,
        Func<string, long>? freeSpace, Action<string>? checkpoint)
    {
        _store = store; _settings = settings;
        if (!AircraftMoveStateRebaser.Same(store.RootPath, settings.RootPath))
            throw new ArgumentException("Aircraft move state and settings must share the Toolkit data folder.");
        _isRunning = isRunning ?? XPlaneProcessDetector.IsXPlaneRunning;
        _freeSpace = freeSpace ?? FreeSpace;
        _checkpoint = checkpoint;
    }

    public AircraftMovePlan Prepare(string source, string destination, CancellationToken token = default)
    {
        if (HasPendingMove) throw new InvalidOperationException("Finish recovery of the interrupted aircraft move first.");
        CheckStopped();
        source = Full(source); destination = Full(destination);
        ValidateLocations(source, destination);
        if (!Directory.Exists(source) || !Directory.EnumerateFiles(source, "*.acf").Any())
            throw new InvalidOperationException("Select an aircraft folder containing an ACF file.");
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(source) & UnixFileMode.UserWrite) == 0)
            throw new InvalidOperationException("The aircraft folder must allow its owner to write before it can be moved. Enable owner write permission first.");
        if (File.Exists(destination) || Directory.Exists(destination))
            throw new InvalidOperationException("The destination already exists. Choose an unused folder name.");
        if (!Directory.Exists(Path.GetDirectoryName(destination)))
            throw new InvalidOperationException("The destination parent folder must already exist.");
        var entries = Snapshot(source, token);
        var bytes = entries.Sum(e => e.Size);
        if (_freeSpace(Path.GetDirectoryName(destination)!) < checked(bytes + 16L * 1024 * 1024))
            throw new IOException("There is not enough free space for the verified aircraft copy.");
        AircraftMoveStateRebaser.Rebase(_store.Load(), source, destination);
        return new(source, destination, entries, Fingerprint(_store.StatePath), Fingerprint(_settings.SettingsPath));
    }

    public AircraftMoveResult Execute(AircraftMovePlan plan, IProgress<AircraftMoveProgress>? progress = null,
        CancellationToken token = default)
    {
        using var lease = AcquireLease();
        // Recheck after the confirmation dialog; plans do not grant lasting authority.
        var current = Prepare(plan.Source, plan.Destination, token);
        if (!SameEntries(current.Entries, plan.Entries) || current.StateFingerprint != plan.StateFingerprint
            || current.SettingsFingerprint != plan.SettingsFingerprint)
            throw new InvalidOperationException("The aircraft or Toolkit history changed. Review the move again.");
        var settings = _settings.Load();
        if (AircraftMoveStateRebaser.Within(settings.SelectedAircraftPath, plan.Source))
            settings.SelectedAircraftPath = Path.Combine(plan.Destination, Path.GetRelativePath(plan.Source, Full(settings.SelectedAircraftPath)));
        var id = Guid.NewGuid().ToString("N");
        var journal = new AircraftMoveJournal
        {
            Id = id, Source = plan.Source, Destination = plan.Destination,
            Stage = Path.Combine(Path.GetDirectoryName(plan.Destination)!, ".mtk-aircraft-move-" + id + "-stage"),
            Hold = Path.Combine(Path.GetDirectoryName(plan.Source)!, ".mtk-aircraft-move-" + id + "-source"),
            Entries = [.. plan.Entries], OriginalState = ReadOptional(_store.StatePath),
            OriginalSettings = ReadOptional(_settings.SettingsPath),
            NewState = JsonSerializer.SerializeToUtf8Bytes(AircraftMoveStateRebaser.Rebase(_store.Load(), plan.Source, plan.Destination)),
            NewSettings = JsonSerializer.SerializeToUtf8Bytes(settings)
        };
        // The before-images remain in the journal until rollback or cleanup completes.
        if (Fingerprint(journal.OriginalState) != plan.StateFingerprint || Fingerprint(journal.OriginalSettings) != plan.SettingsFingerprint)
            throw new InvalidOperationException("Toolkit state changed while preparing the transaction.");
        journal.Save(JournalPath);
        try
        {
            _checkpoint?.Invoke("Prepared");
            CopyTree(journal, token, progress);
            VerifyTree(journal.Stage, plan.Entries, token, Marker(journal));
            VerifyTree(plan.Source, plan.Entries, token);
            CheckStopped(); CheckOriginalDocuments(journal);
            token.ThrowIfCancellationRequested();
            Directory.Move(plan.Source, journal.Hold);
            _checkpoint?.Invoke("SourceRetained");
            // This final rename is on the destination volume, including cross-volume moves.
            Directory.Move(journal.Stage, plan.Destination);
            _checkpoint?.Invoke("Activated");
            VerifyTree(plan.Destination, plan.Entries, token, Marker(journal));
            VerifyTree(journal.Hold, plan.Entries, token);
            CheckStopped(); CheckOriginalDocuments(journal);
            token.ThrowIfCancellationRequested();
            AircraftMoveJournal.Write(_store.StatePath, journal.NewState);
            _checkpoint?.Invoke("StateSaved");
            AircraftMoveJournal.Write(_settings.SettingsPath, journal.NewSettings);
            _checkpoint?.Invoke("SettingsSaved");
            journal.Phase = "Committed";
            journal.Save(JournalPath);
            _checkpoint?.Invoke("Committed");
        }
        catch (Exception ex) when (ex is not AircraftMoveSimulatedCrashException)
        {
            try { RecoverCore(); }
            catch (Exception recoveryError)
            { throw new IOException("Move interrupted. Recovery is pending; do not remove either folder or the move journal. " + recoveryError.Message, ex); }
            throw;
        }
        progress?.Report(new("Finalizing the move", 98));
        // Cancellation after the durable commit cannot turn a complete move into rollback.
        try { RecoverCore(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(plan.Destination, true); }
        progress?.Report(new("Aircraft moved and history updated", 100));
        return new(plan.Destination, false);
    }

    /// <summary>Rolls back an uncommitted move or completes cleanup after a durable commit.</summary>
    public string? Recover()
    {
        if (!HasPendingMove) return null;
        using var lease = AcquireLease();
        return RecoverCore();
    }

    private FileStream AcquireLease()
    {
        Directory.CreateDirectory(_store.RootPath);
        var path = Path.Combine(_store.RootPath, "aircraft-move.lock");
        NoLinks(path);
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new IOException("An aircraft move or recovery is active in another Toolkit instance.", ex); }
    }

    private string? RecoverCore()
    {
        if (!HasPendingMove) return null;
        CheckStopped(); NoLinks(JournalPath);
        if (new FileInfo(JournalPath).Length > AircraftMoveJournal.MaximumBytes) throw new InvalidDataException("Move journal is too large.");
        var j = JsonSerializer.Deserialize<AircraftMoveJournal>(File.ReadAllBytes(JournalPath))
            ?? throw new InvalidDataException("Move journal is unreadable.");
        ValidateJournal(j);
        if (j.Phase == "Committed")
        {
            VerifyTree(j.Destination, j.Entries, CancellationToken.None, Marker(j));
            CheckDocument(_store.StatePath, j.NewState);
            CheckDocument(_settings.SettingsPath, j.NewSettings);
            if (Directory.Exists(j.Source)) throw new IOException("The old aircraft path is occupied again. Recovery requires review.");
            DeleteOwnedTree(j.Hold, j.Entries, allowPartial: false);
            DeleteOwnedTree(j.Stage, j.Entries, allowPartial: false, Marker(j));
            var markerPath = Path.Combine(j.Destination, Marker(j));
            NoLinks(markerPath);
            if (File.Exists(markerPath)) { RequireMarker(j.Destination, j); File.Delete(markerPath); }
            File.Delete(JournalPath);
            return j.Destination;
        }
        // Check everything before restoring a document or removing the activated copy.
        CheckKnownDocument(_store.StatePath, j.OriginalState, j.NewState);
        CheckKnownDocument(_settings.SettingsPath, j.OriginalSettings, j.NewSettings);
        if (Directory.Exists(j.Hold))
        {
            if (Directory.Exists(j.Source) || File.Exists(j.Source)) throw new IOException("The original aircraft path is occupied.");
            NoLinks(j.Hold);
        }
        else if (!Directory.Exists(j.Source)) throw new IOException("Both the source aircraft and retained source are missing.");
        if (Directory.Exists(j.Destination))
        {
            RequireMarker(j.Destination, j);
            VerifyTree(j.Destination, j.Entries, CancellationToken.None, Marker(j));
        }
        if (Directory.Exists(j.Stage) && Directory.EnumerateFileSystemEntries(j.Stage).Any()) RequireMarker(j.Stage, j);
        ValidateOwnedTree(j.Stage, j.Entries, allowPartial: true, Marker(j));
        if (Directory.Exists(j.Hold)) Directory.Move(j.Hold, j.Source);
        RestoreDocument(_store.StatePath, j.OriginalState);
        RestoreDocument(_settings.SettingsPath, j.OriginalSettings);
        DeleteOwnedTree(j.Destination, j.Entries, allowPartial: false, Marker(j));
        DeleteOwnedTree(j.Stage, j.Entries, allowPartial: true, Marker(j));
        File.Delete(JournalPath);
        return j.Source;
    }

    private void ValidateLocations(string source, string destination)
    {
        NoLinks(source); NoLinks(destination); NoLinks(_store.RootPath); NoLinks(_store.StatePath); NoLinks(_settings.SettingsPath);
        if (AircraftMoveStateRebaser.Within(source, destination) || AircraftMoveStateRebaser.Within(destination, source))
            throw new InvalidOperationException("Source and destination must be separate folders; case-only renames are not supported.");
        foreach (var path in new[] { _store.RootPath, _settings.RootPath, _store.BackupRootPath,
                     _settings.Load().AircraftUpdateCacheRootPath, _settings.Load().OfflinePackageRootPath,
                     _settings.Load().DiagnosticsExportRootPath })
            if (AircraftMoveStateRebaser.Within(path, source) || AircraftMoveStateRebaser.Within(path, destination)
                || AircraftMoveStateRebaser.Within(source, path) || AircraftMoveStateRebaser.Within(destination, path))
                throw new InvalidOperationException("Aircraft folders must be separate from Toolkit data, backups and caches.");
    }
    private void ValidateJournal(AircraftMoveJournal j)
    {
        if (j.SchemaVersion != 1 || !Guid.TryParseExact(j.Id, "N", out _) || j.Phase is not ("Prepared" or "Committed"))
            throw new InvalidDataException("Unknown move journal format.");
        ValidateLocations(j.Source, j.Destination);
        if (j.Stage != Path.Combine(Path.GetDirectoryName(j.Destination)!, ".mtk-aircraft-move-" + j.Id + "-stage")
            || j.Hold != Path.Combine(Path.GetDirectoryName(j.Source)!, ".mtk-aircraft-move-" + j.Id + "-source"))
            throw new InvalidDataException("Unsafe move journal paths.");
        NoLinks(j.Stage); NoLinks(j.Hold);
        foreach (var e in j.Entries)
            if (e.RelativePath != ".") Content.ContentPatchPathSafety.ResolveTarget(j.Source, e.RelativePath, "Move journal file");
    }
    private void CheckStopped() { if (_isRunning()) throw new InvalidOperationException("Close X-Plane before moving or recovering an aircraft."); }
    private void CheckOriginalDocuments(AircraftMoveJournal j)
    { CheckDocument(_store.StatePath, j.OriginalState); CheckDocument(_settings.SettingsPath, j.OriginalSettings); }
    private static void CheckDocument(string path, byte[]? expected)
    { if (Fingerprint(path) != Fingerprint(expected)) throw new IOException("Toolkit state/settings changed during the move. Recovery requires review."); }
    private static void CheckKnownDocument(string path, byte[]? original, byte[] next)
    {
        var actual = Fingerprint(path);
        if (actual != Fingerprint(original) && actual != Fingerprint(next)) throw new IOException("Toolkit state/settings has unrelated changes. Recovery requires review.");
    }
    private static void RestoreDocument(string path, byte[]? bytes)
    { if (bytes is null) { if (File.Exists(path)) File.Delete(path); } else AircraftMoveJournal.Write(path, bytes); }
    private static byte[]? ReadOptional(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;
    private static string Fingerprint(string path) => Fingerprint(ReadOptional(path));
    private static string Fingerprint(byte[]? bytes) => bytes is null ? "absent" : Convert.ToHexString(SHA256.HashData(bytes));
    private static string Full(string path) => AircraftMoveStateRebaser.FullPath(path);
    internal static long FreeSpace(string path)
    {
        var mount = DriveInfo.GetDrives().Where(d => d.IsReady && AircraftMoveStateRebaser.Within(path, d.Name))
            .OrderByDescending(d => d.Name.Length).FirstOrDefault();
        return (mount ?? new DriveInfo(Path.GetPathRoot(path)!)).AvailableFreeSpace;
    }
    private static void NoLinks(string path)
    {
        for (var current = Full(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if (new FileInfo(current).LinkTarget is not null || new DirectoryInfo(current).LinkTarget is not null
                || ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0))
                throw new InvalidOperationException("Move paths must not contain symbolic links or junctions: " + current);
    }
    private static List<AircraftMoveEntry> Snapshot(string root, CancellationToken token, string? marker = null)
    {
        NoLinks(root);
        var result = new List<AircraftMoveEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string path)
        {
            token.ThrowIfCancellationRequested(); NoLinks(path);
            var relative = Path.GetRelativePath(root, path);
            if (!seen.Add(relative)) throw new IOException("Case-colliding aircraft paths cannot be moved safely.");
            var attr = File.GetAttributes(path);
            var directory = (attr & FileAttributes.Directory) != 0;
            var time = File.GetLastWriteTimeUtc(path);
            int? mode = OperatingSystem.IsWindows() ? null : (int)File.GetUnixFileMode(path);
            long size = 0; var hash = "";
            if (!directory)
            {
                using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                size = input.Length;
                hash = Hash(input, token);
                if (input.Length != size || File.GetLastWriteTimeUtc(path) != time) throw new IOException("An aircraft file changed while reading: " + relative);
            }
            result.Add(new(relative, directory, size, hash, attr, time, mode));
            if (directory) foreach (var entry in Directory.EnumerateFileSystemEntries(path).Order(StringComparer.Ordinal))
                if (path != root || Path.GetFileName(entry) != marker) Visit(entry);
        }
        Visit(root); return result;
    }
    private static string Hash(Stream input, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024]; int count;
        while ((count = input.Read(buffer)) != 0) { token.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, count); }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    private static bool SameEntries(IReadOnlyList<AircraftMoveEntry> a, IReadOnlyList<AircraftMoveEntry> b) => a.SequenceEqual(b);
    private static void VerifyTree(string path, IReadOnlyList<AircraftMoveEntry> expected, CancellationToken token, string? marker = null)
    {
        var actual = Snapshot(path, token, marker);
        // Directory modification times can change during staging, but file bytes and permissions must match.
        if (actual.Count != expected.Count || !actual.Zip(expected).All(p =>
            p.First.RelativePath == p.Second.RelativePath && p.First.Directory == p.Second.Directory
            && p.First.Size == p.Second.Size && p.First.Sha256 == p.Second.Sha256
            && p.First.UnixMode == p.Second.UnixMode
            && (p.First.Attributes & FileAttributes.ReadOnly) == (p.Second.Attributes & FileAttributes.ReadOnly)))
            throw new IOException("Aircraft copy verification failed: " + path);
    }
    private static void CopyTree(AircraftMoveJournal j, CancellationToken token, IProgress<AircraftMoveProgress>? progress)
    {
        if (Directory.Exists(j.Stage) || File.Exists(j.Stage)) throw new IOException("Move staging path is already occupied.");
        Directory.CreateDirectory(j.Stage);
        File.WriteAllText(Path.Combine(j.Stage, Marker(j)), j.Id);
        long copied = 0, total = j.Entries.Sum(e => e.Size); var lastPercent = -1;
        foreach (var e in j.Entries)
        {
            token.ThrowIfCancellationRequested();
            var target = Path.Combine(j.Stage, e.RelativePath);
            var source = Path.Combine(j.Source, e.RelativePath);
            NoLinks(source); NoLinks(target);
            if (e.Directory) { Directory.CreateDirectory(target); continue; }
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[128 * 1024]; int count;
                while ((count = input.Read(buffer)) != 0)
                {
                    token.ThrowIfCancellationRequested(); output.Write(buffer, 0, count); copied += count;
                    var percent = total == 0 ? 50 : (int)(70d * copied / total);
                    if (percent != lastPercent) { progress?.Report(new("Copying " + e.RelativePath, percent)); lastPercent = percent; }
                }
                output.Flush(flushToDisk: true);
            }
            ApplyMetadata(source, target, e);
        }
        foreach (var e in j.Entries.Where(e => e.Directory).Reverse())
            ApplyMetadata(Path.Combine(j.Source, e.RelativePath), Path.Combine(j.Stage, e.RelativePath), e);
    }
    private static void ApplyMetadata(string source, string target, AircraftMoveEntry e)
    {
        if (e.Directory) Directory.SetLastWriteTimeUtc(target, e.LastWriteUtc);
        else File.SetLastWriteTimeUtc(target, e.LastWriteUtc);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(target, (UnixFileMode)e.UnixMode!.Value);
        else if (e.Directory)
        {
            var access = new DirectorySecurity();
            // SetAccessControl persists modified sections only; a freshly read ACL is not marked modified.
            access.SetSecurityDescriptorBinaryForm(new DirectoryInfo(source).GetAccessControl(AccessControlSections.Access)
                .GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
            new DirectoryInfo(target).SetAccessControl(access);
        }
        else
        {
            var access = new FileSecurity();
            access.SetSecurityDescriptorBinaryForm(new FileInfo(source).GetAccessControl(AccessControlSections.Access)
                .GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
            new FileInfo(target).SetAccessControl(access);
        }
        File.SetAttributes(target, e.Attributes);
    }
    private static string Marker(AircraftMoveJournal j) => ".mtk-move-owner-" + j.Id;
    private static void RequireMarker(string path, AircraftMoveJournal j)
    {
        var marker = Path.Combine(path, Marker(j)); NoLinks(marker);
        if (!File.Exists(marker) || File.ReadAllText(marker) != j.Id) throw new IOException("Move destination ownership is ambiguous. Nothing will be removed.");
    }
    private static void ValidateOwnedTree(string path, IReadOnlyList<AircraftMoveEntry> entries, bool allowPartial, string? marker = null)
    {
        NoLinks(path);
        if (!Directory.Exists(path)) { if (File.Exists(path)) throw new IOException("Move path is occupied by a file."); return; }
        var allowed = entries.ToDictionary(e => e.RelativePath, StringComparer.Ordinal);
        foreach (var actual in Snapshot(path, CancellationToken.None, marker))
        {
            if (!allowed.TryGetValue(actual.RelativePath, out var expected) || actual.Directory != expected.Directory
                || (!actual.Directory && !allowPartial && actual.Sha256 != expected.Sha256))
                throw new IOException("Move cleanup found unexpected or modified files. Nothing will be removed: " + path);
        }
    }
    private static void DeleteOwnedTree(string path, IReadOnlyList<AircraftMoveEntry> entries, bool allowPartial, string? marker = null)
    {
        ValidateOwnedTree(path, entries, allowPartial, marker);
        if (!Directory.Exists(path)) return;
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
        if (!OperatingSystem.IsWindows())
            foreach (var directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories).Prepend(path))
                File.SetUnixFileMode(directory, File.GetUnixFileMode(directory) | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Directory.Delete(path, recursive: true);
    }
}

internal sealed class AircraftMoveSimulatedCrashException : Exception;
