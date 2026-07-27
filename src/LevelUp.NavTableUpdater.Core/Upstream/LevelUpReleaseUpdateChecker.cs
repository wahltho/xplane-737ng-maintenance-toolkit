using LevelUp.NavTableUpdater.Core.Aircraft;

namespace LevelUp.NavTableUpdater.Core.Upstream;

public sealed class LevelUpReleaseUpdateChecker
{
    private readonly IAircraftUpdateIndexSource _indexSource;

    public LevelUpReleaseUpdateChecker(IAircraftUpdateIndexSource indexSource)
    {
        _indexSource = indexSource ?? throw new ArgumentNullException(nameof(indexSource));
    }

    public async Task<AircraftUpstreamUpdateCheckResult> CheckAsync(
        AircraftVariantViewAnalysis? variant,
        CancellationToken cancellationToken = default)
    {
        if (variant is null
            || !string.Equals(
                variant.Family,
                LevelUpAircraftUpdatePackageLoader.Family,
                StringComparison.OrdinalIgnoreCase))
        {
            return AircraftUpstreamUpdateCheckResult.NotApplicable(
                "Select a LevelUp aircraft variant before checking LevelUp releases.",
                LevelUpGitHubReleaseIndexSource.DefaultIndexUrl);
        }

        var findings = new List<string>
        {
            "Read-only check. No aircraft files are downloaded, extracted, backed up, or changed."
        };
        var maintenanceMetadata = ReadMaintenanceMetadata(variant, findings);
        var isCustomDistribution = maintenanceMetadata is not null
            && !string.IsNullOrWhiteSpace(maintenanceMetadata.Distribution);
        var index = await _indexSource.LoadAsync(cancellationToken);
        var packages = index.Packages
            .Where(package => string.Equals(
                package.Family,
                LevelUpAircraftUpdatePackageLoader.Family,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        findings.Add($"Release packages recognized: {packages.Length}.");

        var latestSequence = packages.Select(package => package.Version.Patch).DefaultIfEmpty().Max();
        var latestPackages = packages
            .Where(package => package.Version.Patch == latestSequence)
            .ToArray();
        var full = latestPackages.SingleOrDefault(
            package => package.Kind == AircraftUpdatePackageKind.FullBaseline);
        var patch = latestPackages.SingleOrDefault(
            package => package.Kind == AircraftUpdatePackageKind.CumulativePatch);
        var availableVersion = latestPackages
            .Select(package => package.ReleaseVersion)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        var localVersion = variant.LocalVersion;

        if (latestPackages.Length == 0 || string.IsNullOrWhiteSpace(availableVersion))
        {
            return BuildResult(
                "Package missing",
                "The LevelUp release index contains no usable package.",
                index,
                localVersion,
                "-",
                AircraftUpdatePlanAction.MissingRequiredPackage,
                "Blocked by incomplete index",
                isCustomDistribution,
                [],
                findings);
        }

        if (isCustomDistribution)
        {
            findings.Add(
                "Custom distribution detected. Official LevelUp packages are review-only for this target.");
            return BuildResult(
                "Custom port detected",
                $"Official LevelUp release {availableVersion} is available for review only.",
                index,
                localVersion,
                availableVersion,
                AircraftUpdatePlanAction.LocalNewerThanIndex,
                "Review-only package information",
                true,
                [],
                findings);
        }

        if (VersionsEqual(localVersion, availableVersion))
        {
            findings.Add("Installed LevelUp version matches the public release index.");
            return BuildResult(
                "Up to date",
                $"Installed LevelUp version {localVersion} is current.",
                index,
                localVersion,
                availableVersion,
                AircraftUpdatePlanAction.UpToDate,
                "No action",
                false,
                [],
                findings);
        }

        if (patch is not null && MatchesBaseline(localVersion, patch.Manifest))
        {
            findings.Add(
                $"Installed version matches cumulative patch baseline {patch.BaselineVersionDisplay}.");
            return BuildResult(
                "Incremental update available",
                $"Apply cumulative LevelUp patch {patch.FileName} to update {localVersion} to {availableVersion}.",
                index,
                localVersion,
                availableVersion,
                AircraftUpdatePlanAction.ApplyCumulativePatch,
                "Incremental: apply latest cumulative patch",
                false,
                [patch],
                findings);
        }

        if (full is not null)
        {
            findings.Add(
                string.IsNullOrWhiteSpace(localVersion)
                    ? "Installed LevelUp version is unknown; the exact full package is required."
                    : "Installed LevelUp version does not match the cumulative baseline; the exact full package is required.");
            return BuildResult(
                "Full update required",
                $"Apply full LevelUp package {full.FileName} to reach {availableVersion}.",
                index,
                localVersion,
                availableVersion,
                AircraftUpdatePlanAction.InstallBaselineAndCumulativePatch,
                "Full: apply exact release package",
                false,
                [full],
                findings);
        }

        findings.Add("No full package is available for an unmatched local baseline.");
        return BuildResult(
            "Baseline mismatch",
            "The installed LevelUp version does not match the cumulative patch baseline and no full package is available.",
            index,
            localVersion,
            availableVersion,
            AircraftUpdatePlanAction.BaselineMismatch,
            "Select a matching full package or baseline",
            false,
            [],
            findings);
    }

    private static AircraftUpstreamUpdateCheckResult BuildResult(
        string stateLabel,
        string summary,
        AircraftUpdateIndex index,
        string? localVersion,
        string availableVersion,
        AircraftUpdatePlanAction action,
        string actionDisplay,
        bool isCustomDistribution,
        IReadOnlyList<AircraftUpdatePackage> requiredPackages,
        IReadOnlyList<string> findings) =>
        new(
            stateLabel,
            summary,
            LevelUpAircraftUpdatePackageLoader.Family,
            index.SourceUrl,
            localVersion ?? "-",
            availableVersion,
            action,
            actionDisplay,
            isCustomDistribution,
            requiredPackages,
            findings);

    private static bool MatchesBaseline(
        string? localVersion,
        AircraftUpdatePackageManifest? manifest) =>
        manifest is not null
        && (VersionsEqual(localVersion, manifest.BaselineVersion)
            || manifest.BaselineAliases.Any(alias => VersionsEqual(localVersion, alias)));

    private static bool VersionsEqual(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(
            left.Trim().TrimStart('v', 'V'),
            right.Trim().TrimStart('v', 'V'),
            StringComparison.OrdinalIgnoreCase);

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
}
