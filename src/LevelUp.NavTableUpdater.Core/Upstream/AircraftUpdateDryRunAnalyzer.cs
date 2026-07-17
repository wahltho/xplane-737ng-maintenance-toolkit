using System.IO.Compression;
using LevelUp.NavTableUpdater.Core.Aircraft;

namespace LevelUp.NavTableUpdater.Core.Upstream;

public sealed class AircraftUpdateDryRunAnalyzer
{
    private static readonly string[] ProtectedFileNames =
    [
        "b738_config.txt",
        "b738x.cfg"
    ];

    public AircraftUpdateDryRunResult Analyze(
        string aircraftFolder,
        IEnumerable<AircraftUpdatePackageCacheEntry> cachedPackages)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aircraftFolder);
        ArgumentNullException.ThrowIfNull(cachedPackages);

        var targetRoot = Path.GetFullPath(aircraftFolder);
        var entries = new List<AircraftUpdateDryRunEntry>();
        var findings = new List<string>
        {
            "Dry-run only. No aircraft files are extracted, backed up, or changed."
        };

        if (!Directory.Exists(targetRoot))
        {
            findings.Add($"Aircraft folder is missing: {targetRoot}");
            return new AircraftUpdateDryRunResult(false, "Aircraft folder is missing.", entries, findings);
        }

        foreach (var cachedPackage in cachedPackages)
        {
            if (!cachedPackage.IsCached || !File.Exists(cachedPackage.CachePath))
            {
                findings.Add($"Package is not cached: {cachedPackage.Package.FileName}");
                continue;
            }

            AnalyzePackage(targetRoot, cachedPackage, entries, findings);
        }

        var blockedCount = entries.Count(entry => entry.Action == AircraftUpdateDryRunEntryAction.BlockedUnsafePath);
        var summary = blockedCount > 0
            ? $"Aircraft update dry-run found {blockedCount} unsafe path(s); install would be blocked."
            : $"Aircraft update dry-run: {entries.Count(entry => entry.Action == AircraftUpdateDryRunEntryAction.Add)} add, {entries.Count(entry => entry.Action == AircraftUpdateDryRunEntryAction.Replace)} replace, {entries.Count(entry => entry.Action is AircraftUpdateDryRunEntryAction.PreserveProtectedLocalFile or AircraftUpdateDryRunEntryAction.PreserveToolkitMetadata)} protected.";

        return new AircraftUpdateDryRunResult(blockedCount == 0, summary, entries, findings);
    }

    private static void AnalyzePackage(
        string targetRoot,
        AircraftUpdatePackageCacheEntry cachedPackage,
        ICollection<AircraftUpdateDryRunEntry> entries,
        ICollection<string> findings)
    {
        try
        {
            using var archive = ZipFile.OpenRead(cachedPackage.CachePath);
            findings.Add($"Opened {cachedPackage.Package.FileName}: {archive.Entries.Count} ZIP entr{(archive.Entries.Count == 1 ? "y" : "ies")}.");

            foreach (var zipEntry in archive.Entries)
            {
                if (IsDirectoryEntry(zipEntry))
                {
                    continue;
                }

                AnalyzeZipEntry(targetRoot, cachedPackage.Package.FileName, zipEntry, entries);
            }
        }
        catch (InvalidDataException ex)
        {
            findings.Add($"{cachedPackage.Package.FileName} is not a readable ZIP archive: {ex.Message}");
        }
    }

    private static void AnalyzeZipEntry(
        string targetRoot,
        string packageFileName,
        ZipArchiveEntry zipEntry,
        ICollection<AircraftUpdateDryRunEntry> entries)
    {
        var normalizedPath = NormalizeZipPath(zipEntry.FullName);
        if (normalizedPath is null)
        {
            entries.Add(new AircraftUpdateDryRunEntry(
                packageFileName,
                zipEntry.FullName,
                AircraftUpdateDryRunEntryAction.BlockedUnsafePath,
                zipEntry.Length,
                "ZIP entry path is absolute, empty, or contains path traversal."));
            return;
        }

        var targetPath = Path.GetFullPath(Path.Combine(targetRoot, normalizedPath));
        if (!targetPath.StartsWith(targetRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(targetPath, targetRoot, StringComparison.Ordinal))
        {
            entries.Add(new AircraftUpdateDryRunEntry(
                packageFileName,
                normalizedPath,
                AircraftUpdateDryRunEntryAction.BlockedUnsafePath,
                zipEntry.Length,
                "ZIP entry resolves outside the aircraft folder."));
            return;
        }

        var action = ClassifyAction(normalizedPath, targetPath);
        entries.Add(new AircraftUpdateDryRunEntry(
            packageFileName,
            normalizedPath,
            action,
            zipEntry.Length,
            BuildDetail(action, targetPath)));
    }

    private static AircraftUpdateDryRunEntryAction ClassifyAction(string relativePath, string targetPath)
    {
        if (string.Equals(Path.GetFileName(relativePath), AircraftMaintenanceMetadata.FileName, StringComparison.OrdinalIgnoreCase))
        {
            return AircraftUpdateDryRunEntryAction.PreserveToolkitMetadata;
        }

        if (IsProtectedLocalFile(relativePath))
        {
            return AircraftUpdateDryRunEntryAction.PreserveProtectedLocalFile;
        }

        return File.Exists(targetPath)
            ? AircraftUpdateDryRunEntryAction.Replace
            : AircraftUpdateDryRunEntryAction.Add;
    }

    private static string BuildDetail(AircraftUpdateDryRunEntryAction action, string targetPath) =>
        action switch
        {
            AircraftUpdateDryRunEntryAction.Add => $"Would add {targetPath}.",
            AircraftUpdateDryRunEntryAction.Replace => $"Would replace {targetPath} after backup.",
            AircraftUpdateDryRunEntryAction.PreserveProtectedLocalFile => $"Protected local preference/config file would not be overwritten: {targetPath}.",
            AircraftUpdateDryRunEntryAction.PreserveToolkitMetadata => $"Toolkit metadata is owned locally and would not be overwritten: {targetPath}.",
            AircraftUpdateDryRunEntryAction.BlockedUnsafePath => "Unsafe ZIP path.",
            _ => "Not classified."
        };

    private static bool IsProtectedLocalFile(string relativePath)
    {
        var fileName = Path.GetFileName(relativePath);
        if (ProtectedFileNames.Any(name => string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (fileName.EndsWith("_prefs.txt", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("_vrconfig.txt", StringComparison.OrdinalIgnoreCase)
            || (fileName.StartsWith("X-Camera_", StringComparison.OrdinalIgnoreCase) && fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var normalized = relativePath.Replace('\\', '/');
        return normalized.StartsWith("Output/preferences/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/Output/preferences/", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeZipPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var normalized = path.Replace('\\', '/').Trim();
        if (normalized.Length == 0 || normalized.StartsWith('/') || Path.IsPathRooted(normalized))
        {
            return null;
        }

        var segments = normalized
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .ToArray();

        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            return null;
        }

        return string.Join(Path.DirectorySeparatorChar, segments);
    }

    private static bool IsDirectoryEntry(ZipArchiveEntry entry) =>
        entry.FullName.EndsWith("/", StringComparison.Ordinal)
            || (string.IsNullOrEmpty(entry.Name) && entry.Length == 0);
}
