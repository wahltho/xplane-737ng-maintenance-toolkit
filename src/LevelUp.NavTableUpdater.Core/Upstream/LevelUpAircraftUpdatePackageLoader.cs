using LevelUp.NavTableUpdater.Core.Aircraft;

namespace LevelUp.NavTableUpdater.Core.Upstream;

public sealed record LevelUpAircraftUpdatePackageSelection(
    AircraftUpstreamUpdateCheckResult UpdateCheck,
    AircraftUpdatePackage? Package,
    string? ArchivePath,
    string ManifestPath);

public sealed class LevelUpAircraftUpdatePackageLoader
{
    public const string Family = "levelup-737ng";

    public LevelUpAircraftUpdatePackageSelection Load(
        string selectedPath,
        AircraftVariantViewAnalysis variant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedPath);
        ArgumentNullException.ThrowIfNull(variant);

        if (!string.Equals(variant.Family, Family, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A LevelUp update package can only be planned for a detected LevelUp aircraft.");
        }

        var manifestPath = ResolveManifestPath(selectedPath);
        var manifest = AircraftUpdatePackageManifestParser.Load(manifestPath);
        var archivePath = Path.Combine(Path.GetDirectoryName(manifestPath)!, manifest.Archive.FileName);
        var versionNumber = manifest.ReleaseSequence is > 0 and <= int.MaxValue
            ? (int)manifest.ReleaseSequence.Value
            : 0;
        var package = new AircraftUpdatePackage(
            Family,
            manifest.PackageKind,
            new AircraftUpstreamVersion(0, 0, versionNumber),
            manifest.Archive.FileName,
            SourceUrl: "",
            ReleaseVersion: manifest.ReleaseVersion,
            BaselineVersion: manifest.BaselineVersion,
            ExpectedSizeBytes: manifest.Archive.Size,
            ExpectedSha256: manifest.Archive.Sha256,
            Manifest: manifest);

        var findings = new List<string>
        {
            "Local LevelUp package plan. No aircraft files are changed while the manifest is loaded.",
            $"Manifest: {manifestPath}",
            $"Archive: {archivePath}",
            $"Manifest files: {manifest.Files.Count}; deleted paths: {manifest.DeletedPaths.Count}."
        };
        var localVersion = variant.LocalVersion;

        var maintenanceMetadata = ReadMaintenanceMetadata(variant, findings);
        if (maintenanceMetadata is not null && !string.IsNullOrWhiteSpace(maintenanceMetadata.Distribution))
        {
            var result = BuildResult(
                "Custom port detected",
                $"{maintenanceMetadata.Distribution} is a custom distribution. Official LevelUp aircraft packages are review-only for this target.",
                localVersion,
                manifest,
                AircraftUpdatePlanAction.LocalNewerThanIndex,
                "Review-only package information",
                isCustomDistribution: true,
                requiredPackages: [],
                findings);
            return new LevelUpAircraftUpdatePackageSelection(result, null, null, manifestPath);
        }

        if (VersionsEqual(localVersion, manifest.ReleaseVersion))
        {
            findings.Add("Installed LevelUp version matches the selected package target version.");
            var result = BuildResult(
                "Up to date",
                $"Installed LevelUp version {localVersion} already matches {manifest.ReleaseVersion}.",
                localVersion,
                manifest,
                AircraftUpdatePlanAction.UpToDate,
                "No action",
                isCustomDistribution: false,
                requiredPackages: [],
                findings);
            return new LevelUpAircraftUpdatePackageSelection(result, null, null, manifestPath);
        }

        if (manifest.PackageKind == AircraftUpdatePackageKind.CumulativePatch
            && !MatchesBaseline(localVersion, manifest))
        {
            var expected = string.Join(", ", new[] { manifest.BaselineVersion }.Concat(manifest.BaselineAliases).Where(value => !string.IsNullOrWhiteSpace(value)));
            findings.Add($"Cumulative patch baseline mismatch. Expected one of: {expected}; detected: {localVersion ?? "unknown"}.");
            var result = BuildResult(
                "Baseline mismatch",
                $"The selected cumulative LevelUp patch requires {manifest.BaselineVersion}; the installed version is {localVersion ?? "unknown"}.",
                localVersion,
                manifest,
                AircraftUpdatePlanAction.BaselineMismatch,
                "Select a matching full package or baseline",
                isCustomDistribution: false,
                requiredPackages: [],
                findings);
            return new LevelUpAircraftUpdatePackageSelection(result, null, null, manifestPath);
        }

        var action = manifest.PackageKind == AircraftUpdatePackageKind.FullBaseline
            ? AircraftUpdatePlanAction.InstallBaselineAndCumulativePatch
            : AircraftUpdatePlanAction.ApplyCumulativePatch;
        var actionDisplay = manifest.PackageKind == AircraftUpdatePackageKind.FullBaseline
            ? "Full: apply manifest-controlled LevelUp package"
            : "Incremental: apply cumulative LevelUp patch";
        var stateLabel = manifest.PackageKind == AircraftUpdatePackageKind.FullBaseline
            ? "Full update package loaded"
            : "Incremental update package loaded";
        var summary = manifest.PackageKind == AircraftUpdatePackageKind.FullBaseline
            ? $"Apply full LevelUp package {manifest.Archive.FileName} to reach {manifest.ReleaseVersion}."
            : $"Apply cumulative LevelUp patch {manifest.Archive.FileName} from {manifest.BaselineVersion} to {manifest.ReleaseVersion}.";
        findings.Add("The archive must pass manifest size, SHA-256, per-file hash, content-root and path checks before apply.");
        var updateCheck = BuildResult(
            stateLabel,
            summary,
            localVersion,
            manifest,
            action,
            actionDisplay,
            isCustomDistribution: false,
            requiredPackages: [package],
            findings);
        return new LevelUpAircraftUpdatePackageSelection(updateCheck, package, archivePath, manifestPath);
    }

    public LevelUpAircraftUpdatePackageSelection LoadFreshInstall(string selectedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedPath);

        var manifestPath = ResolveManifestPath(selectedPath);
        var manifest = AircraftUpdatePackageManifestParser.Load(manifestPath);
        if (manifest.PackageKind != AircraftUpdatePackageKind.FullBaseline)
        {
            throw new InvalidDataException("A fresh LevelUp installation requires an exact full-package manifest.");
        }

        var archivePath = Path.Combine(Path.GetDirectoryName(manifestPath)!, manifest.Archive.FileName);
        var versionNumber = manifest.ReleaseSequence is > 0 and <= int.MaxValue
            ? (int)manifest.ReleaseSequence.Value
            : 0;
        var package = new AircraftUpdatePackage(
            Family,
            AircraftUpdatePackageKind.FullBaseline,
            new AircraftUpstreamVersion(0, 0, versionNumber),
            manifest.Archive.FileName,
            SourceUrl: "",
            ReleaseVersion: manifest.ReleaseVersion,
            BaselineVersion: manifest.BaselineVersion,
            ExpectedSizeBytes: manifest.Archive.Size,
            ExpectedSha256: manifest.Archive.Sha256,
            Manifest: manifest);
        var findings = new List<string>
        {
            "Local LevelUp fresh-install package plan. No aircraft files are changed while the manifest is loaded.",
            $"Manifest: {manifestPath}",
            $"Archive: {archivePath}",
            "The archive must pass manifest size, SHA-256, per-file hash, content-root and path checks before installation."
        };
        var updateCheck = BuildResult(
            "Fresh-install package loaded",
            $"Install full LevelUp package {manifest.Archive.FileName} as {manifest.ReleaseVersion}.",
            "Not installed",
            manifest,
            AircraftUpdatePlanAction.InstallBaselineAndCumulativePatch,
            "Install exact full release package",
            isCustomDistribution: false,
            [package],
            findings);
        return new LevelUpAircraftUpdatePackageSelection(
            updateCheck,
            package,
            archivePath,
            manifestPath);
    }

    private static AircraftUpstreamUpdateCheckResult BuildResult(
        string stateLabel,
        string summary,
        string? localVersion,
        AircraftUpdatePackageManifest manifest,
        AircraftUpdatePlanAction action,
        string actionDisplay,
        bool isCustomDistribution,
        IReadOnlyList<AircraftUpdatePackage> requiredPackages,
        IReadOnlyList<string> findings) =>
        new(
            stateLabel,
            summary,
            Family,
            manifest.ManifestPath,
            localVersion ?? "-",
            manifest.ReleaseVersion,
            action,
            actionDisplay,
            isCustomDistribution,
            requiredPackages,
            findings);

    private static string ResolveManifestPath(string selectedPath)
    {
        var fullPath = Path.GetFullPath(selectedPath);
        if (fullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        var directory = Path.GetDirectoryName(fullPath) ?? "";
        var manifestPath = Path.Combine(directory, Path.GetFileNameWithoutExtension(fullPath) + ".manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("The selected LevelUp archive has no adjacent .manifest.json file.", manifestPath);
        }

        return manifestPath;
    }

    private static bool MatchesBaseline(string? localVersion, AircraftUpdatePackageManifest manifest) =>
        VersionsEqual(localVersion, manifest.BaselineVersion)
        || manifest.BaselineAliases.Any(alias => VersionsEqual(localVersion, alias));

    private static bool VersionsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(NormalizeVersion(left), NormalizeVersion(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVersion(string value) => value.Trim().TrimStart('v', 'V');

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
