using LevelUp.NavTableUpdater.Core.Platform;
using LevelUp.NavTableUpdater.Core.State;
using LevelUp.NavTableUpdater.Core.Transactions;

namespace LevelUp.NavTableUpdater.Core.Aircraft;

public sealed class ConfigBackupOperation
{
    private const string ConfigBackupOperationName = "ConfigBackup";
    private const string ConfigRestorePreImageOperationName = "ConfigRestorePreImage";

    private readonly ToolStateStore _stateStore;
    private readonly Func<bool> _isXPlaneRunning;

    public ConfigBackupOperation(ToolStateStore stateStore, Func<bool>? isXPlaneRunning = null)
    {
        _stateStore = stateStore;
        _isXPlaneRunning = isXPlaneRunning ?? XPlaneProcessDetector.IsXPlaneRunning;
    }

    public MaintenanceOperationResult CreateBackup(AircraftVariantViewAnalysis variant)
    {
        var log = new List<string>
        {
            $"[START] Create config backup for {variant.DisplayName}"
        };

        if (_isXPlaneRunning())
        {
            log.Add("[BLOCKED] X-Plane is running.");
            return MaintenanceOperationResult.Blocked("X-Plane is running. Close X-Plane before backing up config files.", log);
        }

        var configFiles = FindConfigFiles(variant).ToArray();
        if (configFiles.Length == 0)
        {
            log.Add("[BLOCKED] No supported config files were found.");
            return MaintenanceOperationResult.Blocked("No supported config files were found for this aircraft folder.", log);
        }

        var createdUtc = DateTimeOffset.UtcNow;
        var backupRecords = new List<BackupRecord>();

        foreach (var sourcePath in configFiles)
        {
            var backupPath = _stateStore.CreateBackupPath(variant, sourcePath, createdUtc);
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)
                ?? throw new InvalidOperationException("Backup path has no parent directory."));
            File.Copy(sourcePath, backupPath, overwrite: false);
            backupRecords.Add(BuildBackupRecord(ConfigBackupOperationName, sourcePath, backupPath, createdUtc, variant));
            log.Add($"[BACKUP] {sourcePath} -> {backupPath}");
        }

        _stateStore.UpdateTarget(variant, state =>
        {
            state.LastOperation = ConfigBackupOperationName;
            state.Backups.AddRange(backupRecords);
        });

        return MaintenanceOperationResult.Applied(
            $"Created config backup for {backupRecords.Count} file(s).",
            backupRecords.Select(record => record.BackupPath).ToArray(),
            log);
    }

    public MaintenanceOperationResult RestoreLatestConfigBackup(AircraftVariantViewAnalysis variant)
    {
        var log = new List<string>
        {
            $"[START] Restore latest config backup for {variant.DisplayName}"
        };

        if (_isXPlaneRunning())
        {
            log.Add("[BLOCKED] X-Plane is running.");
            return MaintenanceOperationResult.Blocked("X-Plane is running. Close X-Plane before restoring config files.", log);
        }

        var target = _stateStore.TryGetTarget(variant);
        if (target is null || target.Backups.Count == 0)
        {
            log.Add("[BLOCKED] No backups are recorded for this aircraft variant.");
            return MaintenanceOperationResult.Blocked("No config backups are recorded for this aircraft variant.", log);
        }

        var restoreRecords = SelectLatestConfigBackupGeneration(variant, target.Backups);
        if (restoreRecords.Length == 0)
        {
            log.Add("[BLOCKED] No valid config backup generation was found.");
            return MaintenanceOperationResult.Blocked("No valid config backup generation was found.", log);
        }

        var createdUtc = DateTimeOffset.UtcNow;
        var preRestoreBackups = new List<BackupRecord>();

        foreach (var record in restoreRecords)
        {
            var preRestoreBackupPath = _stateStore.CreateBackupPath(variant, record.SourcePath, createdUtc);
            RestoreFileTransaction.Restore(record.SourcePath, record.BackupPath, preRestoreBackupPath);
            preRestoreBackups.Add(BuildBackupRecord(ConfigRestorePreImageOperationName, record.SourcePath, preRestoreBackupPath, createdUtc, variant));
            log.Add($"[RESTORE] {record.SourcePath}");
            log.Add($"[BACKUP] Pre-restore image saved at {preRestoreBackupPath}");
        }

        _stateStore.UpdateTarget(variant, state =>
        {
            state.LastRestoreUtc = DateTimeOffset.UtcNow;
            state.LastOperation = "RestoreLatestConfigBackup";
            state.Backups.AddRange(preRestoreBackups);
        });

        return MaintenanceOperationResult.Restored(
            $"Restored {restoreRecords.Length} config file(s) from the latest config backup.",
            preRestoreBackups.Select(record => record.BackupPath).ToArray(),
            log);
    }

    private static IEnumerable<string> FindConfigFiles(AircraftVariantViewAnalysis variant)
    {
        var aircraftFolder = Path.GetFullPath(Path.GetDirectoryName(variant.AcfPath) ?? "");
        if (!Directory.Exists(aircraftFolder))
        {
            return [];
        }

        var comparison = StringComparerForCurrentPlatform();
        return Directory.EnumerateFiles(aircraftFolder)
            .Where(IsSupportedConfigFile)
            .Distinct(comparison)
            .OrderBy(path => path, comparison)
            .ToArray();
    }

    private static bool IsSupportedConfigFile(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.EndsWith("_prefs.txt", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("_vrconfig.txt", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("X-Camera_", StringComparison.OrdinalIgnoreCase) && fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "b738_config.txt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, AircraftMaintenanceMetadata.FileName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "version.txt", StringComparison.OrdinalIgnoreCase);
    }

    private static BackupRecord[] SelectLatestConfigBackupGeneration(
        AircraftVariantViewAnalysis variant,
        IReadOnlyList<BackupRecord> records)
    {
        var candidates = records
            .Where(record => string.Equals(record.Operation, ConfigBackupOperationName, StringComparison.Ordinal)
                && File.Exists(record.BackupPath)
                && IsInsideAircraftFolder(variant, record.SourcePath))
            .ToArray();
        if (candidates.Length == 0)
        {
            return [];
        }

        var latest = candidates.Max(record => record.CreatedUtc);
        var comparison = StringComparerForCurrentPlatform();
        return candidates
            .Where(record => record.CreatedUtc == latest)
            .GroupBy(record => Path.GetFullPath(record.SourcePath), comparison)
            .Select(group => group.Last())
            .OrderBy(record => record.SourcePath, comparison)
            .ToArray();
    }

    private static BackupRecord BuildBackupRecord(
        string operation,
        string sourcePath,
        string backupPath,
        DateTimeOffset createdUtc,
        AircraftVariantViewAnalysis variant) =>
        new()
        {
            Operation = operation,
            SourcePath = sourcePath,
            BackupPath = backupPath,
            CreatedUtc = createdUtc,
            CgYFeet = variant.CurrentCgYFeet,
            CgZFeet = variant.CurrentCgZFeet
        };

    private static bool IsInsideAircraftFolder(AircraftVariantViewAnalysis variant, string sourcePath)
    {
        var aircraftFolder = Path.GetFullPath(Path.GetDirectoryName(variant.AcfPath) ?? "");
        var sourceFullPath = Path.GetFullPath(sourcePath);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return sourceFullPath.Equals(aircraftFolder, comparison)
            || sourceFullPath.StartsWith(aircraftFolder + Path.DirectorySeparatorChar, comparison);
    }

    private static StringComparer StringComparerForCurrentPlatform() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
