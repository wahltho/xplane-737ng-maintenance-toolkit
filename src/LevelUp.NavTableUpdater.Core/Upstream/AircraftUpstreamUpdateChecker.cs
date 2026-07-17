using LevelUp.NavTableUpdater.Core.Aircraft;

namespace LevelUp.NavTableUpdater.Core.Upstream;

public sealed class AircraftUpstreamUpdateChecker
{
    private const string ZiboViewFamily = "zibo-737ng";

    private readonly IAircraftUpdateIndexSource _indexSource;
    private readonly BaselineCumulativeUpdatePlanner _planner;

    public AircraftUpstreamUpdateChecker(
        IAircraftUpdateIndexSource indexSource,
        BaselineCumulativeUpdatePlanner? planner = null)
    {
        _indexSource = indexSource;
        _planner = planner ?? new BaselineCumulativeUpdatePlanner();
    }

    public async Task<AircraftUpstreamUpdateCheckResult> CheckZiboAsync(
        AircraftVariantViewAnalysis? variant,
        CancellationToken cancellationToken = default)
    {
        if (variant is null)
        {
            return AircraftUpstreamUpdateCheckResult.NotApplicable(
                "Select a Zibo aircraft variant before checking upstream aircraft packages.",
                ZiboUpstreamFeedParser.DefaultFeedUrl);
        }

        if (!string.Equals(variant.Family, ZiboViewFamily, StringComparison.OrdinalIgnoreCase))
        {
            return AircraftUpstreamUpdateCheckResult.NotApplicable(
                "The upstream aircraft update check is currently implemented for Zibo only.",
                ZiboUpstreamFeedParser.DefaultFeedUrl);
        }

        var findings = new List<string>
        {
            "Read-only check. No aircraft files are downloaded, extracted, backed up, or changed."
        };

        AircraftUpstreamVersion? localVersion = null;
        if (AircraftUpstreamVersion.TryParse(variant.LocalVersion, out var parsedLocalVersion))
        {
            localVersion = parsedLocalVersion;
        }
        else
        {
            findings.Add("Local Zibo version could not be parsed from version.txt; planner will require a full baseline package.");
        }

        var index = await _indexSource.LoadAsync(cancellationToken);
        var plan = _planner.Plan(index, localVersion);

        findings.Add($"Index packages recognized: {index.Packages.Count}.");
        findings.Add(plan.RequiredPackages.Count == 0
            ? "No upstream package is required by this plan."
            : "Required package list is a plan only; package download and install are not implemented in this read-only step.");

        return new AircraftUpstreamUpdateCheckResult(
            BuildStateLabel(plan.Action),
            plan.Summary,
            index.Family,
            index.SourceUrl,
            localVersion?.ToString() ?? "-",
            plan.AvailableVersion?.ToString() ?? "-",
            plan.Action,
            BuildActionDisplay(plan.Action),
            plan.RequiredPackages,
            findings);
    }

    private static string BuildStateLabel(AircraftUpdatePlanAction action) =>
        action switch
        {
            AircraftUpdatePlanAction.UpToDate => "Up to date",
            AircraftUpdatePlanAction.ApplyCumulativePatch => "Patch available",
            AircraftUpdatePlanAction.InstallBaselineAndCumulativePatch => "Baseline update required",
            AircraftUpdatePlanAction.LocalNewerThanIndex => "Local newer than index",
            AircraftUpdatePlanAction.MissingRequiredPackage => "Package missing",
            _ => "Not checked"
        };

    private static string BuildActionDisplay(AircraftUpdatePlanAction action) =>
        action switch
        {
            AircraftUpdatePlanAction.UpToDate => "No action",
            AircraftUpdatePlanAction.ApplyCumulativePatch => "Apply latest cumulative patch",
            AircraftUpdatePlanAction.InstallBaselineAndCumulativePatch => "Install full baseline plus latest cumulative patch",
            AircraftUpdatePlanAction.LocalNewerThanIndex => "Review manually",
            AircraftUpdatePlanAction.MissingRequiredPackage => "Blocked by incomplete index",
            _ => "Not checked"
        };
}
