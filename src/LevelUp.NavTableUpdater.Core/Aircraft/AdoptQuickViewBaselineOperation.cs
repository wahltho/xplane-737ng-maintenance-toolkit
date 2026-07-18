using LevelUp.NavTableUpdater.Core.State;
using LevelUp.NavTableUpdater.Core.Transactions;

namespace LevelUp.NavTableUpdater.Core.Aircraft;

public sealed class AdoptQuickViewBaselineOperation
{
    private readonly ToolStateStore _stateStore;

    public AdoptQuickViewBaselineOperation(ToolStateStore stateStore)
    {
        _stateStore = stateStore;
    }

    public MaintenanceOperationResult Adopt(AircraftVariantViewAnalysis variant)
    {
        var log = new List<string>
        {
            $"[START] Adopt current Quick View baseline for {variant.DisplayName}"
        };

        if (!File.Exists(variant.AcfPath))
        {
            log.Add("[BLOCKED] ACF file is missing.");
            return MaintenanceOperationResult.Blocked("ACF file is missing.", log);
        }

        var metadata = AircraftFileParser.ReadAcfMetadata(variant.AcfPath);
        if (metadata.Cg is null)
        {
            log.Add("[BLOCKED] ACF CG fields are incomplete.");
            return MaintenanceOperationResult.Blocked("ACF CG fields are incomplete.", log);
        }

        if (!File.Exists(variant.PrefsPath))
        {
            log.Add("[BLOCKED] Quick-view prefs file is missing.");
            return MaintenanceOperationResult.Blocked("Quick-view prefs file is missing.", log);
        }

        var prefsValidation = QuickViewPrefsTransaction.Validate(variant.PrefsPath);
        log.Add($"[CHECK] Quick-view prefs contain {prefsValidation.YKeyCount} Y keys and {prefsValidation.ZKeyCount} Z keys.");

        var xCameraPath = QuickViewBaselineFiles.GetXCameraPath(variant);
        if (File.Exists(xCameraPath))
        {
            var xCameraValidation = XCameraTransaction.Validate(xCameraPath);
            log.Add($"[CHECK] X-Camera CSV has {xCameraValidation.ChangedRows} cockpit-origin row(s) available for future CG adaptation.");
        }
        else
        {
            log.Add("[CHECK] No matching X-Camera CSV found.");
        }

        var prefsHash = QuickViewBaselineFiles.ComputeSha256IfExists(variant.PrefsPath);
        var xCameraHash = QuickViewBaselineFiles.ComputeSha256IfExists(xCameraPath);
        _stateStore.UpdateTarget(variant, target =>
        {
            target.LastQuickViewCgYFeet = metadata.Cg.YFeet;
            target.LastQuickViewCgZFeet = metadata.Cg.ZFeet;
            target.LastQuickViewBaselineSource = "AdoptedCurrent";
            target.LastQuickViewPrefsSha256 = prefsHash;
            target.LastQuickViewXCameraSha256 = xCameraHash;
            target.LastQuickViewAppliedUtc = DateTimeOffset.UtcNow;
            target.LastOperation = "AdoptQuickViewCurrentBaseline";
        });

        log.Add($"[CG] Adopted current Y {metadata.Cg.YFeet:0.000000000} ft, Z {metadata.Cg.ZFeet:0.000000000} ft.");
        log.Add("[OK] Current Quick View baseline recorded in toolkit state.");
        return MaintenanceOperationResult.Applied(
            "Current Quick View baseline was recorded. No aircraft files were changed.",
            [],
            log);
    }
}
