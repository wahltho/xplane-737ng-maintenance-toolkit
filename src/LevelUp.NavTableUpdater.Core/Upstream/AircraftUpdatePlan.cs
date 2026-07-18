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

public enum AircraftUpdateMode
{
    None = 0,
    Incremental,
    Full
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

    public AircraftUpdateMode UpdateMode => RequiredPackages.Any(package => package.Kind == AircraftUpdatePackageKind.FullBaseline)
        ? AircraftUpdateMode.Full
        : RequiredPackages.Any(package => package.Kind == AircraftUpdatePackageKind.CumulativePatch)
            ? AircraftUpdateMode.Incremental
            : AircraftUpdateMode.None;
}
