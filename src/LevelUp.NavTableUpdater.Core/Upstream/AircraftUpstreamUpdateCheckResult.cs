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
    IReadOnlyList<AircraftUpdatePackage> RequiredPackages,
    IReadOnlyList<string> Findings)
{
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
            RequiredPackages: [],
            Findings: []);
}
