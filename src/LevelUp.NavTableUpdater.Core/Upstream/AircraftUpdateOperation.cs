using System.Security.Cryptography;
using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Platform;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Upstream;

public sealed class AircraftUpdateOperation
{
    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly ToolStateStore _stateStore;
    private readonly AircraftUpdateDryRunAnalyzer _dryRunAnalyzer;
    private readonly Func<bool> _isXPlaneRunning;

    public AircraftUpdateOperation(
        ToolStateStore stateStore,
        AircraftUpdateDryRunAnalyzer? dryRunAnalyzer = null,
        Func<bool>? isXPlaneRunning = null)
    {
        _stateStore = stateStore;
        _dryRunAnalyzer = dryRunAnalyzer ?? new AircraftUpdateDryRunAnalyzer();
        _isXPlaneRunning = isXPlaneRunning ?? XPlaneProcessDetector.IsXPlaneRunning;
    }

    public MaintenanceOperationResult Apply(
        AircraftVariantViewAnalysis variant,
        AircraftUpstreamUpdateCheckResult updateCheck,
        IReadOnlyList<AircraftUpdatePackageCacheEntry> cachedPackages,
        CancellationToken cancellationToken = default,
        Action? writePhaseStarting = null)
    {
        ArgumentNullException.ThrowIfNull(variant);
        ArgumentNullException.ThrowIfNull(updateCheck);
        ArgumentNullException.ThrowIfNull(cachedPackages);
        cancellationToken.ThrowIfCancellationRequested();

        var aircraftFolder = Path.GetFullPath(Path.GetDirectoryName(variant.AcfPath) ?? "");
        var log = new List<string>
        {
            $"[START] Apply aircraft update for {variant.DisplayName}",
            $"[MODE] {FormatUpdateMode(updateCheck.UpdateMode)}",
            $"[PLAN] {updateCheck.ActionDisplay}: {updateCheck.Summary}"
        };

        if (_isXPlaneRunning())
        {
            log.Add("[BLOCKED] X-Plane is running.");
            return MaintenanceOperationResult.Blocked("X-Plane is running. Close X-Plane before changing aircraft files.", log);
        }

        if (!Directory.Exists(aircraftFolder))
        {
            log.Add("[BLOCKED] Aircraft folder is missing.");
            return MaintenanceOperationResult.Blocked("Aircraft folder is missing.", log);
        }

        if (updateCheck.IsCustomDistribution)
        {
            log.Add("[BLOCKED] Custom distribution detected. Official upstream packages are review-only for this target.");
            return MaintenanceOperationResult.Blocked("Custom distributions are review-only for official upstream package updates.", log);
        }

        if (updateCheck.RequiredPackages.Count == 0)
        {
            log.Add("[NO-CHANGE] Current upstream plan does not require package changes.");
            return MaintenanceOperationResult.NoChange("No upstream aircraft package changes are required.", log);
        }

        var cacheValidation = ValidateCachedPackages(updateCheck.RequiredPackages, cachedPackages);
        cancellationToken.ThrowIfCancellationRequested();
        if (cacheValidation.BlockingMessages.Count > 0)
        {
            foreach (var message in cacheValidation.BlockingMessages)
            {
                log.Add($"[BLOCKED] {message}");
            }

            return MaintenanceOperationResult.Blocked("Required aircraft update packages are missing or changed in the cache.", log);
        }

        var orderedCacheEntries = updateCheck.RequiredPackages
            .Select(package => cacheValidation.CacheEntriesByFileName[package.FileName])
            .ToArray();
        foreach (var entry in orderedCacheEntries)
        {
            log.Add($"[CACHE] {entry.Package.FileName}: {entry.SizeBytes} bytes, sha256 {entry.Sha256}.");
        }

        log.Add(orderedCacheEntries.All(entry => !string.IsNullOrWhiteSpace(entry.Package.ExpectedSha256))
            ? "[INTEGRITY] Package archives are verified against authoritative manifest size and SHA-256 values."
            : "[INTEGRITY] Legacy packages are verified against the local cache snapshot; the source feed provides no authoritative package hashes.");
        var dryRun = _dryRunAnalyzer.Analyze(aircraftFolder, orderedCacheEntries, cancellationToken);
        foreach (var finding in dryRun.Findings)
        {
            log.Add($"[DRY-RUN] {finding}");
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!dryRun.Succeeded)
        {
            log.Add($"[BLOCKED] {dryRun.Summary}");
            return MaintenanceOperationResult.Blocked(dryRun.Summary, log);
        }

        var changeEntries = dryRun.Entries
            .Where(entry => entry.Action is AircraftUpdateDryRunEntryAction.Add
                or AircraftUpdateDryRunEntryAction.Replace
                or AircraftUpdateDryRunEntryAction.Delete)
            .ToArray();
        if (changeEntries.Length == 0)
        {
            RecordAircraftUpdateState(variant, updateCheck, backups: [], operation: BuildOperationName(updateCheck.UpdateMode, "NoChange"));
            log.Add("[NO-CHANGE] Dry-run found no add, replace, or delete entries.");
            return MaintenanceOperationResult.NoChange("No upstream aircraft files need to be changed.", log);
        }

        cancellationToken.ThrowIfCancellationRequested();
        writePhaseStarting?.Invoke();
        var createdUtc = DateTimeOffset.UtcNow;
        var backupRecords = new List<BackupRecord>();
        var preImagesByTarget = new Dictionary<string, BackupRecord>(StringComparerForCurrentPlatform());
        var writePlan = changeEntries
            .GroupBy(entry => (entry.PackageFileName, entry.RelativePath), entry => entry.Action)
            .ToDictionary(group => group.Key, group => group.Last());

        try
        {
            foreach (var cacheEntry in orderedCacheEntries)
            {
                ApplyPackage(
                    aircraftFolder,
                    variant,
                    cacheEntry,
                    updateCheck,
                    writePlan,
                    createdUtc,
                    preImagesByTarget,
                    backupRecords,
                    log);
            }

            WriteToolkitMetadata(
                aircraftFolder,
                variant,
                updateCheck,
                createdUtc,
                preImagesByTarget,
                backupRecords,
                log);

            RecordAircraftUpdateState(variant, updateCheck, backupRecords, BuildOperationName(updateCheck.UpdateMode, "Apply"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        {
            RollBack(preImagesByTarget.Values.ToArray(), log);
            log.Add($"[FAILED] {ex.Message}");
            return MaintenanceOperationResult.Blocked($"Aircraft update failed and rollback completed: {ex.Message}", log);
        }

        log.Add("[OK] Aircraft update transaction completed.");
        return MaintenanceOperationResult.Applied(
            $"{FormatUpdateMode(updateCheck.UpdateMode)} completed to {updateCheck.AvailableVersionDisplay}.",
            backupRecords.Where(record => !string.IsNullOrWhiteSpace(record.BackupPath)).Select(record => record.BackupPath).ToArray(),
            log);
    }

    public MaintenanceOperationResult RestoreLatest(AircraftVariantViewAnalysis variant)
    {
        ArgumentNullException.ThrowIfNull(variant);

        var aircraftFolder = Path.GetFullPath(Path.GetDirectoryName(variant.AcfPath) ?? "");
        var log = new List<string>
        {
            $"[START] Restore latest aircraft update for {variant.DisplayName}"
        };

        if (_isXPlaneRunning())
        {
            log.Add("[BLOCKED] X-Plane is running.");
            return MaintenanceOperationResult.Blocked("X-Plane is running. Close X-Plane before restoring aircraft files.", log);
        }

        if (!Directory.Exists(aircraftFolder))
        {
            log.Add("[BLOCKED] Aircraft folder is missing.");
            return MaintenanceOperationResult.Blocked("Aircraft folder is missing.", log);
        }

        var target = _stateStore.TryGetTarget(variant);
        if (target is null)
        {
            log.Add("[BLOCKED] No toolkit state is recorded for this aircraft variant.");
            return MaintenanceOperationResult.Blocked("No toolkit state is recorded for this aircraft variant.", log);
        }

        var restoreRecords = SelectLatestAircraftUpdateGeneration(variant, target.Backups);
        if (restoreRecords.Length == 0)
        {
            log.Add("[BLOCKED] No aircraft update backup generation is recorded for this aircraft variant.");
            return MaintenanceOperationResult.Blocked("No aircraft update backup generation is recorded for this aircraft variant.", log);
        }

        var createdUtc = DateTimeOffset.UtcNow;
        var preRestoreBackups = new List<BackupRecord>();

        try
        {
            foreach (var record in restoreRecords.Reverse())
            {
                var preRestore = CapturePreRestoreImage(variant, record, createdUtc, log);
                if (preRestore is not null)
                {
                    preRestoreBackups.Add(preRestore);
                }

                RestorePreImage(record, log);
            }

            _stateStore.UpdateTarget(variant, state =>
            {
                state.InstalledAircraftUpdateFamily = null;
                state.InstalledAircraftUpdateVersion = null;
                state.LastAircraftUpdateMode = null;
                state.LastAircraftUpdateUtc = DateTimeOffset.UtcNow;
                state.LastAircraftUpdatePackages.Clear();
                state.LastOperation = "AircraftUpdateRestore";
                state.Backups.AddRange(preRestoreBackups);
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            log.Add($"[FAILED] {ex.Message}");
            return MaintenanceOperationResult.Blocked($"Aircraft update restore failed before completion: {ex.Message}", log);
        }

        log.Add("[OK] Aircraft update restore completed.");
        return MaintenanceOperationResult.Restored(
            $"Restored {restoreRecords.Length} aircraft update file state(s).",
            preRestoreBackups.Select(record => record.BackupPath).ToArray(),
            log);
    }

    private void ApplyPackage(
        string aircraftFolder,
        AircraftVariantViewAnalysis variant,
        AircraftUpdatePackageCacheEntry cacheEntry,
        AircraftUpstreamUpdateCheckResult updateCheck,
        IReadOnlyDictionary<(string PackageFileName, string RelativePath), AircraftUpdateDryRunEntryAction> writePlan,
        DateTimeOffset createdUtc,
        IDictionary<string, BackupRecord> preImagesByTarget,
        ICollection<BackupRecord> backupRecords,
        ICollection<string> log)
    {
        foreach (var deletion in writePlan
            .Where(entry => string.Equals(entry.Key.PackageFileName, cacheEntry.Package.FileName, StringComparison.OrdinalIgnoreCase)
                && entry.Value == AircraftUpdateDryRunEntryAction.Delete)
            .Select(entry => entry.Key.RelativePath))
        {
            var targetPath = AircraftUpdatePath.ResolveTargetPath(aircraftFolder, deletion);
            if (!preImagesByTarget.ContainsKey(targetPath))
            {
                var backupRecord = CapturePreImage(
                    variant,
                    updateCheck,
                    cacheEntry.Package,
                    targetPath,
                    deletion,
                    createdUtc,
                    "AircraftUpdateDeletedFile");
                preImagesByTarget[targetPath] = backupRecord;
                backupRecords.Add(backupRecord);
                log.Add($"[BACKUP] {backupRecord.BackupPath}");
            }

            File.Delete(targetPath);
            log.Add($"[DELETE] Removed {deletion} as declared by {cacheEntry.Package.FileName}.");
        }

        using var archive = AircraftPackageArchive.Open(cacheEntry.CachePath);
        log.Add($"[PACKAGE] Applying {cacheEntry.Package.FileName} ({archive.Entries.Count} archive entries).");

        foreach (var archiveEntry in archive.Entries)
        {
            if (archiveEntry.IsDirectory)
            {
                continue;
            }

            var normalizedPath = AircraftUpdatePath.MapArchivePath(
                archiveEntry.Path,
                cacheEntry.Package.Manifest?.ContentRoot)
                ?? throw new InvalidOperationException($"Unsafe archive path in {cacheEntry.Package.FileName}: {archiveEntry.Path}");
            if (!writePlan.TryGetValue((cacheEntry.Package.FileName, normalizedPath), out var action))
            {
                continue;
            }

            var targetPath = AircraftUpdatePath.ResolveTargetPath(aircraftFolder, normalizedPath);
            if (!preImagesByTarget.ContainsKey(targetPath))
            {
                var backupRecord = CapturePreImage(
                    variant,
                    updateCheck,
                    cacheEntry.Package,
                    targetPath,
                    normalizedPath,
                    createdUtc,
                    action == AircraftUpdateDryRunEntryAction.Replace ? "AircraftUpdatePreImage" : "AircraftUpdateAddedFile");
                preImagesByTarget[targetPath] = backupRecord;
                backupRecords.Add(backupRecord);
                log.Add(backupRecord.SourceExisted
                    ? $"[BACKUP] {backupRecord.BackupPath}"
                    : $"[BACKUP] New file tracked for restore: {targetPath}");
            }

            var manifestFile = cacheEntry.Package.Manifest?.Files
                .FirstOrDefault(file => string.Equals(file.Path, normalizedPath, StringComparison.OrdinalIgnoreCase));
            ExtractArchiveEntry(archiveEntry, targetPath, manifestFile);
            log.Add(action == AircraftUpdateDryRunEntryAction.Replace
                ? $"[WRITE] Replaced {normalizedPath} from {cacheEntry.Package.FileName}."
                : $"[WRITE] Added {normalizedPath} from {cacheEntry.Package.FileName}.");
        }
    }

    private void WriteToolkitMetadata(
        string aircraftFolder,
        AircraftVariantViewAnalysis variant,
        AircraftUpstreamUpdateCheckResult updateCheck,
        DateTimeOffset createdUtc,
        IDictionary<string, BackupRecord> preImagesByTarget,
        ICollection<BackupRecord> backupRecords,
        ICollection<string> log)
    {
        var metadataPath = Path.Combine(aircraftFolder, AircraftMaintenanceMetadata.FileName);
        if (!preImagesByTarget.ContainsKey(metadataPath))
        {
            var metadataBackup = CapturePreImage(
                variant,
                updateCheck,
                package: null,
                metadataPath,
                AircraftMaintenanceMetadata.FileName,
                createdUtc,
                "AircraftUpdateMetadata");
            preImagesByTarget[metadataPath] = metadataBackup;
            backupRecords.Add(metadataBackup);
            log.Add(metadataBackup.SourceExisted
                ? $"[BACKUP] {metadataBackup.BackupPath}"
                : $"[BACKUP] New toolkit metadata tracked for restore: {metadataPath}");
        }

        var metadata = new AircraftMaintenanceMetadata(
            SchemaVersion: 1,
            AircraftFamily: variant.Family,
            Variant: variant.AircraftId,
            Distribution: null,
            DistributionVersion: updateCheck.AvailableVersionDisplay,
            UpstreamFamily: updateCheck.Family,
            UpstreamBaseVersion: updateCheck.AvailableVersionDisplay,
            UpstreamSourceRef: null,
            UpstreamReleaseTag: null,
            Runtime: null);
        var tempPath = metadataPath + $".tmp-{Guid.NewGuid():N}";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(metadata, MetadataJsonOptions));
            File.Move(tempPath, metadataPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        log.Add($"[METADATA] Wrote {AircraftMaintenanceMetadata.FileName} for installed upstream version {updateCheck.AvailableVersionDisplay}.");
    }

    private BackupRecord CapturePreImage(
        AircraftVariantViewAnalysis variant,
        AircraftUpstreamUpdateCheckResult updateCheck,
        AircraftUpdatePackage? package,
        string targetPath,
        string relativePath,
        DateTimeOffset createdUtc,
        string operation)
    {
        var existed = File.Exists(targetPath);
        var backupPath = "";
        long? sourceSize = null;
        string? sourceSha = null;

        if (existed)
        {
            backupPath = _stateStore.CreateBackupPath(variant, targetPath, createdUtc, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            File.Copy(targetPath, backupPath, overwrite: false);
            var info = new FileInfo(targetPath);
            sourceSize = info.Length;
            sourceSha = ComputeSha256(targetPath);
        }

        return new BackupRecord
        {
            Operation = operation,
            SourcePath = targetPath,
            BackupPath = backupPath,
            CreatedUtc = createdUtc,
            CgYFeet = variant.CurrentCgYFeet,
            CgZFeet = variant.CurrentCgZFeet,
            PackageId = updateCheck.Family,
            PackageVersion = updateCheck.AvailableVersionDisplay,
            PackageFileName = package?.FileName,
            SourceExisted = existed,
            SourceSizeBytes = sourceSize,
            SourceSha256 = sourceSha
        };
    }

    private BackupRecord? CapturePreRestoreImage(
        AircraftVariantViewAnalysis variant,
        BackupRecord restoreRecord,
        DateTimeOffset createdUtc,
        ICollection<string> log)
    {
        if (!File.Exists(restoreRecord.SourcePath))
        {
            log.Add($"[RESTORE] Current file is already missing: {restoreRecord.SourcePath}");
            return null;
        }

        var aircraftFolder = Path.GetFullPath(Path.GetDirectoryName(variant.AcfPath) ?? "");
        var relativePath = Path.GetRelativePath(aircraftFolder, restoreRecord.SourcePath);
        var backupPath = _stateStore.CreateBackupPath(variant, restoreRecord.SourcePath, createdUtc, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        File.Copy(restoreRecord.SourcePath, backupPath, overwrite: false);
        log.Add($"[BACKUP] Pre-restore image saved at {backupPath}");

        return new BackupRecord
        {
            Operation = "AircraftUpdateRestorePreImage",
            SourcePath = restoreRecord.SourcePath,
            BackupPath = backupPath,
            CreatedUtc = createdUtc,
            CgYFeet = variant.CurrentCgYFeet,
            CgZFeet = variant.CurrentCgZFeet,
            PackageId = restoreRecord.PackageId,
            PackageVersion = restoreRecord.PackageVersion,
            PackageFileName = restoreRecord.PackageFileName,
            SourceExisted = true,
            SourceSizeBytes = new FileInfo(restoreRecord.SourcePath).Length,
            SourceSha256 = ComputeSha256(restoreRecord.SourcePath)
        };
    }

    private void RecordAircraftUpdateState(
        AircraftVariantViewAnalysis variant,
        AircraftUpstreamUpdateCheckResult updateCheck,
        IReadOnlyList<BackupRecord> backups,
        string operation)
    {
        _stateStore.UpdateTarget(variant, target =>
        {
            target.InstalledAircraftUpdateFamily = updateCheck.Family;
            target.InstalledAircraftUpdateVersion = updateCheck.AvailableVersionDisplay;
            target.LastAircraftUpdateMode = updateCheck.UpdateMode.ToString();
            target.LastAircraftUpdateUtc = DateTimeOffset.UtcNow;
            target.LastAircraftUpdatePackages = updateCheck.RequiredPackages.Select(package => package.FileName).ToList();
            target.LastOperation = operation;
            target.Backups.AddRange(backups);
        });
    }

    private static CachedPackageValidation ValidateCachedPackages(
        IReadOnlyList<AircraftUpdatePackage> requiredPackages,
        IReadOnlyList<AircraftUpdatePackageCacheEntry> cachedPackages)
    {
        var messages = new List<string>();
        var cacheEntriesByFileName = cachedPackages
            .GroupBy(entry => entry.Package.FileName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

        foreach (var package in requiredPackages)
        {
            if (!cacheEntriesByFileName.TryGetValue(package.FileName, out var entry))
            {
                messages.Add($"Package is not in the cache list: {package.FileName}");
                continue;
            }

            if (!entry.IsCached || !File.Exists(entry.CachePath))
            {
                messages.Add($"Package is not cached: {package.FileName}");
                continue;
            }

            var info = new FileInfo(entry.CachePath);
            var sha = ComputeSha256(entry.CachePath);
            if (entry.SizeBytes is not null && entry.SizeBytes.Value != info.Length)
            {
                messages.Add($"Cached package size changed after inspection: {package.FileName}");
            }

            if (!string.IsNullOrWhiteSpace(entry.Sha256)
                && !string.Equals(entry.Sha256, sha, StringComparison.OrdinalIgnoreCase))
            {
                messages.Add($"Cached package hash changed after inspection: {package.FileName}");
            }

            if (package.ExpectedSizeBytes is not null && package.ExpectedSizeBytes.Value != info.Length)
            {
                messages.Add($"Cached package size does not match the manifest: {package.FileName}");
            }

            if (!string.IsNullOrWhiteSpace(package.ExpectedSha256)
                && !string.Equals(package.ExpectedSha256, sha, StringComparison.OrdinalIgnoreCase))
            {
                messages.Add($"Cached package SHA-256 does not match the manifest: {package.FileName}");
            }
        }

        return new CachedPackageValidation(messages, cacheEntriesByFileName);
    }

    private static void ExtractArchiveEntry(
        AircraftPackageArchiveEntry archiveEntry,
        string targetPath,
        AircraftUpdateManifestFile? manifestFile)
    {
        var targetExisted = File.Exists(targetPath);
        var attributes = targetExisted ? File.GetAttributes(targetPath) : FileAttributes.Normal;
        var unixMode = TryGetUnixFileMode(targetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var tempPath = targetPath + $".tmp-{Guid.NewGuid():N}";

        try
        {
            using (var input = archiveEntry.Open())
            using (var output = File.Create(tempPath))
            {
                input.CopyTo(output);
            }

            File.Move(tempPath, targetPath, overwrite: true);
            if (manifestFile is not null)
            {
                var info = new FileInfo(targetPath);
                var sha = ComputeSha256(targetPath);
                if (info.Length != manifestFile.Size
                    || !string.Equals(sha, manifestFile.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Written payload failed manifest verification: {manifestFile.Path}");
                }
            }

            if (targetExisted)
            {
                File.SetAttributes(targetPath, attributes);
                TrySetUnixFileMode(targetPath, unixMode);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static void RestorePreImage(BackupRecord record, ICollection<string> log)
    {
        if (record.SourceExisted)
        {
            if (string.IsNullOrWhiteSpace(record.BackupPath) || !File.Exists(record.BackupPath))
            {
                throw new FileNotFoundException("Aircraft update backup file is missing.", record.BackupPath);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(record.SourcePath)!);
            var tempPath = record.SourcePath + $".tmp-{Guid.NewGuid():N}";
            try
            {
                File.Copy(record.BackupPath, tempPath, overwrite: false);
                File.Move(tempPath, record.SourcePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }

            log.Add($"[RESTORE] Restored {record.SourcePath}.");
            return;
        }

        if (File.Exists(record.SourcePath))
        {
            File.Delete(record.SourcePath);
            log.Add($"[RESTORE] Removed added file {record.SourcePath}.");
        }
    }

    private static void RollBack(IReadOnlyList<BackupRecord> records, ICollection<string> log)
    {
        foreach (var record in records.Reverse())
        {
            RestorePreImage(record, log);
            log.Add("[ROLLBACK] Reverted an aircraft update file change.");
        }
    }

    private static BackupRecord[] SelectLatestAircraftUpdateGeneration(
        AircraftVariantViewAnalysis variant,
        IReadOnlyList<BackupRecord> records)
    {
        var candidates = records
            .Where(record => record.Operation is "AircraftUpdatePreImage" or "AircraftUpdateAddedFile" or "AircraftUpdateDeletedFile" or "AircraftUpdateMetadata"
                && IsInsideAircraftFolder(variant, record.SourcePath))
            .ToArray();
        if (candidates.Length == 0)
        {
            return [];
        }

        var latest = candidates.Max(record => record.CreatedUtc);
        return candidates
            .Where(record => record.CreatedUtc == latest)
            .GroupBy(record => Path.GetFullPath(record.SourcePath), StringComparerForCurrentPlatform())
            .Select(group => group.Last())
            .OrderBy(record => record.SourcePath, StringComparerForCurrentPlatform())
            .ToArray();
    }

    private static bool IsInsideAircraftFolder(AircraftVariantViewAnalysis variant, string sourcePath)
    {
        var aircraftFolder = Path.GetFullPath(Path.GetDirectoryName(variant.AcfPath) ?? "");
        var sourceFullPath = Path.GetFullPath(sourcePath);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return sourceFullPath.Equals(aircraftFolder, comparison)
            || sourceFullPath.StartsWith(aircraftFolder + Path.DirectorySeparatorChar, comparison);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static UnixFileMode? TryGetUnixFileMode(string path)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(path))
        {
            return null;
        }

#pragma warning disable CA1416
        return File.GetUnixFileMode(path);
#pragma warning restore CA1416
    }

    private static void TrySetUnixFileMode(string path, UnixFileMode? mode)
    {
        if (mode is null || OperatingSystem.IsWindows())
        {
            return;
        }

#pragma warning disable CA1416
        File.SetUnixFileMode(path, mode.Value);
#pragma warning restore CA1416
    }

    private static StringComparer StringComparerForCurrentPlatform() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static string FormatUpdateMode(AircraftUpdateMode mode) =>
        mode switch
        {
            AircraftUpdateMode.Full => "Full aircraft update",
            AircraftUpdateMode.Incremental => "Incremental aircraft update",
            _ => "Aircraft update"
        };

    private static string BuildOperationName(AircraftUpdateMode mode, string suffix) =>
        mode switch
        {
            AircraftUpdateMode.Full => $"AircraftUpdateFull{suffix}",
            AircraftUpdateMode.Incremental => $"AircraftUpdateIncremental{suffix}",
            _ => $"AircraftUpdate{suffix}"
        };

    private sealed record CachedPackageValidation(
        IReadOnlyList<string> BlockingMessages,
        IReadOnlyDictionary<string, AircraftUpdatePackageCacheEntry> CacheEntriesByFileName);
}
