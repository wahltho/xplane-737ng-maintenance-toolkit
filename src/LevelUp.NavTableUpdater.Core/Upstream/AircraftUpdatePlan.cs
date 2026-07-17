namespace LevelUp.NavTableUpdater.Core.Upstream;

public enum AircraftUpdatePlanAction
{
    Unknown = 0,
    UpToDate,
    ApplyCumulativePatch,
    InstallBaselineAndCumulativePatch,
    LocalNewerThanIndex,
    MissingRequiredPackage
}

public sealed record AircraftUpdatePlan(
    string Family,
    AircraftUpdatePlanAction Action,
    AircraftUpstreamVersion? LocalVersion,
    AircraftUpstreamVersion? AvailableVersion,
    AircraftUpstreamVersion? TargetBaseline,
    IReadOnlyList<AircraftUpdatePackage> RequiredPackages,
    string Summary)
{
    public bool HasUpdate =>
        Action is AircraftUpdatePlanAction.ApplyCumulativePatch
            or AircraftUpdatePlanAction.InstallBaselineAndCumulativePatch;
}
