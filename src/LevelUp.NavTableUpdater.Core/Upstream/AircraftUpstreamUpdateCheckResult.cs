namespace LevelUp.NavTableUpdater.Core.Upstream;

public sealed record AircraftUpstreamUpdateCheckResult(
    string StateLabel,
    string Summary,
    string Family,
    string SourceUrl,
    string LocalVersionDisplay,
    string AvailableVersionDisplay,
    AircraftUpdatePlanAction Action,
    string ActionDisplay,
    bool IsCustomDistribution,
    IReadOnlyList<AircraftUpdatePackage> RequiredPackages,
    IReadOnlyList<string> Findings)
{
    public AircraftUpdateMode UpdateMode => RequiredPackages.Any(package => package.Kind == AircraftUpdatePackageKind.FullBaseline)
        ? AircraftUpdateMode.Full
        : RequiredPackages.Any(package => package.Kind == AircraftUpdatePackageKind.CumulativePatch)
            ? AircraftUpdateMode.Incremental
            : AircraftUpdateMode.None;

    public static AircraftUpstreamUpdateCheckResult NotApplicable(string summary, string sourceUrl = "") =>
        new(
            "Not applicable",
            summary,
            Family: "",
            SourceUrl: sourceUrl,
            LocalVersionDisplay: "-",
            AvailableVersionDisplay: "-",
            AircraftUpdatePlanAction.Unknown,
            ActionDisplay: "Not checked",
            IsCustomDistribution: false,
            RequiredPackages: [],
            Findings: []);
}
