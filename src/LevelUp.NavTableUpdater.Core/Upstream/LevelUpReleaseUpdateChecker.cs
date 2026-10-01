using LevelUp.NavTableUpdater.Core.Aircraft;
using System.Text.RegularExpressions;

namespace LevelUp.NavTableUpdater.Core.Upstream;

public sealed class LevelUpReleaseUpdateChecker
{
    private static readonly Regex ReleaseVersionPattern = new(
        @"\A[vV]?(\d+)\.[sS](\d+)(?:\.(\d+)([a-zA-Z]?))?\z",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
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

        if (TryCompareReleaseVersions(localVersion, availableVersion, out var comparison) && comparison > 0)
        {
            findings.Add("The installed LevelUp release is newer than the public release; automatic downgrade is blocked.");
            return BuildResult(
                "Local version is newer",
                $"Installed LevelUp {localVersion} is newer than published {availableVersion}. No downgrade will be applied.",
                index, localVersion, availableVersion,
                AircraftUpdatePlanAction.LocalNewerThanIndex,
                "No automatic downgrade", false, [], findings);
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

        var baseline = FindPublishedBaseline(packages, full, patch, latestSequence);
        if (full is not null || baseline is not null && patch is not null)
        {
            findings.Add(
                string.IsNullOrWhiteSpace(localVersion)
                    ? "Installed LevelUp version is unknown; the exact full package is required."
                    : "Installed LevelUp version does not match the cumulative baseline; the exact full package is required.");
            if (!TryCompareReleaseVersions(localVersion, availableVersion, out _))
            {
                findings.Add("The installed version cannot be compared reliably. Review and explicitly confirm the full replacement; the cumulative patch will not be applied to the existing files.");
            }
            var requiredPackages = full is not null
                ? new[] { full }
                : new[] { baseline!, patch! };
            findings.Add("Stage the published full package and any cumulative update before replacing the aircraft. Retain the complete previous aircraft as a restore backup.");
            return BuildResult(
                "Full update required",
                $"Rebuild LevelUp {availableVersion} from verified release packages. Preserve preferences and local liveries; back up the complete existing aircraft.",
                index,
                localVersion,
                availableVersion,
                AircraftUpdatePlanAction.InstallBaselineAndCumulativePatch,
                full is not null ? "Full: apply exact release package" : "Full: install published baseline and latest cumulative update",
                false,
                requiredPackages,
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

    public async Task<AircraftUpstreamUpdateCheckResult> CheckFreshInstallAsync(
        CancellationToken cancellationToken = default)
    {
        var findings = new List<string>
        {
            "Fresh-install planning only. No aircraft files are downloaded, extracted, or changed."
        };
        var index = await _indexSource.LoadAsync(cancellationToken);
        var packages = index.Packages
            .Where(package => string.Equals(
                package.Family,
                LevelUpAircraftUpdatePackageLoader.Family,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        findings.Add($"Release packages recognized: {packages.Length}.");

        var latestSequence = packages.Select(package => package.Version.Patch).DefaultIfEmpty().Max();
        var full = packages.SingleOrDefault(package =>
            package.Kind == AircraftUpdatePackageKind.FullBaseline
            && package.Version.Patch == latestSequence);
        var patch = packages.SingleOrDefault(package =>
            package.Kind == AircraftUpdatePackageKind.CumulativePatch
            && package.Version.Patch == latestSequence);
        var baseline = FindPublishedBaseline(packages, full, patch, latestSequence);
        if (baseline is not null && patch is not null)
        {
            findings.Add("Install the verified published full baseline followed by the latest cumulative patch in staging.");
            return BuildResult(
                "Ready to install",
                $"Install LevelUp {baseline.ReleaseVersion} and cumulative update {patch.ReleaseVersion}.",
                index, "Not installed", patch.ReleaseVersion!,
                AircraftUpdatePlanAction.InstallBaselineAndCumulativePatch,
                "Install full baseline and latest cumulative update", false,
                [baseline, patch], findings);
        }

        var availableVersion = full?.ReleaseVersion;
        if (full is null || string.IsNullOrWhiteSpace(availableVersion))
        {
            findings.Add("The release index does not contain an exact full package for the latest release.");
            return BuildResult(
                "Package missing",
                "The LevelUp release index contains no usable full package for a fresh install.",
                index,
                localVersion: null,
                availableVersion: "-",
                AircraftUpdatePlanAction.MissingRequiredPackage,
                "Blocked by incomplete index",
                isCustomDistribution: false,
                [],
                findings);
        }

        findings.Add("The exact full LevelUp release package will be used for the fresh install.");
        return BuildResult(
            "Ready to install",
            $"Install full LevelUp package {full.FileName} as {availableVersion}.",
            index,
            localVersion: "Not installed",
            availableVersion,
            AircraftUpdatePlanAction.InstallBaselineAndCumulativePatch,
            "Install exact full release package",
            isCustomDistribution: false,
            [full],
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

    private static AircraftUpdatePackage? FindPublishedBaseline(
        IReadOnlyList<AircraftUpdatePackage> packages,
        AircraftUpdatePackage? full,
        AircraftUpdatePackage? patch,
        int latestSequence) =>
        full is null && patch is not null
            ? packages.SingleOrDefault(package => package.Kind == AircraftUpdatePackageKind.FullBaseline
                && VersionsEqual(package.ReleaseVersion, patch.BaselineVersion)
                && package.Version.Patch < latestSequence)
            : null;

    // LevelUp release labels are not SemVer: S1.51C must compare after S1.51B,
    // and S2 must compare after S1 regardless of the patch number.
    public static bool TryCompareReleaseVersions(string? left, string? right, out int comparison)
    {
        comparison = 0;
        var leftMatch = ReleaseVersionPattern.Match(left?.Trim() ?? "");
        var rightMatch = ReleaseVersionPattern.Match(right?.Trim() ?? "");
        if (!leftMatch.Success || !rightMatch.Success) return false;
        for (var group = 1; group <= 3; group++)
        {
            var leftPart = leftMatch.Groups[group].Success ? leftMatch.Groups[group].Value : "0";
            var rightPart = rightMatch.Groups[group].Success ? rightMatch.Groups[group].Value : "0";
            if (!int.TryParse(leftPart, out var leftNumber) || !int.TryParse(rightPart, out var rightNumber)) return false;
            if (comparison == 0) comparison = leftNumber.CompareTo(rightNumber);
        }
        if (comparison == 0)
            comparison = string.Compare(leftMatch.Groups[4].Value, rightMatch.Groups[4].Value, StringComparison.OrdinalIgnoreCase);
        return true;
    }

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
