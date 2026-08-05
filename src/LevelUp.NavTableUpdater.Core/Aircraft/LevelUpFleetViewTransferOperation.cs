using LevelUp.NavTableUpdater.Core.Platform;
using LevelUp.NavTableUpdater.Core.State;
using LevelUp.NavTableUpdater.Core.Transactions;

namespace LevelUp.NavTableUpdater.Core.Aircraft;

public sealed class LevelUpFleetViewTransferOperation
{
    private const double FeetToMeters = 0.3048;
    private const double DefaultViewToleranceFeet = 0.005;
    private const double PitchToleranceDegrees = 0.001;

    private readonly ToolStateStore _stateStore;
    private readonly Func<bool> _isXPlaneRunning;

    public LevelUpFleetViewTransferOperation(
        ToolStateStore stateStore,
        Func<bool>? isXPlaneRunning = null)
    {
        _stateStore = stateStore;
        _isXPlaneRunning = isXPlaneRunning ?? XPlaneProcessDetector.IsXPlaneRunning;
    }

    public MaintenanceOperationResult Apply(
        AircraftVariantViewAnalysis source,
        IReadOnlyList<AircraftVariantViewAnalysis> detectedVariants)
    {
        var log = new List<string>
        {
            $"[START] Copy Quick Views and Default Viewpoint from {source.DisplayName} to the LevelUp fleet."
        };

        if (_isXPlaneRunning())
        {
            log.Add("[BLOCKED] X-Plane is running.");
            return MaintenanceOperationResult.Blocked(
                "X-Plane is running. Close X-Plane before changing aircraft files.",
                log);
        }

        if (!string.Equals(source.Family, AircraftProductIds.LevelUp737Ng, StringComparison.OrdinalIgnoreCase))
        {
            log.Add("[BLOCKED] The source is not a LevelUp 737NG variant.");
            return MaintenanceOperationResult.Blocked(
                "Fleet view transfer is available only for LevelUp 737NG variants.",
                log);
        }

        var sourceFolder = Path.GetDirectoryName(Path.GetFullPath(source.AcfPath))
            ?? throw new InvalidOperationException("Source ACF path has no parent directory.");
        var targets = detectedVariants
            .Where(variant => string.Equals(variant.Family, AircraftProductIds.LevelUp737Ng, StringComparison.OrdinalIgnoreCase))
            .Where(variant => PathsEqual(Path.GetDirectoryName(Path.GetFullPath(variant.AcfPath)) ?? "", sourceFolder))
            .Where(variant => !PathsEqual(variant.AcfPath, source.AcfPath))
            .DistinctBy(variant => NormalizePath(variant.AcfPath), PathComparer)
            .OrderBy(variant => variant.DisplayName, StringComparer.Ordinal)
            .ToArray();

        if (targets.Length == 0)
        {
            log.Add("[BLOCKED] No other LevelUp variant was found in the source aircraft folder.");
            return MaintenanceOperationResult.Blocked(
                "No other LevelUp variant is available in this aircraft installation.",
                log);
        }

        ValidateSource(source);
        var sourceMetadata = AircraftFileParser.ReadAcfMetadata(source.AcfPath);
        var plans = new List<TargetPlan>(targets.Length);

        foreach (var target in targets)
        {
            ValidateTarget(target);
            var targetMetadata = AircraftFileParser.ReadAcfMetadata(target.AcfPath);
            var deltaYFeet = targetMetadata.Cg!.YFeet - sourceMetadata.Cg!.YFeet;
            var deltaZFeet = targetMetadata.Cg.ZFeet - sourceMetadata.Cg.ZFeet;
            var prefsPlan = QuickViewPrefsTransferTransaction.Plan(
                source.PrefsPath,
                target.PrefsPath,
                deltaYFeet * FeetToMeters,
                deltaZFeet * FeetToMeters);
            AcfDefaultViewTransaction.Validate(target.AcfPath);
            var defaultView = AircraftFileParser.CalculateDefaultViewFromQuickView(targetMetadata.Cg, prefsPlan.QuickView0);
            var defaultViewChanged = !DefaultViewMatches(targetMetadata.DefaultView!, defaultView);

            plans.Add(new TargetPlan(
                target,
                targetMetadata.Cg,
                deltaYFeet,
                deltaZFeet,
                prefsPlan,
                defaultView,
                defaultViewChanged));
            log.Add(
                $"[PLAN] {target.DisplayName}: {prefsPlan.KeyCount} Quick View keys, "
                + $"CG delta Y {deltaYFeet:+0.000000;-0.000000;0.000000} ft, "
                + $"Z {deltaZFeet:+0.000000;-0.000000;0.000000} ft; "
                + $"prefs {(prefsPlan.Changed ? "replace" : "unchanged")}, "
                + $"Default Viewpoint {(defaultViewChanged ? "replace" : "unchanged")}.");
        }

        var changedPlans = plans.Where(plan => plan.PrefsPlan.Changed || plan.DefaultViewChanged).ToArray();
        if (changedPlans.Length == 0)
        {
            log.Add("[NO-CHANGE] All LevelUp fleet views already match the selected source variant.");
            return MaintenanceOperationResult.NoChange(
                "All other LevelUp variants already match the selected source views.",
                log);
        }

        var createdUtc = DateTimeOffset.UtcNow;
        var appliedFiles = new List<(string SourcePath, string BackupPath)>();
        var recordsByAircraft = new Dictionary<string, List<BackupRecord>>(StringComparer.Ordinal);

        try
        {
            foreach (var plan in changedPlans)
            {
                var records = new List<BackupRecord>();
                recordsByAircraft[NormalizePath(plan.Variant.AcfPath)] = records;

                if (plan.PrefsPlan.Changed)
                {
                    var backupPath = _stateStore.CreateBackupPath(plan.Variant, plan.Variant.PrefsPath, createdUtc);
                    var applied = QuickViewPrefsTransferTransaction.Apply(
                        source.PrefsPath,
                        plan.Variant.PrefsPath,
                        plan.DeltaYFeet * FeetToMeters,
                        plan.DeltaZFeet * FeetToMeters,
                        backupPath);
                    appliedFiles.Add((plan.Variant.PrefsPath, backupPath));
                    records.Add(BuildBackupRecord(
                        "TransferLevelUpFleetQuickViews",
                        plan.Variant.PrefsPath,
                        backupPath,
                        createdUtc,
                        plan.Cg));
                    log.Add($"[BACKUP] {backupPath}");
                    log.Add($"[OK] {plan.Variant.DisplayName}: copied {applied.KeyCount} CG-adjusted Quick View keys.");
                }

                if (plan.DefaultViewChanged)
                {
                    var backupPath = _stateStore.CreateBackupPath(plan.Variant, plan.Variant.AcfPath, createdUtc);
                    AcfDefaultViewTransaction.Apply(plan.Variant.AcfPath, plan.DefaultView, backupPath);
                    appliedFiles.Add((plan.Variant.AcfPath, backupPath));
                    records.Add(BuildBackupRecord(
                        "TransferLevelUpFleetDefaultView",
                        plan.Variant.AcfPath,
                        backupPath,
                        createdUtc,
                        plan.Cg));
                    log.Add($"[BACKUP] {backupPath}");
                    log.Add($"[OK] {plan.Variant.DisplayName}: Default Viewpoint set from transferred Quick View 0.");
                }
            }

            _stateStore.UpdateTargets(changedPlans.Select(plan => plan.Variant).ToArray(), (variant, state) =>
            {
                var plan = changedPlans.Single(item => PathsEqual(item.Variant.AcfPath, variant.AcfPath));
                state.LastQuickViewCgYFeet = plan.Cg.YFeet;
                state.LastQuickViewCgZFeet = plan.Cg.ZFeet;
                state.LastQuickViewBaselineSource = $"CopiedFrom:{source.AircraftId}";
                state.LastQuickViewPrefsSha256 = QuickViewBaselineFiles.ComputeSha256IfExists(variant.PrefsPath);
                state.LastQuickViewXCameraSha256 = QuickViewBaselineFiles.ComputeSha256IfExists(QuickViewBaselineFiles.GetXCameraPath(variant));
                state.LastQuickViewAppliedUtc = createdUtc;
                state.LastDefaultViewCgYFeet = plan.Cg.YFeet;
                state.LastDefaultViewCgZFeet = plan.Cg.ZFeet;
                state.LastDefaultViewAppliedUtc = createdUtc;
                state.LastOperation = "TransferLevelUpFleetViews";
                state.Backups.AddRange(recordsByAircraft[NormalizePath(variant.AcfPath)]);
            });
        }
        catch
        {
            RollBackAppliedFiles(appliedFiles, log);
            throw;
        }

        log.Add($"[OK] LevelUp fleet view transfer completed for {changedPlans.Length} variant(s).");
        return MaintenanceOperationResult.Applied(
            $"Quick Views and Default Viewpoints were copied from {source.DisplayName} to {changedPlans.Length} other LevelUp variant(s).",
            appliedFiles.Select(file => file.BackupPath).ToArray(),
            log);
    }

    private static void ValidateSource(AircraftVariantViewAnalysis source)
    {
        if (!File.Exists(source.AcfPath))
        {
            throw new FileNotFoundException("Source ACF file is missing.", source.AcfPath);
        }

        if (!File.Exists(source.PrefsPath))
        {
            throw new FileNotFoundException("Source Quick View prefs file is missing.", source.PrefsPath);
        }

        var metadata = AircraftFileParser.ReadAcfMetadata(source.AcfPath);
        if (metadata.Cg is null)
        {
            throw new InvalidOperationException("Source ACF CG fields are incomplete.");
        }
    }

    private static void ValidateTarget(AircraftVariantViewAnalysis target)
    {
        if (!File.Exists(target.AcfPath))
        {
            throw new FileNotFoundException($"Target ACF file is missing for {target.DisplayName}.", target.AcfPath);
        }

        if (!File.Exists(target.PrefsPath))
        {
            throw new FileNotFoundException($"Target Quick View prefs file is missing for {target.DisplayName}.", target.PrefsPath);
        }

        var metadata = AircraftFileParser.ReadAcfMetadata(target.AcfPath);
        if (metadata.Cg is null || metadata.DefaultView is null)
        {
            throw new InvalidOperationException($"Target ACF CG or Default Viewpoint fields are incomplete for {target.DisplayName}.");
        }
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

    private static void RollBackAppliedFiles(
        IReadOnlyList<(string SourcePath, string BackupPath)> appliedFiles,
        ICollection<string> log)
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

    private static bool DefaultViewMatches(DefaultView actual, DefaultView expected) =>
        Math.Abs(actual.XFeet - expected.XFeet) <= DefaultViewToleranceFeet
        && Math.Abs(actual.YFeet - expected.YFeet) <= DefaultViewToleranceFeet
        && Math.Abs(actual.ZFeet - expected.ZFeet) <= DefaultViewToleranceFeet
        && Math.Abs(actual.PitchDegrees - expected.PitchDegrees) <= PitchToleranceDegrees;

    private static bool PathsEqual(string left, string right) =>
        PathComparer.Equals(NormalizePath(left), NormalizePath(right));

    private static string NormalizePath(string path) => Path.GetFullPath(path);

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private sealed record TargetPlan(
        AircraftVariantViewAnalysis Variant,
        AircraftCg Cg,
        double DeltaYFeet,
        double DeltaZFeet,
        QuickViewPrefsTransferPlan PrefsPlan,
        DefaultView DefaultView,
        bool DefaultViewChanged);
}
