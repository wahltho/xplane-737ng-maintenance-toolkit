using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.App.Services;

public sealed record HistoryBackupLocation(string Path, string Status, string OriginalPath)
{
    public bool HasOriginalPath => !string.IsNullOrWhiteSpace(OriginalPath);
}

public sealed record InstallationHistoryEntry(DateTimeOffset? DateUtc, string Name, string Action,
    string Version, string Scope, string TargetPath, string BackupStatus, IReadOnlyList<HistoryBackupLocation> Backups)
{
    public string When => DateUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "No date saved";
    public bool HasBackups => Backups.Count > 0;
}

public sealed record InstallationHistorySummary(string AircraftFolder, string ReadAt, string Status,
    IReadOnlyList<InstallationHistoryEntry> Entries, IReadOnlyList<string> Notes)
{
    private const int EntryLimit = 1000;
    private const int ProbeLimit = 5000;

    public static InstallationHistorySummary Read(ToolStateStore store, string aircraftFolder, string? xPlaneRoot,
        IReadOnlyDictionary<string, string>? packageNames = null, CancellationToken cancellationToken = default)
    {
        var entries = new List<InstallationHistoryEntry>();
        var notes = new List<string>
        {
            "This shows the history MTK has saved. It may not include every change, especially patches installed manually.",
            "MTK checks whether these backups are still there. It does not check their contents here or confirm that they can be restored.",
            "To restore a package, use its Restore button. Keep MTK's state files and backups when troubleshooting."
        };
        var checkedPaths = new Dictionary<string, string>(StringComparer.Ordinal);
        var status = "History loaded";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsLinked(store.StatePath, store.RootPath))
                throw new IOException("MTK's state file or its folder is a symbolic link.");
            if (!File.Exists(store.StatePath)) return Result("No saved MTK history");
            if (new FileInfo(store.StatePath).Length > 16 * 1024 * 1024)
                throw new IOException("Installation state exceeds the 16 MiB display limit.");
            var document = store.Load(); // Normalizes legacy records in memory; never saves them.
            var installations = document.ContentInstallations.Values.Where(i => SamePath(i.AircraftFolder, aircraftFolder)).ToArray();
            var aircraft = document.Aircraft.Values.Where(a => SamePath(a.AircraftFolder, aircraftFolder)).ToArray();
            var authoritative = installations.FirstOrDefault(i => i.HasAuthoritativeContentState);
            var componentRecords = authoritative is null ? installations : new[] { authoritative };
            var components = componentRecords.SelectMany(i => i.ContentComponents).GroupBy(p => p.Key, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);
            // An authoritative empty generation must not resurrect older per-variant ownership.
            if (authoritative is null)
                foreach (var pair in aircraft.SelectMany(a => a.ContentComponents)) components.TryAdd(pair.Key, pair.Value);
            foreach (var pair in components)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var component = pair.Value;
                var backups = component.Files.Select(f => Backup(f.BackupPath, f.OriginalExisted,
                    originalPath: string.IsNullOrWhiteSpace(f.TargetPath) ? f.RelativePath : f.TargetPath)).Distinct().ToArray();
                var versions = component.Sources.Where(s => component.EnabledModules.Contains(s.ModuleId, StringComparer.Ordinal))
                    .Select(s => $"{s.ModuleId} {s.ReleaseTag}").ToArray();
                entries.Add(new(Date(component.LastOperationUtc) ?? Date(component.InstalledUtc), Name(pair.Key),
                    Operation(component.LastOperation), versions.Length > 0 ? string.Join(", ", versions) : component.PackageVersion,
                    "Aircraft patches", aircraftFolder,
                    component.RestoreAvailable ? component.Files.Count == 0 ? "MTK has no details about the original files."
                            : "Original file details are saved. MTK will check them before restoring."
                        : "An existing installation was added to MTK without an original backup. Restore is unavailable.", backups));
            }
            // Backup records are mirrored between installation and variant state. Show each only once.
            var backupsByOperation = installations.SelectMany(i => i.Backups).Concat(aircraft.SelectMany(a => a.Backups))
                .DistinctBy(b => (b.CreatedUtc, b.Operation, b.PackageId, b.PackageVersion, b.SourcePath, b.BackupPath, b.SourceExisted))
                .GroupBy(b => (b.CreatedUtc, b.Operation, b.PackageId, b.PackageVersion));
            foreach (var group in backupsByOperation)
            {
                cancellationToken.ThrowIfCancellationRequested();
                entries.Add(new(Date(group.Key.CreatedUtc), Name(group.Key.PackageId ?? "Aircraft / views / configuration"),
                    Operation(group.Key.Operation), group.Key.PackageVersion ?? "", "Backup", aircraftFolder,
                    $"Backups listed: {group.Count()}. Contents not checked.",
                    group.Select(b => Backup(b.BackupPath, b.SourceExisted,
                        directory: b.Operation == "AircraftUpdateFullDirectory", originalPath: b.SourcePath)).Distinct().ToArray()));
            }
            foreach (var a in aircraft)
            {
                AddEvent(a.LastAircraftUpdateUtc, "Aircraft package", a.InstalledAircraftUpdateVersion is null
                        ? "Last aircraft package change (installed version unknown)" : "Last aircraft update",
                    a.InstalledAircraftUpdateVersion ?? "", aircraftFolder);
                AddEvent(a.LastQuickViewAppliedUtc, a.AircraftId, "Last Quick View change", "", a.PrefsPath);
                AddEvent(a.LastDefaultViewAppliedUtc, a.AircraftId, "Last default viewpoint change", "", a.AcfPath);
                AddEvent(a.LastRestoreUtc, a.AircraftId, "Last view/configuration restore", "", a.PrefsPath);
            }
            foreach (var tool in document.ToolInstallations.Values.Where(t => SamePath(t.XPlaneRoot, aircraftFolder)
                         || (!string.IsNullOrWhiteSpace(xPlaneRoot) && SamePath(t.XPlaneRoot, xPlaneRoot))))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var scope = SamePath(tool.XPlaneRoot, aircraftFolder) ? "Aircraft component" : "Shared X-Plane tool";
                entries.Add(new(Date(tool.LastOperationUtc), Name(tool.PackageId), Operation(tool.LastOperation),
                    tool.InstalledVersion, scope, Join(tool.XPlaneRoot, tool.TargetPath),
                    $"Saved backups: {tool.Backups.Count}. See below.", []));
                foreach (var generation in tool.Backups)
                {
                    var locations = generation.OverlayFiles.Count > 0
                        ? generation.OverlayFiles.Select(f => Backup(f.OriginalExisted ? Join(generation.BackupPath, f.RelativePath) : "",
                            f.OriginalExisted, originalPath: f.RelativePath)).ToArray()
                        : new[] { Backup(generation.BackupPath, generation.SourceExisted, directory: true,
                            originalPath: Join(tool.XPlaneRoot, tool.TargetPath)) };
                    entries.Add(new(Date(generation.CreatedUtc), Name(tool.PackageId), "Earlier backup",
                        $"Previous: {DisplayVersion(generation.PreviousVersion)}; installed: {DisplayVersion(generation.InstalledVersion)}",
                        scope, Join(tool.XPlaneRoot, tool.TargetPath), "Files from before the change. Contents not checked.", locations));
                }
            }
            foreach (var livery in document.LiveryInstallations.Values.Where(l => SamePath(l.DestinationDirectory, Path.Combine(aircraftFolder, "liveries"))))
                entries.Add(new(Date(livery.LastOperationUtc), Name(livery.PackageId), "Last livery operation", livery.PackageVersion,
                    "Livery", livery.DestinationDirectory, "No livery backups are listed here.", []));
            if (entries.Count == 0) status = "No saved MTK history for this aircraft";
            if (entries.Count > EntryLimit) notes.Add($"Only the newest {EntryLimit} entries are displayed.");
            if (checkedPaths.Count >= ProbeLimit) notes.Add($"MTK checks up to {ProbeLimit:N0} backup locations here.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or System.Text.Json.JsonException)
        {
            status = "History incomplete";
            notes.Add("Could not load all of the history: " + ex.Message);
        }
        return Result(status);

        InstallationHistorySummary Result(string resultStatus) => new(aircraftFolder,
            DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss"), resultStatus,
            entries.DistinctBy(e => (e.DateUtc, e.Name, e.Action, e.Version, e.Scope, e.TargetPath))
                .OrderByDescending(e => e.DateUtc).Take(EntryLimit).ToArray(), notes);
        string Name(string id) => packageNames?.GetValueOrDefault(id) ?? id;
        void AddEvent(DateTimeOffset? date, string name, string action, string version, string path)
        {
            if (Date(date) is { } when) entries.Add(new(when, name, action, version, "Aircraft", path, "See the saved backups below.", []));
        }
        HistoryBackupLocation Backup(string path, bool originalExisted, bool directory = false, string originalPath = "")
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!originalExisted) return new(path, "Did not exist before the change. No backup needed.", originalPath);
            if (string.IsNullOrWhiteSpace(path)) return new(path, "Backup location unknown", originalPath);
            var key = (directory ? "directory:" : "file:") + path;
            if (!checkedPaths.TryGetValue(key, out var presence))
            {
                if (checkedPaths.Count >= ProbeLimit) return new(path, "Not checked (limit reached)", originalPath);
                try
                {
                    if (!Path.IsPathFullyQualified(path)) presence = "Not checked (backup path is incomplete)";
                    else if (IsLinked(path, store.BackupRootPath, store.RootPath)) presence = "Not checked (linked file or folder)";
                    else
                    {
                        var attributes = File.GetAttributes(path);
                        presence = attributes.HasFlag(FileAttributes.Directory) == directory
                            ? directory ? "Folder found; contents not checked" : "File found; contents not checked"
                            : directory ? "Expected a folder but found a file" : "Expected a file but found a folder";
                    }
                }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                {
                    presence = "Backup not found";
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    presence = "Could not check this backup: " + ex.Message;
                }
                checkedPaths[key] = presence;
            }
            return new(path, presence, originalPath);
        }
    }

    private static string DisplayVersion(string value) => string.IsNullOrWhiteSpace(value) ? "not recorded" : value;
    private static DateTimeOffset? Date(DateTimeOffset? value) => value is null || value == default(DateTimeOffset) ? null : value;
    private static bool SamePath(string left, string right) => !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right)
        && Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)).Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static string Join(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(root) || Path.IsPathRooted(relative)) return "Invalid recorded path";
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        return path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ? path : "Invalid recorded path";
    }
    private static bool IsLinked(string path, params string[] trustedRoots)
    {
        var fullPath = Path.GetFullPath(path);
        for (var current = fullPath; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            var file = new FileInfo(current);
            var dir = new DirectoryInfo(current);
            if (file.LinkTarget is not null || dir.LinkTarget is not null) return true;
            if (trustedRoots.Any(root => SamePath(current, root))) break;
        }
        return false;
    }
    private static string Operation(string value) => value switch
    {
        "ContentPatchInstall" => "Install patches",
        "ContentPatchUpdate" => "Update patches",
        "ContentPatchRepair" => "Repair patches",
        "ContentPatchUninstall" => "Remove patches",
        "ContentPatchRestore" => "Restore patches",
        "ContentPatchRestorePreImage" => "Backup before patch restore",
        "ApplyQuickViewCgAdapt" => "Adjust Quick Views",
        "ApplyQv0ToDefaultView" => "Set default viewpoint",
        "RestorePreImage" => "Backup before view restore",
        "AircraftUpdateRestorePreImage" => "Backup before aircraft restore",
        "AircraftUpdateFullDirectory" => "Complete aircraft folder backup",
        "AircraftUpdatePreImage" => "Files before aircraft update",
        "AircraftUpdateAddedFile" => "Files added by aircraft update",
        "AircraftUpdateDeletedFile" => "Files removed by aircraft update",
        "AircraftUpdateMetadata" => "Aircraft update metadata backup",
        "ConfigBackup" => "Save aircraft configuration",
        "ConfigRestorePreImage" => "Backup before configuration restore",
        "TransferLevelUpFleetViews" => "Copy views to LevelUp variants",
        "" => "Action not recorded",
        _ => value
    };
}
