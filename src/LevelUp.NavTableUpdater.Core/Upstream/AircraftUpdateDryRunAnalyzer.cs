using System.Security.Cryptography;
using LevelUp.NavTableUpdater.Core.Aircraft;

namespace LevelUp.NavTableUpdater.Core.Upstream;

public sealed class AircraftUpdateDryRunAnalyzer
{
    public AircraftUpdateDryRunResult Analyze(
        string aircraftFolder,
        IEnumerable<AircraftUpdatePackageCacheEntry> cachedPackages,
        CancellationToken cancellationToken = default,
        IReadOnlySet<string>? managedComponentPaths = null,
        bool allowMissingAircraftFolder = false,
        IProgress<AircraftUpdateDryRunProgress>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aircraftFolder);
        ArgumentNullException.ThrowIfNull(cachedPackages);
        cancellationToken.ThrowIfCancellationRequested();

        var targetRoot = Path.GetFullPath(aircraftFolder);
        var managedPaths = (managedComponentPaths ?? new HashSet<string>())
            .Select(path => AircraftUpdatePath.NormalizeRelativePath(path)
                ?? throw new InvalidDataException($"Unsafe managed aircraft component path: {path}"))
            .ToHashSet(AircraftUpdateLocalContentPolicy.PathComparer);
        var entries = new List<AircraftUpdateDryRunEntry>();
        var packageLiveryRoots = new HashSet<string>(StringComparerForCurrentPlatform());
        var findings = new List<string>
        {
            "Dry-run only. No aircraft files are extracted, backed up, or changed."
        };
        if (managedPaths.Count > 0)
        {
            findings.Add($"Will retain {managedPaths.Count} verified toolkit-managed aircraft component file(s).");
        }

        if (!Directory.Exists(targetRoot) && !allowMissingAircraftFolder)
        {
            findings.Add($"Aircraft folder is missing: {targetRoot}");
            return new AircraftUpdateDryRunResult(false, "Aircraft folder is missing.", entries, findings);
        }

        if (!Directory.Exists(targetRoot))
        {
            findings.Add($"Fresh-install target is currently empty: {targetRoot}");
        }

        var packageEntries = cachedPackages.ToArray();
        for (var packageIndex = 0; packageIndex < packageEntries.Length; packageIndex++)
        {
            var cachedPackage = packageEntries[packageIndex];
            cancellationToken.ThrowIfCancellationRequested();
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

            AnalyzePackage(
                targetRoot,
                cachedPackage,
                entries,
                findings,
                packageLiveryRoots,
                managedPaths,
                packageIndex,
                packageEntries.Length,
                progress,
                cancellationToken);
        }

        if (packageEntries.Any(entry => entry.Package.Kind == AircraftUpdatePackageKind.FullBaseline)
            && Directory.Exists(targetRoot))
        {
            AddCleanBaselineEntries(targetRoot, entries, packageLiveryRoots, managedPaths, findings, cancellationToken);
        }
        else
        {
            AddPreservedLocalLiveryEntries(targetRoot, packageLiveryRoots, entries, findings, cancellationToken);
        }

        var blockedCount = entries.Count(entry => entry.Action is AircraftUpdateDryRunEntryAction.BlockedUnsafePath
            or AircraftUpdateDryRunEntryAction.BlockedInvalidPackage);
        var summary = blockedCount > 0
            ? $"Aircraft update dry-run found {blockedCount} blocking package issue(s); install is disabled."
            : $"Aircraft update dry-run: {entries.Count(entry => entry.Action == AircraftUpdateDryRunEntryAction.Add)} add, "
                + $"{entries.Count(entry => entry.Action == AircraftUpdateDryRunEntryAction.Replace)} replace, "
                + $"{entries.Count(entry => entry.Action == AircraftUpdateDryRunEntryAction.Delete)} delete, "
                + $"{entries.Count(entry => entry.Action is AircraftUpdateDryRunEntryAction.PreserveProtectedLocalFile or AircraftUpdateDryRunEntryAction.PreserveManagedComponent or AircraftUpdateDryRunEntryAction.PreserveToolkitMetadata)} protected"
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
        ISet<string> packageLiveryRoots,
        IReadOnlySet<string> managedComponentPaths,
        int packageIndex,
        int packageCount,
        IProgress<AircraftUpdateDryRunProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var archive = AircraftPackageArchive.Open(cachedPackage.CachePath);
            var fileEntries = archive.Entries.Where(entry => !entry.IsDirectory).ToArray();
            findings.Add($"Opened {cachedPackage.Package.FileName}: {fileEntries.Length} archive file entr{(fileEntries.Length == 1 ? "y" : "ies")}.");
            ReportProgress(progress, cachedPackage.Package.FileName, packageIndex, packageCount, 0, fileEntries.Length, null);

            if (cachedPackage.Package.Manifest is null)
            {
                AnalyzeLegacyArchive(
                    targetRoot,
                    cachedPackage.Package,
                    fileEntries,
                    entries,
                    packageLiveryRoots,
                    managedComponentPaths,
                    packageIndex,
                    packageCount,
                    progress,
                    cancellationToken);
                return;
            }

            AnalyzeManifestArchive(
                targetRoot,
                cachedPackage.Package,
                archive,
                fileEntries.Length,
                entries,
                findings,
                packageLiveryRoots,
                managedComponentPaths,
                packageIndex,
                packageCount,
                progress,
                cancellationToken);
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
        AircraftUpdatePackage package,
        IReadOnlyList<AircraftPackageArchiveEntry> archiveEntries,
        ICollection<AircraftUpdateDryRunEntry> entries,
        ISet<string> packageLiveryRoots,
        IReadOnlySet<string> managedComponentPaths,
        int packageIndex,
        int packageCount,
        IProgress<AircraftUpdateDryRunProgress>? progress,
        CancellationToken cancellationToken)
    {
        var contentRoot = AircraftUpdatePath.DetectContentRoot(package, archiveEntries);
        for (var entryIndex = 0; entryIndex < archiveEntries.Count; entryIndex++)
        {
            var archiveEntry = archiveEntries[entryIndex];
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedPath = AircraftUpdatePath.MapArchivePath(archiveEntry.Path, contentRoot);
            AnalyzeWriteEntry(targetRoot, package.FileName, archiveEntry.Path, normalizedPath, archiveEntry.Size, entries, packageLiveryRoots, managedComponentPaths);
            ReportProgress(
                progress,
                package.FileName,
                packageIndex,
                packageCount,
                entryIndex + 1,
                archiveEntries.Count,
                archiveEntry.Path);
        }
    }

    private static void AnalyzeManifestArchive(
        string targetRoot,
        AircraftUpdatePackage package,
        AircraftPackageArchive archive,
        int fileEntryCount,
        ICollection<AircraftUpdateDryRunEntry> entries,
        ICollection<string> findings,
        ISet<string> packageLiveryRoots,
        IReadOnlySet<string> managedComponentPaths,
        int packageIndex,
        int packageCount,
        IProgress<AircraftUpdateDryRunProgress>? progress,
        CancellationToken cancellationToken)
    {
        var manifest = package.Manifest!;
        var manifestFiles = manifest.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var verified = 0;

        var entryIndex = 0;
        archive.ProcessFileEntriesSequentially((archiveEntry, stream) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            entryIndex++;
            ReportProgress(
                progress,
                package.FileName,
                packageIndex,
                packageCount,
                entryIndex,
                fileEntryCount,
                archiveEntry.Path);
            var relativePath = AircraftUpdatePath.MapArchivePath(archiveEntry.Path, manifest.ContentRoot);
            if (relativePath is null)
            {
                entries.Add(new AircraftUpdateDryRunEntry(
                    package.FileName,
                    archiveEntry.Path,
                    AircraftUpdateDryRunEntryAction.BlockedUnsafePath,
                    archiveEntry.Size,
                    $"Archive entry is outside declared contentRoot '{manifest.ContentRoot}' or has an unsafe path."));
                return;
            }

            if (!seenPaths.Add(relativePath))
            {
                entries.Add(new AircraftUpdateDryRunEntry(
                    package.FileName,
                    relativePath,
                    AircraftUpdateDryRunEntryAction.BlockedInvalidPackage,
                    archiveEntry.Size,
                    "Archive contains the same manifest path more than once."));
                return;
            }

            if (!manifestFiles.TryGetValue(relativePath, out var manifestFile))
            {
                entries.Add(new AircraftUpdateDryRunEntry(
                    package.FileName,
                    relativePath,
                    AircraftUpdateDryRunEntryAction.BlockedInvalidPackage,
                    archiveEntry.Size,
                    "Archive entry is not declared in the package manifest."));
                return;
            }

            var integrityError = VerifyArchiveEntry(stream, archiveEntry.Size, manifestFile, cancellationToken);
            if (integrityError is not null)
            {
                entries.Add(new AircraftUpdateDryRunEntry(
                    package.FileName,
                    relativePath,
                    AircraftUpdateDryRunEntryAction.BlockedInvalidPackage,
                    archiveEntry.Size,
                    integrityError));
                return;
            }

            verified++;
            AnalyzeWriteEntry(targetRoot, package.FileName, archiveEntry.Path, relativePath, archiveEntry.Size, entries, packageLiveryRoots, managedComponentPaths);
        }, cancellationToken);

        foreach (var missing in manifest.Files.Where(file => !seenPaths.Contains(file.Path)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries.Add(new AircraftUpdateDryRunEntry(
                package.FileName,
                missing.Path,
                AircraftUpdateDryRunEntryAction.BlockedInvalidPackage,
                missing.Size,
                "Manifest file is missing from the archive."));
        }

        foreach (var deletedPath in manifest.DeletedPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AnalyzeDeleteEntry(targetRoot, package.FileName, deletedPath, entries, managedComponentPaths);
        }

        findings.Add($"Verified {verified} archive payload file(s) against manifest size and SHA-256.");
        findings.Add($"Manifest declares {manifest.DeletedPaths.Count} deletion(s).");
    }

    private static void ReportProgress(
        IProgress<AircraftUpdateDryRunProgress>? progress,
        string packageFileName,
        int packageIndex,
        int packageCount,
        int processedFileCount,
        int totalFileCount,
        string? currentPath) =>
        progress?.Report(new AircraftUpdateDryRunProgress(
            packageFileName,
            packageIndex + 1,
            packageCount,
            processedFileCount,
            totalFileCount,
            currentPath));

    private static void AnalyzeWriteEntry(
        string targetRoot,
        string packageFileName,
        string sourcePath,
        string? normalizedPath,
        long size,
        ICollection<AircraftUpdateDryRunEntry> entries,
        ISet<string> packageLiveryRoots,
        IReadOnlySet<string> managedComponentPaths)
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

        var liveryRoot = AircraftUpdateLocalContentPolicy.GetLiveryRoot(normalizedPath);
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

        var action = ClassifyWriteAction(normalizedPath, targetPath, managedComponentPaths);
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
        ICollection<AircraftUpdateDryRunEntry> entries,
        IReadOnlySet<string> managedComponentPaths)
    {
        var normalizedPath = AircraftUpdatePath.NormalizeRelativePath(relativePath);
        if (normalizedPath is not null && managedComponentPaths.Contains(normalizedPath))
        {
            entries.Add(new AircraftUpdateDryRunEntry(
                packageFileName,
                normalizedPath,
                AircraftUpdateDryRunEntryAction.PreserveManagedComponent,
                0,
                "A verified toolkit-managed aircraft component will be retained."));
            return;
        }

        if (normalizedPath is not null
            && managedComponentPaths.Any(path => IsAncestorPath(normalizedPath, path)))
        {
            entries.Add(new AircraftUpdateDryRunEntry(
                packageFileName,
                normalizedPath,
                AircraftUpdateDryRunEntryAction.BlockedInvalidPackage,
                0,
                "Manifest attempts to delete a directory containing a managed aircraft component."));
            return;
        }

        if (AircraftUpdateLocalContentPolicy.IsProtectedLocalFile(relativePath)
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

    private static string? VerifyArchiveEntry(
        Stream stream,
        long entrySize,
        AircraftUpdateManifestFile manifestFile,
        CancellationToken cancellationToken)
    {
        if (entrySize != manifestFile.Size)
        {
            return $"Payload size differs from manifest: expected {manifestFile.Size}, got {entrySize}.";
        }

        using var hashAlgorithm = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        int bytesRead;
        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hashAlgorithm.AppendData(buffer, 0, bytesRead);
        }

        var hash = Convert.ToHexString(hashAlgorithm.GetHashAndReset()).ToLowerInvariant();
        return string.Equals(hash, manifestFile.Sha256, StringComparison.OrdinalIgnoreCase)
            ? null
            : "Payload SHA-256 differs from the package manifest.";
    }

    private static AircraftUpdateDryRunEntryAction ClassifyWriteAction(
        string relativePath,
        string targetPath,
        IReadOnlySet<string> managedComponentPaths)
    {
        if (string.Equals(Path.GetFileName(relativePath), AircraftMaintenanceMetadata.FileName, StringComparison.OrdinalIgnoreCase))
        {
            return AircraftUpdateDryRunEntryAction.PreserveToolkitMetadata;
        }

        if (AircraftUpdateLocalContentPolicy.IsProtectedLocalFile(relativePath))
        {
            return AircraftUpdateDryRunEntryAction.PreserveProtectedLocalFile;
        }

        if (managedComponentPaths.Contains(relativePath))
        {
            return AircraftUpdateDryRunEntryAction.PreserveManagedComponent;
        }

        return File.Exists(targetPath)
            ? AircraftUpdateDryRunEntryAction.Replace
            : AircraftUpdateDryRunEntryAction.Add;
    }

    private static string BuildDetail(AircraftUpdateDryRunEntryAction action, string relativePath, string targetPath) =>
        action switch
        {
            AircraftUpdateDryRunEntryAction.Add when AircraftUpdateLocalContentPolicy.IsLiveryPath(relativePath) => $"Would add package-owned livery file {targetPath}.",
            AircraftUpdateDryRunEntryAction.Add => $"Would add {targetPath}.",
            AircraftUpdateDryRunEntryAction.Replace when AircraftUpdateLocalContentPolicy.IsLiveryPath(relativePath) => $"Would replace package-owned livery file {targetPath} after backup; local changes to this package-owned file would be overwritten.",
            AircraftUpdateDryRunEntryAction.Replace => $"Would replace {targetPath} after backup.",
            AircraftUpdateDryRunEntryAction.PreserveProtectedLocalFile => $"Protected local preference/config file would not be overwritten: {targetPath}.",
            AircraftUpdateDryRunEntryAction.PreserveManagedComponent => $"Verified toolkit-managed aircraft component file will be retained: {targetPath}.",
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
        ICollection<string> findings,
        CancellationToken cancellationToken)
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
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(targetRoot, entry);
            var normalizedPath = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            var liveryRoot = AircraftUpdateLocalContentPolicy.GetLiveryRoot(normalizedPath);
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

    private static void AddCleanBaselineEntries(
        string targetRoot,
        ICollection<AircraftUpdateDryRunEntry> entries,
        ISet<string> packageLiveryRoots,
        IReadOnlySet<string> managedComponentPaths,
        ICollection<string> findings,
        CancellationToken cancellationToken)
    {
        var finalPackagePaths = new HashSet<string>(StringComparerForCurrentPlatform());
        foreach (var entry in entries.Where(entry => entry.PackageFileName != "(local)"))
        {
            if (entry.Action is AircraftUpdateDryRunEntryAction.Add
                or AircraftUpdateDryRunEntryAction.Replace
                or AircraftUpdateDryRunEntryAction.PreserveProtectedLocalFile
                or AircraftUpdateDryRunEntryAction.PreserveManagedComponent
                or AircraftUpdateDryRunEntryAction.PreserveToolkitMetadata)
            {
                finalPackagePaths.Add(entry.RelativePath);
            }
            else if (entry.Action is AircraftUpdateDryRunEntryAction.Delete
                or AircraftUpdateDryRunEntryAction.AlreadyAbsent)
            {
                finalPackagePaths.Remove(entry.RelativePath);
            }
        }

        var alreadyClassified = new HashSet<string>(
            entries.Select(entry => entry.RelativePath),
            StringComparerForCurrentPlatform());
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(targetRoot);
        var obsolete = 0;
        var protectedCount = 0;
        var localLiveries = 0;

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pendingDirectories.Pop();
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = Path.GetRelativePath(targetRoot, path);
                var info = Directory.Exists(path) ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path);
                if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    var liveryRoot = AircraftUpdateLocalContentPolicy.GetLiveryRoot(relativePath);
                    var mustPreserve = AircraftUpdateLocalContentPolicy.CouldContainProtectedLocalContent(relativePath)
                        || liveryRoot is not null && !packageLiveryRoots.Contains(liveryRoot);
                    entries.Add(new AircraftUpdateDryRunEntry(
                        "(local)",
                        relativePath,
                        mustPreserve
                            ? AircraftUpdateDryRunEntryAction.BlockedUnsafePath
                            : AircraftUpdateDryRunEntryAction.Delete,
                        0,
                        mustPreserve
                            ? "A protected local entry is a symbolic link and cannot be migrated safely during a clean baseline replacement."
                            : "The old installation link will not be carried into the clean baseline."));
                    if (!mustPreserve)
                    {
                        obsolete++;
                    }

                    continue;
                }

                if (Directory.Exists(path))
                {
                    var liveryRoot = AircraftUpdateLocalContentPolicy.GetLiveryRoot(relativePath);
                    if (liveryRoot is not null && !packageLiveryRoots.Contains(liveryRoot))
                    {
                        if (alreadyClassified.Add(liveryRoot))
                        {
                            entries.Add(new AircraftUpdateDryRunEntry(
                                "(local)",
                                liveryRoot,
                                AircraftUpdateDryRunEntryAction.PreserveLocalLivery,
                                0,
                                $"Local livery will be migrated into the clean baseline: {path}."));
                            localLiveries++;
                        }

                        continue;
                    }

                    pendingDirectories.Push(path);
                    continue;
                }

                if (finalPackagePaths.Contains(relativePath) || !alreadyClassified.Add(relativePath))
                {
                    continue;
                }

                if (AircraftUpdateLocalContentPolicy.IsProtectedLocalFile(relativePath))
                {
                    entries.Add(new AircraftUpdateDryRunEntry(
                        "(local)",
                        relativePath,
                        AircraftUpdateDryRunEntryAction.PreserveProtectedLocalFile,
                        new FileInfo(path).Length,
                        $"Protected local preference/config file will be migrated into the clean baseline: {path}."));
                    protectedCount++;
                    continue;
                }

                if (managedComponentPaths.Contains(relativePath))
                {
                    entries.Add(new AircraftUpdateDryRunEntry(
                        "(local)",
                        relativePath,
                        AircraftUpdateDryRunEntryAction.PreserveManagedComponent,
                        new FileInfo(path).Length,
                        $"Verified toolkit-managed aircraft component file will be migrated into the clean baseline: {path}."));
                    protectedCount++;
                    continue;
                }

                if (string.Equals(Path.GetFileName(relativePath), AircraftMaintenanceMetadata.FileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                entries.Add(new AircraftUpdateDryRunEntry(
                    "(clean baseline)",
                    relativePath,
                    AircraftUpdateDryRunEntryAction.Delete,
                    new FileInfo(path).Length,
                    $"Old baseline file will not be carried into the clean installation: {path}."));
                obsolete++;
            }
        }

        findings.Add($"Clean baseline replacement will omit {obsolete} obsolete old-baseline entr{(obsolete == 1 ? "y" : "ies")}.");
        findings.Add($"Clean baseline replacement will migrate {protectedCount} additional protected file(s) and {localLiveries} local livery entr{(localLiveries == 1 ? "y" : "ies")}.");
    }

    private static StringComparer StringComparerForCurrentPlatform() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static bool IsAncestorPath(string candidate, string descendant)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return descendant.StartsWith(
            candidate.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            comparison);
    }
}
