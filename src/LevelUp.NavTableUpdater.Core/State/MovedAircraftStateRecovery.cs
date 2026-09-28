using System.Security.Cryptography;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Content;

namespace LevelUp.NavTableUpdater.Core.State;

public sealed record MovedAircraftStateCandidate(
    string PreviousFolder,
    string CurrentFolder,
    string ProductFamily,
    int VerifiedFiles,
    int VerifiedBackups);

/// <summary>
/// Rebinds an existing, verifiable MTK ownership chain after the user moves an
/// aircraft directory. It never adopts a standalone patch or changes aircraft files.
/// </summary>
public sealed class MovedAircraftStateRecovery(ToolStateStore store)
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public MovedAircraftStateCandidate? FindCandidate(string currentFolder, string productFamily)
    {
        var current = Path.GetFullPath(currentFolder);
        var family = AircraftProductIds.Normalize(productFamily);
        if (family is null || !Directory.Exists(current) || IsLink(current)) return null;

        var state = store.Load();
        if (state.ContentInstallations.ContainsKey(ToolStateStore.PathKey(current))) return null;
        if (state.Aircraft.Values.Any(item => SamePath(RecordedAircraftFolder(item), current))) return null;

        var matches = new List<MovedAircraftStateCandidate>();
        foreach (var pair in state.ContentInstallations)
        {
            var installation = pair.Value;
            if (string.IsNullOrWhiteSpace(installation.AircraftFolder)
                || installation.ContentComponents.Count == 0) continue;
            var previous = Path.GetFullPath(installation.AircraftFolder);
            if (SamePath(previous, current) || Directory.Exists(previous) || File.Exists(previous)
                || !string.Equals(pair.Key, ToolStateStore.PathKey(previous), StringComparison.Ordinal)) continue;

            var owners = state.Aircraft.Where(item => SamePath(RecordedAircraftFolder(item.Value), previous)).ToArray();
            if (owners.Length == 0 || !owners.Any(item => ProductFamily(item.Value.AircraftId) == family)) continue;
            if (owners.Any(item => ProductFamily(item.Value.AircraftId) != family)) continue;
            if (HasOtherOldOwnership(state, previous)) continue;

            try
            {
                var counts = Verify(state, installation, owners, previous, current, family);
                matches.Add(new(previous, current, family, counts.Files, counts.Backups));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                or InvalidOperationException or ArgumentException)
            {
                // A missing or divergent file is not proof of a move. Leave the
                // original state and normal standalone-ownership guard intact.
            }
        }

        return matches.Count == 1 ? matches[0] : null;
    }

    public string Reconnect(MovedAircraftStateCandidate candidate)
    {
        // Verify again immediately before writing; the displayed candidate may
        // have gone stale while the confirmation dialog was open.
        var current = FindCandidate(candidate.CurrentFolder, candidate.ProductFamily);
        if (current is null || current != candidate)
            throw new InvalidOperationException("The aircraft or backup state changed since the move was detected. Scan again.");

        var state = store.Load();
        var oldKey = ToolStateStore.PathKey(candidate.PreviousFolder);
        var newKey = ToolStateStore.PathKey(candidate.CurrentFolder);
        if (!state.ContentInstallations.TryGetValue(oldKey, out var installation)
            || state.ContentInstallations.ContainsKey(newKey))
            throw new InvalidOperationException("The aircraft ownership state changed. Scan again.");

        var owners = state.Aircraft.Where(item => SamePath(RecordedAircraftFolder(item.Value), candidate.PreviousFolder)).ToArray();
        Verify(state, installation, owners, candidate.PreviousFolder, candidate.CurrentFolder, candidate.ProductFamily);

        var destinationKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var owner in owners)
        {
            var nextKey = string.IsNullOrWhiteSpace(owner.Value.AcfPath)
                ? ToolStateStore.ProductTargetKey(candidate.ProductFamily, candidate.CurrentFolder)
                : ToolStateStore.PathKey(Rebase(owner.Value.AcfPath, candidate.PreviousFolder, candidate.CurrentFolder));
            if (!destinationKeys.Add(nextKey)
                || state.Aircraft.ContainsKey(nextKey) && !string.Equals(nextKey, owner.Key, StringComparison.Ordinal))
                throw new InvalidOperationException("The destination already has an aircraft state record.");
        }

        RebaseInstallation(installation, candidate.PreviousFolder, candidate.CurrentFolder);
        state.ContentInstallations.Remove(oldKey);
        state.ContentInstallations.Add(newKey, installation);
        foreach (var owner in owners)
        {
            state.Aircraft.Remove(owner.Key);
            RebaseAircraft(owner.Value, candidate.PreviousFolder, candidate.CurrentFolder);
            var key = string.IsNullOrWhiteSpace(owner.Value.AcfPath)
                ? ToolStateStore.ProductTargetKey(candidate.ProductFamily, candidate.CurrentFolder)
                : ToolStateStore.PathKey(owner.Value.AcfPath);
            state.Aircraft.Add(key, owner.Value);
        }

        // Preserve a byte-for-byte copy of the former state document, separate
        // from the aircraft backups. Save itself uses an atomic replacement.
        var stateCopy = Path.Combine(store.RootPath,
            $"state-before-aircraft-reconnect-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.json");
        File.Copy(store.StatePath, stateCopy, overwrite: false);
        store.Save(state);
        return stateCopy;
    }

    private static (int Files, int Backups) Verify(ToolStateDocument state,
        ContentInstallationToolState installation,
        KeyValuePair<string, AircraftToolState>[] owners,
        string previous, string current, string family)
    {
        if (owners.Length == 0 || owners.Any(item => ProductFamily(item.Value.AircraftId) != family))
            throw new InvalidDataException("The recorded aircraft product differs from the selected product.");
        if (IsLink(previous) || IsLink(current) || HasOtherOldOwnership(state, previous))
            throw new InvalidDataException("The old or new folder has an unsafe or competing ownership record.");

        var expected = new Dictionary<string, (long Size, string Hash)>(StringComparer.OrdinalIgnoreCase);
        var backups = new HashSet<string>(PathComparison == StringComparison.Ordinal
            ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        GatherComponents(installation.ContentComponents.Values, previous, current, expected, backups);
        foreach (var owner in owners)
        {
            if (!string.IsNullOrWhiteSpace(owner.Value.AcfPath))
                Rebase(owner.Value.AcfPath, previous, current);
            if (!string.IsNullOrWhiteSpace(owner.Value.PrefsPath))
                Rebase(owner.Value.PrefsPath, previous, current);
            GatherComponents(owner.Value.ContentComponents.Values, previous, current, expected, backups);
            GatherBackups(owner.Value.Backups, previous, backups);
        }
        GatherBackups(installation.Backups, previous, backups);
        if (expected.Count == 0)
            throw new InvalidDataException("No complete managed-file fingerprints are available for this aircraft.");

        foreach (var file in expected)
        {
            var path = ContentPatchPathSafety.ResolveTarget(current, file.Key, "Moved aircraft file");
            VerifyFile(path, file.Value.Size, file.Value.Hash);
        }
        return (expected.Count, backups.Count);
    }

    private static void GatherComponents(IEnumerable<ContentComponentState> components,
        string previous, string current, Dictionary<string, (long Size, string Hash)> expected,
        HashSet<string> backups)
    {
        foreach (var component in components)
        foreach (var file in component.Files)
        {
            var oldPath = ContentPatchPathSafety.ResolveTarget(previous, file.RelativePath, "Recorded aircraft file");
            var newPath = ContentPatchPathSafety.ResolveTarget(current, file.RelativePath, "Moved aircraft file");
            if (!SamePath(file.TargetPath, oldPath) || !IsWithin(newPath, current)
                || file.InstalledSizeBytes is null || string.IsNullOrWhiteSpace(file.InstalledSha256))
                throw new InvalidDataException($"Incomplete ownership evidence: {file.RelativePath}.");
            var identity = (file.InstalledSizeBytes.Value, file.InstalledSha256);
            if (expected.TryGetValue(file.RelativePath, out var existing)
                && (existing.Size != identity.Item1 || !HashEqual(existing.Hash, identity.Item2)))
                throw new InvalidDataException($"Conflicting recorded hashes: {file.RelativePath}.");
            expected[file.RelativePath] = identity;
            if (file.OriginalExisted)
            {
                if (file.OriginalSizeBytes is null || string.IsNullOrWhiteSpace(file.OriginalSha256))
                    throw new InvalidDataException($"Incomplete original backup evidence: {file.RelativePath}.");
                VerifyFile(file.BackupPath, file.OriginalSizeBytes.Value, file.OriginalSha256);
                backups.Add(file.BackupPath);
            }
        }
    }

    private static void GatherBackups(IEnumerable<BackupRecord> records, string previous, HashSet<string> backups)
    {
        foreach (var record in records)
        {
            Rebase(record.SourcePath, previous, previous);
            if (record.SourceExisted)
            {
                if (IsLink(record.BackupPath))
                    throw new InvalidDataException($"Backup is a symbolic link: {record.BackupPath}.");
                if (File.Exists(record.BackupPath))
                {
                    if (record.SourceSizeBytes.HasValue && new FileInfo(record.BackupPath).Length != record.SourceSizeBytes.Value)
                        throw new InvalidDataException($"Backup size changed: {record.BackupPath}.");
                    if (!string.IsNullOrWhiteSpace(record.SourceSha256)
                        && !HashEqual(HashFile(record.BackupPath), record.SourceSha256))
                        throw new InvalidDataException($"Backup hash changed: {record.BackupPath}.");
                }
                else if (!Directory.Exists(record.BackupPath) || record.SourceSizeBytes.HasValue || record.SourceSha256 is not null)
                    throw new InvalidDataException($"Backup is missing: {record.BackupPath}.");
                backups.Add(record.BackupPath);
            }
            if (record.AircraftContentGeneration is { } generation)
            {
                GatherHistoricalComponents(generation.InstallationComponents.Values, previous, backups);
                GatherHistoricalComponents(generation.ProductComponents.Values, previous, backups);
            }
        }
    }

    private static void GatherHistoricalComponents(IEnumerable<ContentComponentState> components,
        string previous, HashSet<string> backups)
    {
        foreach (var component in components)
        foreach (var file in component.Files)
        {
            var oldPath = ContentPatchPathSafety.ResolveTarget(previous, file.RelativePath, "Recorded historical file");
            if (!SamePath(file.TargetPath, oldPath))
                throw new InvalidDataException($"Historical ownership path differs: {file.RelativePath}.");
            if (!file.OriginalExisted) continue;
            if (file.OriginalSizeBytes is null || string.IsNullOrWhiteSpace(file.OriginalSha256))
                throw new InvalidDataException($"Incomplete historical backup: {file.RelativePath}.");
            VerifyFile(file.BackupPath, file.OriginalSizeBytes.Value, file.OriginalSha256);
            backups.Add(file.BackupPath);
        }
    }

    private static void RebaseInstallation(ContentInstallationToolState installation, string previous, string current)
    {
        installation.AircraftFolder = current;
        RebaseComponents(installation.ContentComponents.Values, previous, current);
        RebaseBackups(installation.Backups, previous, current);
    }

    private static void RebaseAircraft(AircraftToolState owner, string previous, string current)
    {
        owner.AircraftFolder = current;
        if (!string.IsNullOrWhiteSpace(owner.AcfPath)) owner.AcfPath = Rebase(owner.AcfPath, previous, current);
        if (!string.IsNullOrWhiteSpace(owner.PrefsPath)) owner.PrefsPath = Rebase(owner.PrefsPath, previous, current);
        RebaseComponents(owner.ContentComponents.Values, previous, current);
        RebaseBackups(owner.Backups, previous, current);
    }

    private static void RebaseComponents(IEnumerable<ContentComponentState> components, string previous, string current)
    {
        foreach (var component in components)
        foreach (var file in component.Files)
            file.TargetPath = Rebase(file.TargetPath, previous, current);
    }

    private static void RebaseBackups(IEnumerable<BackupRecord> records, string previous, string current)
    {
        foreach (var record in records)
        {
            record.SourcePath = Rebase(record.SourcePath, previous, current);
            if (record.AircraftContentGeneration is not { } generation) continue;
            RebaseComponents(generation.InstallationComponents.Values, previous, current);
            RebaseComponents(generation.ProductComponents.Values, previous, current);
        }
    }

    private static bool HasOtherOldOwnership(ToolStateDocument state, string previous) =>
        state.ToolInstallations.Values.Any(item => SamePath(item.XPlaneRoot, previous)
            || IsWithin(item.TargetPath, previous))
        || state.LiveryInstallations.Values.Any(item => IsWithin(item.DestinationDirectory, previous)
            || IsWithin(item.TargetPath, previous))
        || state.ResourceInstallations.Values.Any(item => IsWithin(item.DestinationDirectory, previous)
            || IsWithin(item.TargetPath, previous));

    private static string RecordedAircraftFolder(AircraftToolState state) =>
        !string.IsNullOrWhiteSpace(state.AircraftFolder) ? state.AircraftFolder
            : string.IsNullOrWhiteSpace(state.AcfPath) ? "" : Path.GetDirectoryName(state.AcfPath) ?? "";

    private static string? ProductFamily(string aircraftId) => AircraftProductIds.Normalize(aircraftId);

    private static string Rebase(string path, string previous, string current)
    {
        if (!IsWithin(path, previous)) throw new InvalidDataException($"State path is outside the former aircraft folder: {path}.");
        var relative = Path.GetRelativePath(previous, path);
        return relative == "." ? current : ContentPatchPathSafety.ResolveTarget(current, relative, "Moved aircraft state path");
    }

    private static bool IsWithin(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root)) return false;
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(path);
        return string.Equals(fullPath, fullRoot, PathComparison)
            || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, PathComparison);
    }

    private static bool SamePath(string left, string right) =>
        !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right)
        && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), PathComparison);

    private static bool IsLink(string path) =>
        (File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static void VerifyFile(string path, long size, string hash)
    {
        if (!File.Exists(path) || IsLink(path) || new FileInfo(path).Length != size || !HashEqual(HashFile(path), hash))
            throw new InvalidDataException($"Managed file or backup differs: {path}.");
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool HashEqual(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
