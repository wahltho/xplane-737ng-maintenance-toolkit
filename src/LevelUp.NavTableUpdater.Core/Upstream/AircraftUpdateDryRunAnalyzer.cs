using System.Security.Cryptography;
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
        var packageLiveryRoots = new HashSet<string>(StringComparerForCurrentPlatform());
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
                findings.Add(cachedPackage.ValidationError ?? $"Package is not cached: {cachedPackage.Package.FileName}");
                entries.Add(new AircraftUpdateDryRunEntry(
                    cachedPackage.Package.FileName,
                    cachedPackage.Package.FileName,
                    AircraftUpdateDryRunEntryAction.BlockedInvalidPackage,
                    cachedPackage.SizeBytes ?? 0,
                    cachedPackage.ValidationError ?? "Package is missing or failed integrity validation."));
                continue;
            }

            AnalyzePackage(targetRoot, cachedPackage, entries, findings, packageLiveryRoots);
        }

        AddPreservedLocalLiveryEntries(targetRoot, packageLiveryRoots, entries, findings);

        var blockedCount = entries.Count(entry => entry.Action is AircraftUpdateDryRunEntryAction.BlockedUnsafePath
            or AircraftUpdateDryRunEntryAction.BlockedInvalidPackage);
        var summary = blockedCount > 0
            ? $"Aircraft update dry-run found {blockedCount} blocking package issue(s); install is disabled."
            : $"Aircraft update dry-run: {entries.Count(entry => entry.Action == AircraftUpdateDryRunEntryAction.Add)} add, "
                + $"{entries.Count(entry => entry.Action == AircraftUpdateDryRunEntryAction.Replace)} replace, "
                + $"{entries.Count(entry => entry.Action == AircraftUpdateDryRunEntryAction.Delete)} delete, "
                + $"{entries.Count(entry => entry.Action is AircraftUpdateDryRunEntryAction.PreserveProtectedLocalFile or AircraftUpdateDryRunEntryAction.PreserveToolkitMetadata)} protected"
                + (entries.All(entry => entry.Action != AircraftUpdateDryRunEntryAction.PreserveLocalLivery)
                    ? "."
                    : $", {entries.Count(entry => entry.Action == AircraftUpdateDryRunEntryAction.PreserveLocalLivery)} local livery preserved.");

        return new AircraftUpdateDryRunResult(blockedCount == 0, summary, entries, findings);
    }

    private static void AnalyzePackage(
        string targetRoot,
        AircraftUpdatePackageCacheEntry cachedPackage,
        ICollection<AircraftUpdateDryRunEntry> entries,
        ICollection<string> findings,
        ISet<string> packageLiveryRoots)
    {
        try
        {
            using var archive = AircraftPackageArchive.Open(cachedPackage.CachePath);
            var fileEntries = archive.Entries.Where(entry => !entry.IsDirectory).ToArray();
            findings.Add($"Opened {cachedPackage.Package.FileName}: {fileEntries.Length} archive file entr{(fileEntries.Length == 1 ? "y" : "ies")}.");

            if (cachedPackage.Package.Manifest is null)
            {
                AnalyzeLegacyArchive(targetRoot, cachedPackage.Package.FileName, fileEntries, entries, packageLiveryRoots);
                return;
            }

            AnalyzeManifestArchive(targetRoot, cachedPackage.Package, fileEntries, entries, findings, packageLiveryRoots);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            findings.Add($"{cachedPackage.Package.FileName} is not a readable aircraft package archive: {ex.Message}");
            entries.Add(new AircraftUpdateDryRunEntry(
                cachedPackage.Package.FileName,
                cachedPackage.Package.FileName,
                AircraftUpdateDryRunEntryAction.BlockedInvalidPackage,
                cachedPackage.SizeBytes ?? 0,
                $"Package archive could not be validated: {ex.Message}"));
        }
    }

    private static void AnalyzeLegacyArchive(
        string targetRoot,
        string packageFileName,
        IEnumerable<AircraftPackageArchiveEntry> archiveEntries,
        ICollection<AircraftUpdateDryRunEntry> entries,
        ISet<string> packageLiveryRoots)
    {
        foreach (var archiveEntry in archiveEntries)
        {
            var normalizedPath = AircraftUpdatePath.NormalizeRelativePath(archiveEntry.Path);
            AnalyzeWriteEntry(targetRoot, packageFileName, archiveEntry.Path, normalizedPath, archiveEntry.Size, entries, packageLiveryRoots);
        }
    }

    private static void AnalyzeManifestArchive(
        string targetRoot,
        AircraftUpdatePackage package,
        IEnumerable<AircraftPackageArchiveEntry> archiveEntries,
        ICollection<AircraftUpdateDryRunEntry> entries,
        ICollection<string> findings,
        ISet<string> packageLiveryRoots)
    {
        var manifest = package.Manifest!;
        var manifestFiles = manifest.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var verified = 0;

        foreach (var archiveEntry in archiveEntries)
        {
            var relativePath = AircraftUpdatePath.MapArchivePath(archiveEntry.Path, manifest.ContentRoot);
            if (relativePath is null)
            {
                entries.Add(new AircraftUpdateDryRunEntry(
                    package.FileName,
                    archiveEntry.Path,
                    AircraftUpdateDryRunEntryAction.BlockedUnsafePath,
                    archiveEntry.Size,
                    $"Archive entry is outside declared contentRoot '{manifest.ContentRoot}' or has an unsafe path."));
                continue;
            }

            if (!seenPaths.Add(relativePath))
            {
                entries.Add(new AircraftUpdateDryRunEntry(
                    package.FileName,
                    relativePath,
                    AircraftUpdateDryRunEntryAction.BlockedInvalidPackage,
                    archiveEntry.Size,
                    "Archive contains the same manifest path more than once."));
                continue;
            }

            if (!manifestFiles.TryGetValue(relativePath, out var manifestFile))
            {
                entries.Add(new AircraftUpdateDryRunEntry(
                    package.FileName,
                    relativePath,
                    AircraftUpdateDryRunEntryAction.BlockedInvalidPackage,
                    archiveEntry.Size,
                    "Archive entry is not declared in the package manifest."));
                continue;
            }

            var integrityError = VerifyArchiveEntry(archiveEntry, manifestFile);
            if (integrityError is not null)
            {
                entries.Add(new AircraftUpdateDryRunEntry(
                    package.FileName,
                    relativePath,
                    AircraftUpdateDryRunEntryAction.BlockedInvalidPackage,
                    archiveEntry.Size,
                    integrityError));
                continue;
            }

            verified++;
            AnalyzeWriteEntry(targetRoot, package.FileName, archiveEntry.Path, relativePath, archiveEntry.Size, entries, packageLiveryRoots);
        }

        foreach (var missing in manifest.Files.Where(file => !seenPaths.Contains(file.Path)))
        {
            entries.Add(new AircraftUpdateDryRunEntry(
                package.FileName,
                missing.Path,
                AircraftUpdateDryRunEntryAction.BlockedInvalidPackage,
                missing.Size,
                "Manifest file is missing from the archive."));
        }

        foreach (var deletedPath in manifest.DeletedPaths)
        {
            AnalyzeDeleteEntry(targetRoot, package.FileName, deletedPath, entries);
        }

        findings.Add($"Verified {verified} archive payload file(s) against manifest size and SHA-256.");
        findings.Add($"Manifest declares {manifest.DeletedPaths.Count} deletion(s).");
    }

    private static void AnalyzeWriteEntry(
        string targetRoot,
        string packageFileName,
        string sourcePath,
        string? normalizedPath,
        long size,
        ICollection<AircraftUpdateDryRunEntry> entries,
        ISet<string> packageLiveryRoots)
    {
        if (normalizedPath is null)
        {
            entries.Add(new AircraftUpdateDryRunEntry(
                packageFileName,
                sourcePath,
                AircraftUpdateDryRunEntryAction.BlockedUnsafePath,
                size,
                "Archive entry path is absolute, empty, or contains path traversal."));
            return;
        }

        var liveryRoot = GetLiveryRoot(normalizedPath);
        if (liveryRoot is not null)
        {
            packageLiveryRoots.Add(liveryRoot);
        }

        string targetPath;
        try
        {
            targetPath = AircraftUpdatePath.ResolveTargetPath(targetRoot, normalizedPath);
        }
        catch (InvalidOperationException ex)
        {
            entries.Add(new AircraftUpdateDryRunEntry(
                packageFileName,
                normalizedPath,
                AircraftUpdateDryRunEntryAction.BlockedUnsafePath,
                size,
                ex.Message));
            return;
        }

        var action = ClassifyWriteAction(normalizedPath, targetPath);
        entries.Add(new AircraftUpdateDryRunEntry(
            packageFileName,
            normalizedPath,
            action,
            size,
            BuildDetail(action, normalizedPath, targetPath)));
    }

    private static void AnalyzeDeleteEntry(
        string targetRoot,
        string packageFileName,
        string relativePath,
        ICollection<AircraftUpdateDryRunEntry> entries)
    {
        if (IsProtectedLocalFile(relativePath)
            || string.Equals(Path.GetFileName(relativePath), AircraftMaintenanceMetadata.FileName, StringComparison.OrdinalIgnoreCase))
        {
            entries.Add(new AircraftUpdateDryRunEntry(
                packageFileName,
                relativePath,
                AircraftUpdateDryRunEntryAction.BlockedInvalidPackage,
                0,
                "Manifest attempts to delete a protected local or toolkit-owned file."));
            return;
        }

        try
        {
            var targetPath = AircraftUpdatePath.ResolveTargetPath(targetRoot, relativePath);
            var exists = File.Exists(targetPath);
            entries.Add(new AircraftUpdateDryRunEntry(
                packageFileName,
                relativePath,
                exists ? AircraftUpdateDryRunEntryAction.Delete : AircraftUpdateDryRunEntryAction.AlreadyAbsent,
                exists ? new FileInfo(targetPath).Length : 0,
                exists ? $"Would back up and delete {targetPath}." : $"Declared deletion is already absent: {targetPath}."));
        }
        catch (InvalidOperationException ex)
        {
            entries.Add(new AircraftUpdateDryRunEntry(
                packageFileName,
                relativePath,
                AircraftUpdateDryRunEntryAction.BlockedUnsafePath,
                0,
                ex.Message));
        }
    }

    private static string? VerifyArchiveEntry(AircraftPackageArchiveEntry entry, AircraftUpdateManifestFile manifestFile)
    {
        if (entry.Size != manifestFile.Size)
        {
            return $"Payload size differs from manifest: expected {manifestFile.Size}, got {entry.Size}.";
        }

        using var stream = entry.Open();
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return string.Equals(hash, manifestFile.Sha256, StringComparison.OrdinalIgnoreCase)
            ? null
            : "Payload SHA-256 differs from the package manifest.";
    }

    private static AircraftUpdateDryRunEntryAction ClassifyWriteAction(string relativePath, string targetPath)
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

    private static string BuildDetail(AircraftUpdateDryRunEntryAction action, string relativePath, string targetPath) =>
        action switch
        {
            AircraftUpdateDryRunEntryAction.Add when IsLiveryPath(relativePath) => $"Would add package-owned livery file {targetPath}.",
            AircraftUpdateDryRunEntryAction.Add => $"Would add {targetPath}.",
            AircraftUpdateDryRunEntryAction.Replace when IsLiveryPath(relativePath) => $"Would replace package-owned livery file {targetPath} after backup; local changes to this package-owned file would be overwritten.",
            AircraftUpdateDryRunEntryAction.Replace => $"Would replace {targetPath} after backup.",
            AircraftUpdateDryRunEntryAction.PreserveProtectedLocalFile => $"Protected local preference/config file would not be overwritten: {targetPath}.",
            AircraftUpdateDryRunEntryAction.PreserveToolkitMetadata => $"Toolkit metadata is owned locally and would not be overwritten: {targetPath}.",
            AircraftUpdateDryRunEntryAction.PreserveLocalLivery => $"Local livery is not part of the package set and will be preserved: {targetPath}.",
            AircraftUpdateDryRunEntryAction.BlockedUnsafePath => "Unsafe archive path.",
            AircraftUpdateDryRunEntryAction.BlockedInvalidPackage => "Invalid aircraft update package.",
            _ => "Not classified."
        };

    private static void AddPreservedLocalLiveryEntries(
        string targetRoot,
        ISet<string> packageLiveryRoots,
        ICollection<AircraftUpdateDryRunEntry> entries,
        ICollection<string> findings)
    {
        if (packageLiveryRoots.Count == 0)
        {
            return;
        }

        var localLiveriesPath = Path.Combine(targetRoot, "liveries");
        if (!Directory.Exists(localLiveriesPath))
        {
            return;
        }

        var preserved = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(localLiveriesPath).OrderBy(entry => Path.GetFileName(entry), StringComparerForCurrentPlatform()))
        {
            var relativePath = Path.GetRelativePath(targetRoot, entry);
            var normalizedPath = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            var liveryRoot = GetLiveryRoot(normalizedPath);
            if (liveryRoot is null || packageLiveryRoots.Contains(liveryRoot))
            {
                continue;
            }

            entries.Add(new AircraftUpdateDryRunEntry(
                PackageFileName: "(local)",
                RelativePath: liveryRoot,
                AircraftUpdateDryRunEntryAction.PreserveLocalLivery,
                SizeBytes: 0,
                Detail: BuildDetail(AircraftUpdateDryRunEntryAction.PreserveLocalLivery, liveryRoot, entry)));
            preserved++;
        }

        if (preserved > 0)
        {
            findings.Add($"Preserved {preserved} local livery entr{(preserved == 1 ? "y" : "ies")} not present in the package set.");
        }
    }

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

    private static string? GetLiveryRoot(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 || !string.Equals(segments[0], "liveries", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return $"{segments[0]}{Path.DirectorySeparatorChar}{segments[1]}";
    }

    private static bool IsLiveryPath(string relativePath) => GetLiveryRoot(relativePath) is not null;

    private static StringComparer StringComparerForCurrentPlatform() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
