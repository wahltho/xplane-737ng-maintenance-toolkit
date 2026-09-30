using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Diagnostics;

public sealed class DiagnosticReportExporter
{
    private const long HashBudget = 512L * 1024 * 1024;
    private const int MaxChecks = 5000;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string Export(DiagnosticExportContext context, ToolStateStore store, string destinationFolder,
        bool anonymizePaths = true, CancellationToken cancellationToken = default)
    {
        var report = Collect(context, store, anonymizePaths, cancellationToken);
        var privacy = new DiagnosticPrivacyFilter(anonymizePaths);
        privacy.AddPath(context.AircraftFolder, "[AIRCRAFT]");
        privacy.AddPath(context.PreviousAircraftFolder, "[PREVIOUS_AIRCRAFT]");
        privacy.AddPath(context.XPlaneRoot, "[XPLANE]");
        privacy.AddPath(store.BackupRootPath, "[BACKUPS]");
        privacy.AddPath(store.RootPath, "[TOOLKIT_DATA]");
        privacy.AddPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "[HOME]");
        privacy.AddPath(destinationFolder, "[DIAGNOSTICS]");
        for (var i = 0; i < report.RecordedAircraft.Count; i++)
            privacy.AddPath(report.RecordedAircraft[i].Folder, $"[RECORDED_AIRCRAFT_{i + 1}]");

        string jsonText, summary, log;
        try
        {
            var json = JsonSerializer.SerializeToNode(report, JsonOptions)!;
            privacy.FilterJson(json);
            jsonText = json.ToJsonString(JsonOptions);
            summary = privacy.Filter(BuildSummary(report));
            log = privacy.Filter($"Install Log\n{context.InstallLog}\n\nOperation Log\n{context.OperationLog}\n");
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException ex)
        {
            // Never fall back to exporting unfiltered data after a privacy failure.
            throw new InvalidOperationException("Diagnostic privacy filtering timed out. No archive was written.", ex);
        }
        cancellationToken.ThrowIfCancellationRequested();

        var destination = Path.GetFullPath(destinationFolder);
        Directory.CreateDirectory(destination);
        var path = Path.Combine(destination,
            $"xplane-737ng-diagnostics-{report.ExportedUtc:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip");
        var temporary = path + ".tmp";
        try
        {
            using (var archive = new ZipArchive(new FileStream(temporary, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None), ZipArchiveMode.Create))
            {
                WriteEntry(archive, "report.txt", summary);
                WriteEntry(archive, "diagnostics.json", jsonText);
                WriteEntry(archive, "operation-log.txt", log);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path);
            return path;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public DiagnosticReport Collect(DiagnosticExportContext context, ToolStateStore store,
        bool anonymizePaths = true, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var report = new DiagnosticReport
        {
            Context = context, PathsAnonymized = anonymizePaths,
            OperatingSystem = RuntimeInformation.OSDescription,
            Runtime = RuntimeInformation.FrameworkDescription,
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            StatePath = store.StatePath, BackupRoot = store.BackupRootPath
        };
        var checks = new FileChecks(report, cancellationToken);
        foreach (var pair in context.KeyFiles)
            checks.AddRelative("Aircraft identity", pair.Key, context.AircraftFolder, pair.Value, null, null);

        ToolStateDocument state;
        try
        {
            if (IsLinked(store.StatePath, store.RootPath))
                throw new InvalidDataException("The state file or its data folder is a symbolic link.");
            if (File.Exists(store.StatePath) && new FileInfo(store.StatePath).Length > 16 * 1024 * 1024)
                throw new InvalidDataException("State file exceeds the diagnostic read limit (16 MiB).");
            report.StateStatus = File.Exists(store.StatePath) ? "Loaded" : "Not present";
            state = store.Load();
            report.StateSchemaVersion = state.SchemaVersion;
        }
        catch (Exception ex) when (IsReadError(ex))
        {
            report.StateStatus = "Could not read";
            report.Warnings.Add($"State: {ex.Message}");
            return report;
        }

        foreach (var installation in state.ContentInstallations.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            report.RecordedAircraft.Add(new(installation.AircraftFolder,
                Directory.Exists(installation.AircraftFolder), "", [.. installation.ContentComponents.Keys]));
            if (!IsSelectedFolder(installation.AircraftFolder, context)) continue;
            AddComponents(report, checks, installation.ContentComponents.Values, installation.AircraftFolder, "Installation", store);
            AddBackups(report, checks, installation.Backups, store);
        }
        foreach (var aircraft in state.Aircraft.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = string.IsNullOrWhiteSpace(aircraft.AircraftFolder)
                ? Path.GetDirectoryName(aircraft.AcfPath) ?? "" : aircraft.AircraftFolder;
            var recordedIndex = report.RecordedAircraft.FindIndex(a => SamePath(a.Folder, root));
            if (recordedIndex < 0)
                report.RecordedAircraft.Add(new(root, Directory.Exists(root),
                    aircraft.InstalledAircraftUpdateVersion ?? "", [.. aircraft.ContentComponents.Keys]));
            else
            {
                var recorded = report.RecordedAircraft[recordedIndex];
                report.RecordedAircraft[recordedIndex] = recorded with
                {
                    InstalledUpdateVersion = aircraft.InstalledAircraftUpdateVersion ?? recorded.InstalledUpdateVersion,
                    ComponentIds = recorded.ComponentIds.Concat(aircraft.ContentComponents.Keys).Distinct(StringComparer.Ordinal).ToArray()
                };
            }
            if (!IsSelectedFolder(root, context)) continue;
            AddComponents(report, checks, aircraft.ContentComponents.Values, root, "Product/variant", store);
            AddBackups(report, checks, aircraft.Backups, store);
        }
        foreach (var tool in state.ToolInstallations.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // ToolInstallations also includes components scoped to the aircraft folder.
            if (!IsSelectedFolder(tool.XPlaneRoot, context) && !SamePath(tool.XPlaneRoot, context.XPlaneRoot)) continue;
            report.Components.Add(new("Tool/component", tool.PackageId, tool.InstalledVersion,
                tool.TargetPath, [], [$"Channel: {tool.Channel}"], tool.Backups.Count > 0));
            foreach (var file in tool.InstalledFiles.Where(f => !f.Protected))
                checks.AddRelative("Tool file", tool.PackageId, tool.TargetPath, file.RelativePath, file.Size, file.Sha256);
            foreach (var relative in tool.RetiredFiles)
                checks.AddRelative("Retired tool file", tool.PackageId, tool.TargetPath, relative, null, null, absent: true);
            foreach (var backup in tool.Backups)
            {
                if (backup.SourceExisted || backup.OverlayFiles.Any(f => f.OriginalExisted))
                    checks.AddBackup("Tool backup directory", tool.PackageId, backup.BackupPath, null, null, store, directory: true);
                foreach (var file in backup.OverlayFiles.Where(f => f.OriginalExisted))
                    checks.AddRelative("Tool overlay backup", tool.PackageId, backup.BackupPath,
                        file.RelativePath, file.OriginalSize, file.OriginalSha256);
            }
        }
        return report;
    }

    private static void AddComponents(DiagnosticReport report, FileChecks checks,
        IEnumerable<ContentComponentState> components, string root, string kind, ToolStateStore store, bool historical = false)
    {
        foreach (var component in components)
        {
            if (!historical)
                report.Components.Add(new(kind, component.ComponentId, component.PackageVersion,
                    root, [.. component.EnabledModules],
                    component.Sources.Select(s => $"{s.ModuleId}: {s.PackageId} {s.ReleaseTag} SHA-256={s.AssetSha256}").ToArray(),
                    component.RestoreAvailable));
            foreach (var file in component.Files)
            {
                if (!historical)
                    checks.AddRelative("Managed file snapshot", component.ComponentId, root,
                        file.RelativePath, file.InstalledSizeBytes, file.InstalledSha256,
                        absent: file.InstalledSha256 is null && file.InstalledSizeBytes is null);
                if (file.OriginalExisted)
                    checks.AddBackup("Original file backup", component.ComponentId, file.BackupPath,
                        file.OriginalSizeBytes, file.OriginalSha256, store);
            }
            if (!historical)
                foreach (var scope in component.Scopes)
                    checks.AddRelative("Managed scope directory", component.ComponentId, root,
                        scope.RelativePath, null, null, directory: true);
        }
    }

    private static void AddBackups(DiagnosticReport report, FileChecks checks,
        IEnumerable<BackupRecord> backups, ToolStateStore store)
    {
        foreach (var backup in backups)
        {
            if (backup.SourceExisted)
                checks.AddBackup("Operation backup", backup.PackageId ?? backup.Operation,
                    backup.BackupPath, backup.SourceSizeBytes, backup.SourceSha256, store,
                    directory: backup.SourceSizeBytes is null && backup.SourceSha256 is null
                        && Directory.Exists(backup.BackupPath));
            if (backup.AircraftContentGeneration is not { } generation) continue;
            var root = Directory.Exists(backup.BackupPath) ? backup.BackupPath : Path.GetDirectoryName(backup.SourcePath) ?? "";
            AddComponents(report, checks, generation.InstallationComponents.Values, root, "Historical", store, historical: true);
            AddComponents(report, checks, generation.ProductComponents.Values, root, "Historical", store, historical: true);
        }
    }

    private static bool IsSelectedFolder(string path, DiagnosticExportContext context) =>
        SamePath(path, context.AircraftFolder) || SamePath(path, context.PreviousAircraftFolder);

    private static bool SamePath(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        try
        {
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (IsReadError(ex)) { return false; }
    }

    private static bool IsLinked(string path, string? root = null)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if (new FileInfo(current).LinkTarget is not null || new DirectoryInfo(current).LinkTarget is not null)
                return true;
            if (root is not null && SamePath(current, root)) break;
            var parent = Path.GetDirectoryName(current);
            if (parent == current) break;
            current = parent ?? "";
        }
        return false;
    }

    private static bool IsReadError(Exception ex) => ex is IOException or UnauthorizedAccessException
        or InvalidOperationException or ArgumentException or JsonException or NotSupportedException;

    private static void WriteEntry(ZipArchive archive, string name, string contents)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        writer.Write(contents);
    }

    private static string BuildSummary(DiagnosticReport report)
    {
        var text = new StringBuilder("X-Plane 737NG Maintenance Toolkit diagnostic report\n");
        text.AppendLine($"Exported (UTC): {report.ExportedUtc:O}");
        text.AppendLine($"Toolkit: {report.Context.ToolkitVersion}; catalog: {report.Context.CatalogVersion}");
        text.AppendLine($"System: {report.OperatingSystem}; {report.ProcessArchitecture}; {report.Runtime}");
        text.AppendLine($"Paths anonymized: {report.PathsAnonymized}");
        text.AppendLine($"Product: {report.Context.Product}; folder: {report.Context.AircraftFolder}");
        text.AppendLine($"Aircraft installed: {report.Context.InstalledAircraftVersion}; available: {report.Context.AvailableAircraftVersion}");
        text.AppendLine($"State: {report.StateStatus}; schema: {report.StateSchemaVersion}; {report.StatePath}");
        text.AppendLine($"Backup root: {report.BackupRoot}\n");
        foreach (var status in report.Context.Status) text.AppendLine($"{status.Key}: {status.Value}");
        text.AppendLine("\nPackages (last displayed status):");
        foreach (var package in report.Context.Packages)
            text.AppendLine($"- {package.PackageId}: installed={package.InstalledVersion}; available={package.AvailableVersion}; {package.Status}");
        text.AppendLine("\nRecorded components:");
        foreach (var component in report.Components)
        {
            text.AppendLine($"- {component.PackageId} {component.Version}: {component.Root}; modules={string.Join(", ", component.EnabledModules)}; restore recorded={component.RestoreRecorded}");
            foreach (var source in component.Sources) text.AppendLine($"  {source}");
        }
        text.AppendLine("\nAircraft folders recorded by MTK (useful after a move):");
        foreach (var aircraft in report.RecordedAircraft)
            text.AppendLine($"- {aircraft.Folder}: folder exists={aircraft.DirectoryExists}; update version={aircraft.InstalledUpdateVersion}; components={string.Join(", ", aircraft.ComponentIds)}");
        text.AppendLine("\nFile and backup checks:");
        foreach (var file in report.Files)
        {
            text.AppendLine($"- [{file.Status}] {file.Kind} / {file.Owner}: {file.Path}");
            text.AppendLine($"  Expected: {file.ExpectedSize} bytes; SHA-256={file.ExpectedSha256 ?? "not recorded"}");
            text.AppendLine($"  Observed: {file.ActualSize} bytes; SHA-256={file.ActualSha256 ?? "not checked"}; {file.Detail}");
        }
        text.AppendLine("\nFindings and warnings:");
        foreach (var warning in report.Context.Findings.Concat(report.Warnings)) text.AppendLine($"- {warning}");
        text.AppendLine("\nWhat this report does not prove:");
        foreach (var limitation in report.Limitations) text.AppendLine($"- {limitation}");
        return text.ToString();
    }

    private sealed class FileChecks(DiagnosticReport report, CancellationToken cancellationToken)
    {
        private long _remainingBytes = HashBudget;
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

        public void AddRelative(string kind, string owner, string root, string relative,
            long? size, string? hash, bool absent = false, bool directory = false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (LimitReached()) return;
            try
            {
                if (string.IsNullOrWhiteSpace(root)) return;
                var path = ContentPatchPathSafety.ResolveTarget(root, relative, "Diagnostic file");
                Add(kind, owner, path, size, hash, root, absent, directory);
            }
            catch (Exception ex) when (IsReadError(ex))
            {
                report.Files.Add(new(kind, owner, relative, size, hash, null, null, "UnsafePath", ex.Message));
            }
        }

        public void AddBackup(string kind, string owner, string path, long? size, string? hash,
            ToolStateStore? store, bool directory = false)
        {
            // State references are read for diagnostics only, never adopted or rewritten.
            string? root = null;
            if (store is not null && !string.IsNullOrWhiteSpace(path))
            {
                try
                {
                    var full = Path.GetFullPath(path);
                    root = new[] { store.BackupRootPath, store.RootPath }.FirstOrDefault(r =>
                        SamePath(full, r) || full.StartsWith(Path.TrimEndingDirectorySeparator(r) + Path.DirectorySeparatorChar,
                            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));
                }
                catch (Exception ex) when (IsReadError(ex)) { /* Add records the invalid path below. */ }
            }
            Add(kind, owner, path, size, hash, root, directory: directory);
        }

        private void Add(string kind, string owner, string path, long? size, string? hash,
            string? root, bool absent = false, bool directory = false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = $"{kind}|{owner}|{path}|{size}|{hash}";
            if (!_seen.Add(key)) return;
            if (LimitReached()) return;
            try
            {
                if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("No path recorded.");
                if (IsLinked(path, root))
                {
                    report.Files.Add(new(kind, owner, path, size, hash, null, null, "SymlinkNotRead"));
                    return;
                }
                if (absent)
                {
                    report.Files.Add(new(kind, owner, path, size, hash, null, null,
                        File.Exists(path) || Directory.Exists(path) ? "UnexpectedlyPresent" : "AbsentAsRecorded"));
                    return;
                }
                if (directory)
                {
                    report.Files.Add(new(kind, owner, path, size, hash, null, null,
                        Directory.Exists(path) ? "DirectoryPresentNotHashVerified" : "MissingDirectory"));
                    return;
                }
                if (!File.Exists(path))
                {
                    report.Files.Add(new(kind, owner, path, size, hash, null, null,
                        Directory.Exists(path) ? "UnexpectedDirectory" : "Missing"));
                    return;
                }
                var before = new FileInfo(path);
                var length = before.Length;
                var lastWrite = before.LastWriteTimeUtc;
                if (length > _remainingBytes)
                {
                    report.Files.Add(new(kind, owner, path, size, hash, length, null, "HashBudgetExceeded"));
                    return;
                }
                _remainingBytes -= length;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    65536, FileOptions.SequentialScan);
                using var algorithm = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[65536];
                int count;
                long read = 0;
                while ((count = stream.Read(buffer)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    read += count;
                    if (read > length) throw new IOException("File grew while the report was being collected.");
                    algorithm.AppendData(buffer, 0, count);
                }
                var actualHash = Convert.ToHexString(algorithm.GetHashAndReset()).ToLowerInvariant();
                var after = new FileInfo(path);
                var status = read != length || after.Length != length || after.LastWriteTimeUtc != lastWrite
                    ? "ChangedDuringRead"
                    : hash is null ? "ObservedWithoutExpectedHash"
                    : (size is null || size == length) && string.Equals(hash, actualHash, StringComparison.OrdinalIgnoreCase)
                        ? "MatchesRecordedHash" : "DiffersFromRecordedHash";
                report.Files.Add(new(kind, owner, path, size, hash, length, actualHash, status));
            }
            catch (Exception ex) when (IsReadError(ex))
            {
                report.Files.Add(new(kind, owner, path, size, hash, null, null, "CouldNotRead", ex.Message));
            }
        }

        private bool LimitReached()
        {
            if (report.Files.Count < MaxChecks) return false;
            const string warning = "File check limit reached (5000). Remaining checks were skipped.";
            if (!report.Warnings.Contains(warning)) report.Warnings.Add(warning);
            return true;
        }
    }
}
