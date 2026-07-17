using System.Text.RegularExpressions;
using LevelUp.NavTableUpdater.Core.Aircraft;

namespace LevelUp.NavTableUpdater.Core.Upstream;

public sealed class AircraftUpstreamUpdateChecker
{
    private const string ZiboViewFamily = "zibo-737ng";
    private static readonly Regex ZiboLuaVersionPattern =
        new(@"\bversion\s*=\s*[""']v?(\d+\.\d+(?:\.\d+)?)[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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

        var maintenanceMetadata = ReadMaintenanceMetadata(variant, findings);
        var isCustomDistribution = maintenanceMetadata is not null
            && !string.IsNullOrWhiteSpace(maintenanceMetadata.Distribution);
        var localVersion = ResolveLocalZiboVersion(variant, findings);

        var index = await _indexSource.LoadAsync(cancellationToken);
        var plan = _planner.Plan(index, localVersion);
        var requiredPackages = isCustomDistribution ? [] : plan.RequiredPackages;

        findings.Add($"Index packages recognized: {index.Packages.Count}.");
        if (isCustomDistribution)
        {
            findings.Add("Custom distribution detected. Official upstream packages are review-only and will not be treated as directly installable for this target.");
        }

        findings.Add(requiredPackages.Count == 0
            ? "No upstream package is required by this plan."
            : "Required package list is a plan only; package download and install are not implemented in this read-only step.");

        return new AircraftUpstreamUpdateCheckResult(
            isCustomDistribution ? "Custom port detected" : BuildStateLabel(plan.Action),
            isCustomDistribution ? BuildCustomDistributionSummary(maintenanceMetadata!, plan) : plan.Summary,
            index.Family,
            index.SourceUrl,
            localVersion?.ToString() ?? "-",
            plan.AvailableVersion?.ToString() ?? "-",
            isCustomDistribution ? AircraftUpdatePlanAction.LocalNewerThanIndex : plan.Action,
            isCustomDistribution ? "Review-only upstream information" : BuildActionDisplay(plan.Action),
            isCustomDistribution,
            requiredPackages,
            findings);
    }

    private static AircraftMaintenanceMetadata? ReadMaintenanceMetadata(
        AircraftVariantViewAnalysis variant,
        ICollection<string> findings)
    {
        var aircraftFolder = Path.GetDirectoryName(variant.AcfPath);
        if (string.IsNullOrWhiteSpace(aircraftFolder))
        {
            return null;
        }

        var metadata = AircraftFileParser.ReadMaintenanceMetadata(aircraftFolder, out var error);
        if (error is not null)
        {
            findings.Add(error);
        }

        return metadata;
    }

    private static AircraftUpstreamVersion? ResolveLocalZiboVersion(
        AircraftVariantViewAnalysis variant,
        ICollection<string> findings)
    {
        if (AircraftUpstreamVersion.TryParse(variant.LocalVersion, out var versionTxtVersion))
        {
            findings.Add("Local Zibo version read from version.txt.");
            return versionTxtVersion;
        }

        var aircraftFolder = Path.GetDirectoryName(variant.AcfPath);
        if (string.IsNullOrWhiteSpace(aircraftFolder))
        {
            findings.Add("Local Zibo version could not be parsed and the aircraft folder could not be resolved.");
            return null;
        }

        var fmsLuaPath = Path.Combine(
            aircraftFolder,
            "plugins",
            "xlua",
            "scripts",
            "B738.a_fms",
            "B738.a_fms.lua");

        if (!File.Exists(fmsLuaPath))
        {
            findings.Add("Local Zibo version could not be parsed from version.txt and B738.a_fms.lua was not found.");
            return null;
        }

        foreach (var line in File.ReadLines(fmsLuaPath))
        {
            var match = ZiboLuaVersionPattern.Match(line);
            if (!match.Success)
            {
                continue;
            }

            if (AircraftUpstreamVersion.TryParse(match.Groups[1].Value, out var luaVersion))
            {
                findings.Add("Local Zibo version read from plugins/xlua/scripts/B738.a_fms/B738.a_fms.lua.");
                return luaVersion;
            }
        }

        findings.Add("Local Zibo version could not be parsed from version.txt or B738.a_fms.lua.");
        return null;
    }

    private static string BuildCustomDistributionSummary(
        AircraftMaintenanceMetadata metadata,
        AircraftUpdatePlan plan)
    {
        var distribution = string.IsNullOrWhiteSpace(metadata.Distribution)
            ? "custom distribution"
            : metadata.Distribution;
        var distributionVersion = string.IsNullOrWhiteSpace(metadata.DistributionVersion)
            ? "unknown version"
            : metadata.DistributionVersion;
        var upstreamBase = string.IsNullOrWhiteSpace(metadata.UpstreamBaseVersion)
            ? "unknown upstream base"
            : metadata.UpstreamBaseVersion;
        var available = plan.AvailableVersion?.ToString() ?? "-";

        return $"{distribution} {distributionVersion} is based on {upstreamBase}. Official upstream index currently offers {available}; review only.";
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
