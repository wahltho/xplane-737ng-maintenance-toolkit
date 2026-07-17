using LevelUp.NavTableUpdater.Core.Platform;
using LevelUp.NavTableUpdater.Core.State;
using LevelUp.NavTableUpdater.Core.Transactions;

namespace LevelUp.NavTableUpdater.Core.Aircraft;

public sealed class ApplyQuickViewCgAdaptOperation
{
    private const double FeetToMeters = 0.3048;
    private const double CgToleranceFeet = 0.001;
    private const string ExpectedIdentityStatus = "Expected metadata";

    private readonly ToolStateStore _stateStore;
    private readonly Func<bool> _isXPlaneRunning;

    public ApplyQuickViewCgAdaptOperation(ToolStateStore stateStore, Func<bool>? isXPlaneRunning = null)
    {
        _stateStore = stateStore;
        _isXPlaneRunning = isXPlaneRunning ?? XPlaneProcessDetector.IsXPlaneRunning;
    }

    public MaintenanceOperationResult Apply(AircraftVariantViewAnalysis variant)
    {
        var log = new List<string>
        {
            $"[START] Adapt Quick Views after CG change for {variant.DisplayName}"
        };

        if (_isXPlaneRunning())
        {
            log.Add("[BLOCKED] X-Plane is running.");
            return MaintenanceOperationResult.Blocked("X-Plane is running. Close X-Plane before changing aircraft files.", log);
        }

        if (!File.Exists(variant.AcfPath))
        {
            log.Add("[BLOCKED] ACF file is missing.");
            return MaintenanceOperationResult.Blocked("ACF file is missing.", log);
        }

        if (!File.Exists(variant.PrefsPath))
        {
            log.Add("[BLOCKED] Quick-view prefs file is missing.");
            return MaintenanceOperationResult.Blocked("Quick-view prefs file is missing.", log);
        }

        var metadata = AircraftFileParser.ReadAcfMetadata(variant.AcfPath);
        if (metadata.Cg is null)
        {
            log.Add("[BLOCKED] ACF CG fields are incomplete.");
            return MaintenanceOperationResult.Blocked("ACF CG fields are incomplete.", log);
        }

        var previousState = _stateStore.TryGetTarget(variant);
        var hasStoredQuickViewBaseline = previousState?.LastQuickViewCgYFeet is not null
            && previousState.LastQuickViewCgZFeet is not null;
        var baselineYFeet = hasStoredQuickViewBaseline
            ? previousState!.LastQuickViewCgYFeet!.Value
            : variant.ReferenceCgYFeet;
        var baselineZFeet = hasStoredQuickViewBaseline
            ? previousState!.LastQuickViewCgZFeet!.Value
            : variant.ReferenceCgZFeet;
        var deltaYFeet = metadata.Cg.YFeet - baselineYFeet;
        var deltaZFeet = metadata.Cg.ZFeet - baselineZFeet;
        var hasCgDelta = Math.Abs(deltaYFeet) > CgToleranceFeet || Math.Abs(deltaZFeet) > CgToleranceFeet;
        var hasExpectedIdentity = string.Equals(variant.IdentityStatus, ExpectedIdentityStatus, StringComparison.Ordinal);

        log.Add(hasStoredQuickViewBaseline
            ? "[CG] Baseline source: stored toolkit state."
            : "[CG] Baseline source: aircraft reference catalog.");
        log.Add($"[CG] Aircraft identity: {variant.IdentityStatus}.");
        log.Add($"[CG] Baseline Y {baselineYFeet:0.000000000} ft, Z {baselineZFeet:0.000000000} ft.");
        log.Add($"[CG] Current  Y {metadata.Cg.YFeet:0.000000000} ft, Z {metadata.Cg.ZFeet:0.000000000} ft.");
        log.Add($"[CG] Delta    Y {deltaYFeet:+0.000000;-0.000000;0.000000} ft, Z {deltaZFeet:+0.000000;-0.000000;0.000000} ft.");

        if (hasCgDelta && !hasStoredQuickViewBaseline && !hasExpectedIdentity)
        {
            log.Add("[BLOCKED] Aircraft metadata differs and no stored quick-view CG baseline exists.");
            return MaintenanceOperationResult.Blocked(
                "Aircraft metadata differs and no stored quick-view CG baseline exists. Review or recalibrate the baseline before adapting Quick Views.",
                log);
        }

        if (!hasCgDelta)
        {
            UpdateState(variant, metadata.Cg, backups: [], changed: false);
            log.Add("[NO-CHANGE] Quick Views already use the current CG baseline.");
            return MaintenanceOperationResult.NoChange("Quick Views already use the current CG baseline.", log);
        }

        var deltaYMeters = deltaYFeet * FeetToMeters;
        var deltaZMeters = deltaZFeet * FeetToMeters;
        var xCameraPath = GetXCameraPath(variant);
        var hasXCamera = File.Exists(xCameraPath);

        var prefsValidation = QuickViewPrefsTransaction.Validate(variant.PrefsPath);
        log.Add($"[CHECK] Quick-view prefs contain {prefsValidation.YKeyCount} Y keys and {prefsValidation.ZKeyCount} Z keys.");

        XCameraRewriteSummary? xCameraValidation = null;
        if (hasXCamera)
        {
            xCameraValidation = XCameraTransaction.Validate(xCameraPath);
            log.Add($"[CHECK] X-Camera CSV has {xCameraValidation.ChangedRows} cockpit-origin row(s) to adjust.");
        }
        else
        {
            log.Add("[CHECK] No matching X-Camera CSV found; skipping optional X-Camera adjustment.");
        }

        var createdUtc = DateTimeOffset.UtcNow;
        var backupRecords = new List<BackupRecord>();
        var appliedFiles = new List<(string SourcePath, string BackupPath)>();

        try
        {
            var prefsBackupPath = _stateStore.CreateBackupPath(variant, variant.PrefsPath, createdUtc);
            var prefsSummary = QuickViewPrefsTransaction.Apply(variant.PrefsPath, deltaYMeters, deltaZMeters, prefsBackupPath);
            appliedFiles.Add((variant.PrefsPath, prefsBackupPath));
            backupRecords.Add(BuildBackupRecord("ApplyQuickViewCgAdapt", variant.PrefsPath, prefsBackupPath, createdUtc, metadata.Cg));
            log.Add($"[BACKUP] {prefsBackupPath}");
            log.Add($"[OK] Adjusted {prefsSummary.YKeyCount} Y keys and {prefsSummary.ZKeyCount} Z keys in quick-view prefs.");

            if (hasXCamera && xCameraValidation is not null)
            {
                var xCameraBackupPath = _stateStore.CreateBackupPath(variant, xCameraPath, createdUtc);
                var xCameraSummary = XCameraTransaction.Apply(xCameraPath, deltaYMeters, deltaZMeters, xCameraBackupPath);
                appliedFiles.Add((xCameraPath, xCameraBackupPath));
                backupRecords.Add(BuildBackupRecord("ApplyQuickViewCgAdapt", xCameraPath, xCameraBackupPath, createdUtc, metadata.Cg));
                log.Add($"[BACKUP] {xCameraBackupPath}");
                log.Add($"[OK] Adjusted {xCameraSummary.ChangedRows} X-Camera row(s); skipped {xCameraSummary.SkippedRows} row(s).");
            }

            UpdateState(variant, metadata.Cg, backupRecords, changed: true);
        }
        catch
        {
            RollBackAppliedFiles(appliedFiles, log);
            throw;
        }

        log.Add("[OK] Quick-view CG adaptation completed.");
        return MaintenanceOperationResult.Applied(
            hasXCamera
                ? "Quick Views and matching X-Camera cameras were adjusted for the current CG."
                : "Quick Views were adjusted for the current CG.",
            backupRecords.Select(record => record.BackupPath).ToArray(),
            log);
    }

    private void UpdateState(
        AircraftVariantViewAnalysis variant,
        AircraftCg cg,
        IReadOnlyList<BackupRecord> backups,
        bool changed)
    {
        _stateStore.UpdateTarget(variant, target =>
        {
            target.LastQuickViewCgYFeet = cg.YFeet;
            target.LastQuickViewCgZFeet = cg.ZFeet;
            target.LastQuickViewAppliedUtc = DateTimeOffset.UtcNow;
            target.LastOperation = changed ? "ApplyQuickViewCgAdapt" : "ApplyQuickViewCgAdaptNoChange";
            target.Backups.AddRange(backups);
        });
    }

    private static BackupRecord BuildBackupRecord(
        string operation,
        string sourcePath,
        string backupPath,
        DateTimeOffset createdUtc,
        AircraftCg cg) =>
        new()
        {
            Operation = operation,
            SourcePath = sourcePath,
            BackupPath = backupPath,
            CreatedUtc = createdUtc,
            CgYFeet = cg.YFeet,
            CgZFeet = cg.ZFeet
        };

    private static void RollBackAppliedFiles(IReadOnlyList<(string SourcePath, string BackupPath)> appliedFiles, ICollection<string> log)
    {
        foreach (var (sourcePath, backupPath) in appliedFiles.Reverse())
        {
            if (!File.Exists(backupPath))
            {
                continue;
            }

            File.Copy(backupPath, sourcePath, overwrite: true);
            log.Add($"[ROLLBACK] Restored {sourcePath} from {backupPath}.");
        }
    }

    private static string GetXCameraPath(AircraftVariantViewAnalysis variant)
    {
        var aircraftFolder = Path.GetDirectoryName(variant.AcfPath) ?? "";
        var acfStem = Path.GetFileNameWithoutExtension(variant.AcfPath);
        return Path.Combine(aircraftFolder, $"X-Camera_{acfStem}.csv");
    }
}
