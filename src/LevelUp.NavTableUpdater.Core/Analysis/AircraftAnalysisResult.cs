namespace LevelUp.NavTableUpdater.Core.Analysis;

public sealed record AircraftAnalysisResult(
    string AircraftPath,
    string TargetScriptPath,
    bool TargetScriptExists,
    InstallState State,
    string StateLabel,
    string LocalPackageVersion,
    string AvailablePackageVersion,
    string LineEnding,
    string Summary,
    bool IsSafeToPatch,
    IReadOnlyList<ComponentStatus> Components,
    IReadOnlyList<string> PlannedChanges,
    IReadOnlyList<string> Findings)
{
    public static AircraftAnalysisResult Empty(string availableVersion) =>
        new(
            AircraftPath: "",
            TargetScriptPath: "",
            TargetScriptExists: false,
            State: InstallState.NoTargetSelected,
            StateLabel: "No aircraft selected",
            LocalPackageVersion: "-",
            AvailablePackageVersion: availableVersion,
            LineEnding: "-",
            Summary: "Select or detect a Zibo or LevelUp aircraft folder to start.",
            IsSafeToPatch: false,
            Components: Array.Empty<ComponentStatus>(),
            PlannedChanges: Array.Empty<string>(),
            Findings: Array.Empty<string>());
}
