using LevelUp.NavTableUpdater.Core.Platform;
using LevelUp.NavTableUpdater.Core.State;
using LevelUp.NavTableUpdater.Core.Transactions;

namespace LevelUp.NavTableUpdater.Core.Aircraft;

public sealed class RestoreLatestBackupOperation
{
    private readonly ToolStateStore _stateStore;
    private readonly Func<bool> _isXPlaneRunning;

    public RestoreLatestBackupOperation(ToolStateStore stateStore, Func<bool>? isXPlaneRunning = null)
    {
        _stateStore = stateStore;
        _isXPlaneRunning = isXPlaneRunning ?? XPlaneProcessDetector.IsXPlaneRunning;
    }

    public MaintenanceOperationResult Restore(AircraftVariantViewAnalysis variant)
    {
        var log = new List<string>
        {
            $"[START] Restore latest backup for {variant.DisplayName}"
        };

        if (_isXPlaneRunning())
        {
            log.Add("[BLOCKED] X-Plane is running.");
            return MaintenanceOperationResult.Blocked("X-Plane is running. Close X-Plane before restoring aircraft files.", log);
        }

        var target = _stateStore.TryGetTarget(variant);
        if (target is null || target.Backups.Count == 0)
        {
            log.Add("[BLOCKED] No backups are recorded for this aircraft variant.");
            return MaintenanceOperationResult.Blocked("No backups are recorded for this aircraft variant.", log);
        }

        var restoreRecords = SelectLatestRestoreGeneration(variant, target.Backups);
        if (restoreRecords.Length == 0)
        {
            log.Add("[BLOCKED] No valid restoreable backup generation was found.");
            return MaintenanceOperationResult.Blocked("No valid restoreable backup generation was found.", log);
        }

        var createdUtc = DateTimeOffset.UtcNow;
        var preRestoreBackups = new List<BackupRecord>();

        foreach (var record in restoreRecords)
        {
            var preRestoreBackupPath = _stateStore.CreateBackupPath(variant, record.SourcePath, createdUtc);
            RestoreFileTransaction.Restore(record.SourcePath, record.BackupPath, preRestoreBackupPath);
            preRestoreBackups.Add(new BackupRecord
            {
                Operation = "RestorePreImage",
                SourcePath = record.SourcePath,
                BackupPath = preRestoreBackupPath,
                CreatedUtc = createdUtc,
                CgYFeet = variant.CurrentCgYFeet,
                CgZFeet = variant.CurrentCgZFeet
            });
            log.Add($"[RESTORE] {record.SourcePath}");
            log.Add($"[BACKUP] Pre-restore image saved at {preRestoreBackupPath}");
        }

        _stateStore.UpdateTarget(variant, state =>
        {
            state.LastRestoreUtc = DateTimeOffset.UtcNow;
            state.LastOperation = "RestoreLatestBackup";
            state.Backups.AddRange(preRestoreBackups);
        });

        return MaintenanceOperationResult.Restored(
            $"Restored {restoreRecords.Length} file(s) from the latest backup generation.",
            preRestoreBackups.Select(record => record.BackupPath).ToArray(),
            log);
    }

    private static BackupRecord[] SelectLatestRestoreGeneration(
        AircraftVariantViewAnalysis variant,
        IReadOnlyList<BackupRecord> records)
    {
        var candidates = records
            .Where(record => !string.Equals(record.Operation, "RestorePreImage", StringComparison.Ordinal)
                && File.Exists(record.BackupPath)
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

    private static StringComparer StringComparerForCurrentPlatform() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
