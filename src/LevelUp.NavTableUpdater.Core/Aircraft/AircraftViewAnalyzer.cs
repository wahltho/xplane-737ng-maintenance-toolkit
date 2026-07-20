using LevelUp.NavTableUpdater.Core.Platform;

namespace LevelUp.NavTableUpdater.Core.Aircraft;

public sealed class AircraftViewAnalyzer
{
    private const double FeetToMeters = 0.3048;
    private const double CgToleranceFeet = 0.001;
    private const double ViewToleranceFeet = 0.005;
    private const double PitchToleranceDegrees = 0.001;

    public AircraftViewAnalysisResult Analyze(string aircraftFolder)
    {
        if (string.IsNullOrWhiteSpace(aircraftFolder))
        {
            return AircraftViewAnalysisResult.Empty();
        }

        var fullPath = Path.GetFullPath(aircraftFolder);
        if (!Directory.Exists(fullPath))
        {
            return new AircraftViewAnalysisResult(
                StateLabel: "Aircraft folder missing",
                Summary: "The selected aircraft folder does not exist.",
                IsXPlaneRunning: XPlaneProcessDetector.IsXPlaneRunning(),
                Variants: [],
                Findings: [$"Missing folder: {fullPath}"]);
        }

        var variants = new List<AircraftVariantViewAnalysis>();
        var findings = new List<string>();

        foreach (var reference in AircraftReferenceCatalog.All)
        {
            var acfPath = Path.Combine(fullPath, reference.AcfFileName);
            if (!File.Exists(acfPath))
            {
                continue;
            }

            variants.Add(AnalyzeVariant(fullPath, reference, acfPath, findings));
        }

        var isXPlaneRunning = XPlaneProcessDetector.IsXPlaneRunning();
        if (isXPlaneRunning)
        {
            findings.Add("X-Plane appears to be running. View utilities should stay read-only until X-Plane is closed.");
        }

        if (variants.Count == 0)
        {
            return new AircraftViewAnalysisResult(
                StateLabel: "No supported 737NG variant",
                Summary: "No known Zibo or LevelUp ACF file was found in the selected folder.",
                IsXPlaneRunning: isXPlaneRunning,
                Variants: [],
                Findings: findings);
        }

        var deltaCount = variants.Count(v => v.DeltaYFeet.HasValue
            && v.DeltaZFeet.HasValue
            && (Math.Abs(v.DeltaYFeet.Value) > CgToleranceFeet || Math.Abs(v.DeltaZFeet.Value) > CgToleranceFeet));
        var status = deltaCount > 0 ? "CG delta detected" : "Reference CG";
        var summary = deltaCount > 0
            ? $"{deltaCount} of {variants.Count} recognized variant(s) differ from the reference CG baseline."
            : $"{variants.Count} recognized variant(s) match the reference CG baseline within tolerance.";

        return new AircraftViewAnalysisResult(status, summary, isXPlaneRunning, variants, findings);
    }

    private static AircraftVariantViewAnalysis AnalyzeVariant(
        string aircraftFolder,
        AircraftReference reference,
        string acfPath,
        ICollection<string> findings)
    {
        AcfMetadata metadata;
        try
        {
            metadata = AircraftFileParser.ReadAcfMetadata(acfPath);
        }
        catch (IOException ex)
        {
            findings.Add($"{reference.DisplayName}: failed to read ACF ({ex.Message}).");
            return BuildUnreadableVariant(reference, acfPath, "ACF read failed");
        }
        catch (UnauthorizedAccessException ex)
        {
            findings.Add($"{reference.DisplayName}: failed to read ACF ({ex.Message}).");
            return BuildUnreadableVariant(reference, acfPath, "ACF read failed");
        }

        var versionTxt = AircraftFileParser.ReadVersionTxt(aircraftFolder);
        var maintenanceMetadata = AircraftFileParser.ReadMaintenanceMetadata(aircraftFolder, out var metadataError);
        if (metadataError is not null)
        {
            findings.Add($"{reference.DisplayName}: {metadataError}");
        }

        var localVersion = ResolveLocalVersion(reference, maintenanceMetadata, versionTxt, findings);
        var effectiveReference = AircraftReferenceCatalog.ResolveForKnownCg(reference, localVersion, metadata.AcfVersion, maintenanceMetadata);
        if (!string.Equals(effectiveReference.SourceVersion, reference.SourceVersion, StringComparison.Ordinal)
            || Math.Abs(effectiveReference.ReferenceCgYFeet - reference.ReferenceCgYFeet) > CgToleranceFeet
            || Math.Abs(effectiveReference.ReferenceCgZFeet - reference.ReferenceCgZFeet) > CgToleranceFeet)
        {
            findings.Add($"{reference.DisplayName}: reference CG selected from {effectiveReference.SourceVersion} catalog baseline.");
        }

        var identityStatus = BuildIdentityStatus(effectiveReference, metadata, versionTxt, maintenanceMetadata);
        var prefsPath = Path.Combine(aircraftFolder, effectiveReference.PrefsFileName);
        var quickViewStatus = "Prefs missing";
        var defaultViewStatus = metadata.DefaultView is null ? "Default view incomplete" : "QV0 missing";

        QuickView0? quickView0 = null;
        if (File.Exists(prefsPath))
        {
            quickView0 = AircraftFileParser.ReadQuickView0(prefsPath);
            quickViewStatus = quickView0 is null ? "QV0 incomplete" : "QV0 readable";
        }

        var deltaYFeet = metadata.Cg?.YFeet - effectiveReference.ReferenceCgYFeet;
        var deltaZFeet = metadata.Cg?.ZFeet - effectiveReference.ReferenceCgZFeet;
        var deltaYMeters = deltaYFeet * FeetToMeters;
        var deltaZMeters = deltaZFeet * FeetToMeters;

        var hasCgDelta = deltaYFeet.HasValue && deltaZFeet.HasValue
            && (Math.Abs(deltaYFeet.Value) > CgToleranceFeet || Math.Abs(deltaZFeet.Value) > CgToleranceFeet);
        var status = metadata.Cg is null
            ? "CG missing"
            : hasCgDelta
                ? "CG delta detected"
                : "Reference CG";

        if (metadata.Cg is not null && metadata.DefaultView is not null && quickView0 is not null)
        {
            var expectedDefaultView = AircraftFileParser.CalculateDefaultViewFromQuickView(metadata.Cg, quickView0);
            defaultViewStatus = DefaultViewMatches(metadata.DefaultView, expectedDefaultView)
                ? "Default view matches QV0"
                : "Default view differs from QV0";
        }

        if (identityStatus != "Expected metadata")
        {
            findings.Add($"{effectiveReference.DisplayName}: {identityStatus}.");
        }

        if (hasCgDelta)
        {
            findings.Add($"{effectiveReference.DisplayName}: CG differs from reference baseline by Y {deltaYFeet!.Value:+0.000000;-0.000000;0.000000} ft, Z {deltaZFeet!.Value:+0.000000;-0.000000;0.000000} ft.");
        }

        return new AircraftVariantViewAnalysis(
            effectiveReference.AircraftId,
            effectiveReference.DisplayName,
            effectiveReference.Family,
            acfPath,
            prefsPath,
            effectiveReference.Source,
            effectiveReference.SourceRef,
            effectiveReference.SourceVersion,
            localVersion,
            metadata.AcfVersion,
            metadata.FileWriterVersion,
            metadata.Cg?.YFeet,
            metadata.Cg?.ZFeet,
            effectiveReference.ReferenceCgYFeet,
            effectiveReference.ReferenceCgZFeet,
            deltaYFeet,
            deltaZFeet,
            deltaYMeters,
            deltaZMeters,
            status,
            identityStatus,
            quickViewStatus,
            defaultViewStatus);
    }

    private static string? ResolveLocalVersion(
        AircraftReference reference,
        AircraftMaintenanceMetadata? maintenanceMetadata,
        string? versionTxt,
        ICollection<string> findings)
    {
        if (maintenanceMetadata is null)
        {
            return versionTxt;
        }

        if (!string.IsNullOrWhiteSpace(maintenanceMetadata.AircraftFamily)
            && !string.Equals(maintenanceMetadata.AircraftFamily, reference.Family, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(
                $"{reference.DisplayName}: {AircraftMaintenanceMetadata.FileName} aircraftFamily '{maintenanceMetadata.AircraftFamily}' does not match detected family '{reference.Family}'.");
        }

        if (string.IsNullOrWhiteSpace(maintenanceMetadata.DistributionVersion))
        {
            findings.Add($"{reference.DisplayName}: {AircraftMaintenanceMetadata.FileName} has no distributionVersion; falling back to version.txt.");
            return versionTxt;
        }

        var distribution = string.IsNullOrWhiteSpace(maintenanceMetadata.Distribution)
            ? "custom distribution"
            : maintenanceMetadata.Distribution;
        var runtime = string.IsNullOrWhiteSpace(maintenanceMetadata.Runtime)
            ? ""
            : $" ({maintenanceMetadata.Runtime})";
        findings.Add($"{reference.DisplayName}: local version {maintenanceMetadata.DistributionVersion} read from {AircraftMaintenanceMetadata.FileName} for {distribution}{runtime}.");
        return maintenanceMetadata.DistributionVersion;
    }

    private static AircraftVariantViewAnalysis BuildUnreadableVariant(AircraftReference reference, string acfPath, string status)
    {
        var prefsPath = Path.Combine(Path.GetDirectoryName(acfPath) ?? "", reference.PrefsFileName);
        return new AircraftVariantViewAnalysis(
            reference.AircraftId,
            reference.DisplayName,
            reference.Family,
            acfPath,
            prefsPath,
            reference.Source,
            reference.SourceRef,
            reference.SourceVersion,
            LocalVersion: null,
            AcfVersion: null,
            FileWriterVersion: null,
            CurrentCgYFeet: null,
            CurrentCgZFeet: null,
            reference.ReferenceCgYFeet,
            reference.ReferenceCgZFeet,
            DeltaYFeet: null,
            DeltaZFeet: null,
            DeltaYMeters: null,
            DeltaZMeters: null,
            status,
            IdentityStatus: "Unreadable",
            QuickViewStatus: "Not checked",
            DefaultViewStatus: "Not checked");
    }

    private static string BuildIdentityStatus(
        AircraftReference reference,
        AcfMetadata metadata,
        string? versionTxt,
        AircraftMaintenanceMetadata? maintenanceMetadata)
    {
        var issues = new List<string>();

        if (!StringEquals(metadata.Name, reference.ExpectedName))
        {
            issues.Add("name differs");
        }

        if (!StringEquals(metadata.Description, reference.ExpectedDescription))
        {
            issues.Add("description differs");
        }

        if (metadata.Studio?.Contains(reference.ExpectedStudioContains, StringComparison.OrdinalIgnoreCase) != true)
        {
            issues.Add("studio differs");
        }

        if (maintenanceMetadata is null && reference.ExpectedVersionTxt is not null && !StringEquals(versionTxt, reference.ExpectedVersionTxt))
        {
            issues.Add("version.txt differs");
        }

        if (reference.ExpectedAcfVersion is not null && !StringEquals(metadata.AcfVersion, reference.ExpectedAcfVersion))
        {
            issues.Add("acf version differs");
        }

        if (reference.ExpectedFileWriterVersion is not null && !StringEquals(metadata.FileWriterVersion, reference.ExpectedFileWriterVersion))
        {
            issues.Add("writer version differs");
        }

        if (maintenanceMetadata is not null)
        {
            var distribution = string.IsNullOrWhiteSpace(maintenanceMetadata.Distribution)
                ? "custom distribution"
                : maintenanceMetadata.Distribution;
            var version = string.IsNullOrWhiteSpace(maintenanceMetadata.DistributionVersion)
                ? ""
                : $" {maintenanceMetadata.DistributionVersion}";

            return issues.Count == 0
                ? $"Custom distribution ({distribution}{version})"
                : $"Custom distribution ({distribution}{version}; metadata differs: {string.Join(", ", issues)})";
        }

        return issues.Count == 0 ? "Expected metadata" : $"Metadata differs ({string.Join(", ", issues)})";
    }

    private static bool DefaultViewMatches(DefaultView actual, DefaultView expected)
    {
        return Math.Abs(actual.XFeet - expected.XFeet) <= ViewToleranceFeet
            && Math.Abs(actual.YFeet - expected.YFeet) <= ViewToleranceFeet
            && Math.Abs(actual.ZFeet - expected.ZFeet) <= ViewToleranceFeet
            && Math.Abs(actual.PitchDegrees - expected.PitchDegrees) <= PitchToleranceDegrees;
    }

    private static bool StringEquals(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}
