namespace LevelUp.NavTableUpdater.Core.Upstream;

public sealed class BaselineCumulativeUpdatePlanner
{
    public AircraftUpdatePlan Plan(AircraftUpdateIndex index, AircraftUpstreamVersion? localVersion)
    {
        ArgumentNullException.ThrowIfNull(index);

        var packages = index.Packages
            .Where(package => string.Equals(package.Family, index.Family, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var latestPackage = packages
            .Where(package => package.Kind is AircraftUpdatePackageKind.FullBaseline or AircraftUpdatePackageKind.CumulativePatch)
            .OrderBy(package => package.Version)
            .LastOrDefault();

        var availableVersion = latestPackage?.Version;
        if (availableVersion is null)
        {
            return new AircraftUpdatePlan(
                index.Family,
                AircraftUpdatePlanAction.MissingRequiredPackage,
                localVersion,
                AvailableVersion: null,
                TargetBaseline: null,
                RequiredPackages: [],
                "No upstream full package or cumulative patch was found.");
        }

        var targetBaseline = availableVersion.Value.Baseline;
        var latestPatch = packages
            .Where(package => package.Kind == AircraftUpdatePackageKind.CumulativePatch && package.Baseline == targetBaseline)
            .OrderBy(package => package.Version)
            .LastOrDefault();

        var fullForTargetBaseline = packages
            .Where(package => package.Kind == AircraftUpdatePackageKind.FullBaseline && package.Baseline == targetBaseline)
            .OrderBy(package => package.Version)
            .LastOrDefault();

        var patchForTargetBaseline = latestPatch;

        if (localVersion is null)
        {
            return PlanFullInstall(index.Family, localVersion, availableVersion, targetBaseline, fullForTargetBaseline, patchForTargetBaseline);
        }

        if (localVersion.Value > availableVersion.Value)
        {
            return new AircraftUpdatePlan(
                index.Family,
                AircraftUpdatePlanAction.LocalNewerThanIndex,
                localVersion,
                availableVersion,
                targetBaseline,
                RequiredPackages: [],
                $"Installed version {localVersion} is newer than the upstream index version {availableVersion}.");
        }

        if (localVersion.Value == availableVersion.Value)
        {
            return new AircraftUpdatePlan(
                index.Family,
                AircraftUpdatePlanAction.UpToDate,
                localVersion,
                availableVersion,
                targetBaseline,
                RequiredPackages: [],
                $"Installed version {localVersion} is current.");
        }

        if (localVersion.Value.Baseline != targetBaseline)
        {
            return PlanFullInstall(index.Family, localVersion, availableVersion, targetBaseline, fullForTargetBaseline, patchForTargetBaseline);
        }

        if (patchForTargetBaseline is null)
        {
            return new AircraftUpdatePlan(
                index.Family,
                AircraftUpdatePlanAction.MissingRequiredPackage,
                localVersion,
                availableVersion,
                targetBaseline,
                RequiredPackages: [],
                $"No cumulative patch was found for baseline {targetBaseline.ToBaselineString()}.");
        }

        return new AircraftUpdatePlan(
            index.Family,
            AircraftUpdatePlanAction.ApplyCumulativePatch,
            localVersion,
            availableVersion,
            targetBaseline,
            RequiredPackages: [patchForTargetBaseline],
            $"Apply cumulative patch {patchForTargetBaseline.FileName} to update {localVersion} to {availableVersion}.");
    }

    private static AircraftUpdatePlan PlanFullInstall(
        string family,
        AircraftUpstreamVersion? localVersion,
        AircraftUpstreamVersion? availableVersion,
        AircraftUpstreamVersion targetBaseline,
        AircraftUpdatePackage? fullForTargetBaseline,
        AircraftUpdatePackage? patchForTargetBaseline)
    {
        if (fullForTargetBaseline is null)
        {
            return new AircraftUpdatePlan(
                family,
                AircraftUpdatePlanAction.MissingRequiredPackage,
                localVersion,
                availableVersion,
                targetBaseline,
                RequiredPackages: [],
                $"No full baseline package was found for baseline {targetBaseline.ToBaselineString()}.");
        }

        var requiredPackages = patchForTargetBaseline is null
            ? [fullForTargetBaseline]
            : new[] { fullForTargetBaseline, patchForTargetBaseline };

        var sourceDescription = localVersion is null
            ? "a new or unversioned install"
            : $"installed baseline {localVersion.Value.ToBaselineString()}";

        return new AircraftUpdatePlan(
            family,
            AircraftUpdatePlanAction.InstallBaselineAndCumulativePatch,
            localVersion,
            availableVersion,
            targetBaseline,
            requiredPackages,
            $"Install full baseline {fullForTargetBaseline.FileName}"
                + (patchForTargetBaseline is null ? "" : $" and cumulative patch {patchForTargetBaseline.FileName}")
                + $" for {sourceDescription}.");
    }
}
