using LevelUp.NavTableUpdater.Core.State;
using LevelUp.NavTableUpdater.Core.Platform;
using LevelUp.NavTableUpdater.Core.Transactions;

namespace LevelUp.NavTableUpdater.Core.Aircraft;

public sealed class ApplyDefaultViewFromQv0Operation
{
    private readonly ToolStateStore _stateStore;
    private readonly Func<bool> _isXPlaneRunning;

    public ApplyDefaultViewFromQv0Operation(ToolStateStore stateStore, Func<bool>? isXPlaneRunning = null)
    {
        _stateStore = stateStore;
        _isXPlaneRunning = isXPlaneRunning ?? XPlaneProcessDetector.IsXPlaneRunning;
    }

    public ApplyDefaultViewResult Apply(AircraftVariantViewAnalysis variant)
    {
        var log = new List<string>
        {
            $"[START] Apply QV0 to Default View for {variant.DisplayName}"
        };

        if (_isXPlaneRunning())
        {
            log.Add("[BLOCKED] X-Plane is running.");
            return ApplyDefaultViewResult.Blocked("X-Plane is running. Close X-Plane before changing aircraft files.", log);
        }

        if (!File.Exists(variant.AcfPath))
        {
            log.Add("[BLOCKED] ACF file is missing.");
            return ApplyDefaultViewResult.Blocked("ACF file is missing.", log);
        }

        if (!File.Exists(variant.PrefsPath))
        {
            log.Add("[BLOCKED] Quick-view prefs file is missing.");
            return ApplyDefaultViewResult.Blocked("Quick-view prefs file is missing.", log);
        }

        var metadata = AircraftFileParser.ReadAcfMetadata(variant.AcfPath);
        if (metadata.Cg is null)
        {
            log.Add("[BLOCKED] ACF CG fields are incomplete.");
            return ApplyDefaultViewResult.Blocked("ACF CG fields are incomplete.", log);
        }

        if (metadata.DefaultView is null)
        {
            log.Add("[BLOCKED] ACF default-view fields are incomplete.");
            return ApplyDefaultViewResult.Blocked("ACF default-view fields are incomplete.", log);
        }

        var quickView0 = AircraftFileParser.ReadQuickView0(variant.PrefsPath);
        if (quickView0 is null)
        {
            log.Add("[BLOCKED] Quick View 0 fields are incomplete.");
            return ApplyDefaultViewResult.Blocked("Quick View 0 fields are incomplete.", log);
        }

        var targetDefaultView = AircraftFileParser.CalculateDefaultViewFromQuickView(metadata.Cg, quickView0);
        if (DefaultViewMatches(metadata.DefaultView, targetDefaultView))
        {
            log.Add("[NO-CHANGE] Default View already matches Quick View 0.");
            UpdateState(variant, metadata.Cg, backup: null, changed: false);
            return ApplyDefaultViewResult.NoChange("Default View already matches Quick View 0.", log);
        }

        var createdUtc = DateTimeOffset.UtcNow;
        var backupPath = _stateStore.CreateBackupPath(variant, variant.AcfPath, createdUtc);
        log.Add($"[BACKUP] {backupPath}");

        AcfDefaultViewTransaction.Apply(variant.AcfPath, targetDefaultView, backupPath);
        UpdateState(variant, metadata.Cg, new BackupRecord
        {
            Operation = "ApplyQv0ToDefaultView",
            SourcePath = variant.AcfPath,
            BackupPath = backupPath,
            CreatedUtc = createdUtc,
            CgYFeet = metadata.Cg.YFeet,
            CgZFeet = metadata.Cg.ZFeet
        }, changed: true);

        log.Add("[OK] ACF default-view fields updated.");
        return ApplyDefaultViewResult.Applied("Default View was updated from Quick View 0.", backupPath, log);
    }

    private void UpdateState(AircraftVariantViewAnalysis variant, AircraftCg cg, BackupRecord? backup, bool changed)
    {
        _stateStore.UpdateTarget(variant, target =>
        {
            target.LastDefaultViewCgYFeet = cg.YFeet;
            target.LastDefaultViewCgZFeet = cg.ZFeet;
            target.LastDefaultViewAppliedUtc = DateTimeOffset.UtcNow;
            target.LastOperation = changed ? "ApplyQv0ToDefaultView" : "ApplyQv0ToDefaultViewNoChange";
            if (backup is not null)
            {
                target.Backups.Add(backup);
            }
        });
    }

    private static bool DefaultViewMatches(DefaultView actual, DefaultView expected)
    {
        return Math.Abs(actual.XFeet - expected.XFeet) <= 0.005
            && Math.Abs(actual.YFeet - expected.YFeet) <= 0.005
            && Math.Abs(actual.ZFeet - expected.ZFeet) <= 0.005
            && Math.Abs(actual.PitchDegrees - expected.PitchDegrees) <= 0.001;
    }
}

public sealed record ApplyDefaultViewResult(
    bool Succeeded,
    bool Changed,
    string Status,
    string Message,
    string? BackupPath,
    IReadOnlyList<string> Log)
{
    public static ApplyDefaultViewResult Applied(string message, string backupPath, IReadOnlyList<string> log) =>
        new(Succeeded: true, Changed: true, Status: "Applied", message, backupPath, log);

    public static ApplyDefaultViewResult NoChange(string message, IReadOnlyList<string> log) =>
        new(Succeeded: true, Changed: false, Status: "No change", message, BackupPath: null, log);

    public static ApplyDefaultViewResult Blocked(string message, IReadOnlyList<string> log) =>
        new(Succeeded: false, Changed: false, Status: "Blocked", message, BackupPath: null, log);
}
