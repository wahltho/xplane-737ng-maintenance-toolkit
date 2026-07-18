using System.Security.Cryptography;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Aircraft;

public enum QuickViewBaselineSource
{
    Unknown,
    StoredToolkitState,
    ReferenceCatalog,
    InferredCurrentDefaultView
}

public enum QuickViewBaselineConfidence
{
    Low,
    Medium,
    High
}

public sealed record QuickViewBaselineAssessment(
    QuickViewBaselineSource Source,
    QuickViewBaselineConfidence Confidence,
    string Status,
    string Recommendation,
    string Detail,
    double? BaselineYFeet,
    double? BaselineZFeet,
    double? DeltaYFeet,
    double? DeltaZFeet,
    bool CanAdapt,
    bool CanAdoptCurrent,
    bool RequiresExplicitAdoption)
{
    public bool HasCgDelta => DeltaYFeet.HasValue
        && DeltaZFeet.HasValue
        && (Math.Abs(DeltaYFeet.Value) > QuickViewBaselineAnalyzer.CgToleranceFeet
            || Math.Abs(DeltaZFeet.Value) > QuickViewBaselineAnalyzer.CgToleranceFeet);
}

public sealed class QuickViewBaselineAnalyzer
{
    internal const double CgToleranceFeet = 0.001;

    private const string ExpectedIdentityStatus = "Expected metadata";
    private const string DefaultViewMatchesQv0Status = "Default view matches QV0";
    private const string Qv0ReadableStatus = "QV0 readable";

    private readonly ToolStateStore _stateStore;

    public QuickViewBaselineAnalyzer(ToolStateStore stateStore)
    {
        _stateStore = stateStore;
    }

    public QuickViewBaselineAssessment Assess(AircraftVariantViewAnalysis? variant)
    {
        if (variant is null)
        {
            return Unknown(
                "No aircraft variant selected.",
                "Select a supported variant before adapting Quick Views.",
                canAdoptCurrent: false);
        }

        if (variant.CurrentCgYFeet is null || variant.CurrentCgZFeet is null)
        {
            return Unknown(
                "ACF CG is incomplete.",
                "Review the ACF before adapting Quick Views.",
                canAdoptCurrent: false);
        }

        var target = _stateStore.TryGetTarget(variant);
        if (target?.LastQuickViewCgYFeet is not null && target.LastQuickViewCgZFeet is not null)
        {
            return FromStoredBaseline(variant, target);
        }

        var referenceDeltaY = variant.CurrentCgYFeet.Value - variant.ReferenceCgYFeet;
        var referenceDeltaZ = variant.CurrentCgZFeet.Value - variant.ReferenceCgZFeet;
        if (!HasDelta(referenceDeltaY, referenceDeltaZ))
        {
            return new QuickViewBaselineAssessment(
                QuickViewBaselineSource.ReferenceCatalog,
                QuickViewBaselineConfidence.High,
                "Reference baseline",
                "No Quick View adaptation is required. The current ACF CG matches the reference baseline.",
                "The variant matches the catalog CG baseline within tolerance. Recording this baseline is safe.",
                variant.ReferenceCgYFeet,
                variant.ReferenceCgZFeet,
                referenceDeltaY,
                referenceDeltaZ,
                CanAdapt: false,
                CanAdoptCurrent: true,
                RequiresExplicitAdoption: false);
        }

        if (!string.Equals(variant.IdentityStatus, ExpectedIdentityStatus, StringComparison.Ordinal))
        {
            return Unknown(
                "Current CG differs from the reference baseline, but aircraft metadata is not the expected reference identity.",
                "Adopt the current baseline only if you have verified that the Quick Views already fit this aircraft.",
                canAdoptCurrent: IsQuickViewReadable(variant),
                baselineYFeet: variant.ReferenceCgYFeet,
                baselineZFeet: variant.ReferenceCgZFeet,
                deltaYFeet: referenceDeltaY,
                deltaZFeet: referenceDeltaZ);
        }

        if (string.Equals(variant.DefaultViewStatus, DefaultViewMatchesQv0Status, StringComparison.Ordinal))
        {
            return new QuickViewBaselineAssessment(
                QuickViewBaselineSource.InferredCurrentDefaultView,
                QuickViewBaselineConfidence.Medium,
                "Current baseline inferred",
                "Default View already matches Quick View 0 at the current CG. Adopt the current baseline instead of adapting automatically.",
                "The current CG differs from the reference baseline, but Quick View 0 and the ACF default view are already internally consistent.",
                variant.CurrentCgYFeet,
                variant.CurrentCgZFeet,
                0,
                0,
                CanAdapt: false,
                CanAdoptCurrent: true,
                RequiresExplicitAdoption: true);
        }

        if (!IsQuickViewReadable(variant))
        {
            return Unknown(
                "Current CG differs from the reference baseline, but Quick View 0 is not readable.",
                "Review or restore the quick-view preferences before adapting from the reference baseline.",
                canAdoptCurrent: false,
                baselineYFeet: variant.ReferenceCgYFeet,
                baselineZFeet: variant.ReferenceCgZFeet,
                deltaYFeet: referenceDeltaY,
                deltaZFeet: referenceDeltaZ);
        }

        return new QuickViewBaselineAssessment(
            QuickViewBaselineSource.ReferenceCatalog,
            QuickViewBaselineConfidence.Medium,
            "Reference baseline assumed",
            "Adapt from the reference baseline only if the current Quick Views have not already been adjusted for this CG.",
            "No stored toolkit baseline exists. Aircraft identity matches the reference catalog and Default View does not match Quick View 0 at the current CG.",
            variant.ReferenceCgYFeet,
            variant.ReferenceCgZFeet,
            referenceDeltaY,
            referenceDeltaZ,
            CanAdapt: true,
            CanAdoptCurrent: true,
            RequiresExplicitAdoption: false);
    }

    private static QuickViewBaselineAssessment FromStoredBaseline(
        AircraftVariantViewAnalysis variant,
        AircraftToolState target)
    {
        var baselineY = target.LastQuickViewCgYFeet!.Value;
        var baselineZ = target.LastQuickViewCgZFeet!.Value;
        var deltaY = variant.CurrentCgYFeet!.Value - baselineY;
        var deltaZ = variant.CurrentCgZFeet!.Value - baselineZ;
        var prefsHash = QuickViewBaselineFiles.ComputeSha256IfExists(variant.PrefsPath);
        var xCameraPath = QuickViewBaselineFiles.GetXCameraPath(variant);
        var xCameraHash = QuickViewBaselineFiles.ComputeSha256IfExists(xCameraPath);
        var storedPrefsHashMissing = string.IsNullOrWhiteSpace(target.LastQuickViewPrefsSha256);
        var storedXCameraHashMissing = File.Exists(xCameraPath) && string.IsNullOrWhiteSpace(target.LastQuickViewXCameraSha256);
        var prefsChanged = !string.IsNullOrWhiteSpace(target.LastQuickViewPrefsSha256)
            && !string.Equals(target.LastQuickViewPrefsSha256, prefsHash, StringComparison.OrdinalIgnoreCase);
        var xCameraChanged = !string.IsNullOrWhiteSpace(target.LastQuickViewXCameraSha256)
            && !string.Equals(target.LastQuickViewXCameraSha256, xCameraHash, StringComparison.OrdinalIgnoreCase);
        var storedHashesIncomplete = storedPrefsHashMissing || storedXCameraHashMissing;
        var relatedFilesChanged = prefsChanged || xCameraChanged;
        var filesTrustworthy = !storedHashesIncomplete && !relatedFilesChanged;
        var confidence = filesTrustworthy
            ? QuickViewBaselineConfidence.High
            : QuickViewBaselineConfidence.Medium;
        var detail = (storedHashesIncomplete, relatedFilesChanged) switch
        {
            (true, _) => "A stored toolkit CG baseline exists, but older state data has no quick-view file hashes.",
            (_, true) => "A stored toolkit CG baseline exists, but quick-view related files changed since the baseline was recorded.",
            _ => "A stored toolkit CG baseline exists for this aircraft variant."
        };
        var hasDelta = HasDelta(deltaY, deltaZ);
        var canAdapt = filesTrustworthy && hasDelta && IsQuickViewReadable(variant);
        var recommendation = (hasDelta, filesTrustworthy) switch
        {
            (false, _) => "No Quick View adaptation is required. The stored baseline already matches the current CG.",
            (true, true) => "Adapt from the stored toolkit baseline.",
            _ => "Adopt the current baseline only if you have verified that the Quick Views already fit this aircraft, or restore the last known backup before adapting."
        };

        return new QuickViewBaselineAssessment(
            QuickViewBaselineSource.StoredToolkitState,
            confidence,
            "Stored toolkit baseline",
            recommendation,
            detail,
            baselineY,
            baselineZ,
            deltaY,
            deltaZ,
            CanAdapt: canAdapt,
            CanAdoptCurrent: true,
            RequiresExplicitAdoption: false);
    }

    private static QuickViewBaselineAssessment Unknown(
        string detail,
        string recommendation,
        bool canAdoptCurrent,
        double? baselineYFeet = null,
        double? baselineZFeet = null,
        double? deltaYFeet = null,
        double? deltaZFeet = null) =>
        new(
            QuickViewBaselineSource.Unknown,
            QuickViewBaselineConfidence.Low,
            "Unknown baseline",
            recommendation,
            detail,
            baselineYFeet,
            baselineZFeet,
            deltaYFeet,
            deltaZFeet,
            CanAdapt: false,
            canAdoptCurrent,
            RequiresExplicitAdoption: canAdoptCurrent);

    private static bool IsQuickViewReadable(AircraftVariantViewAnalysis variant) =>
        string.Equals(variant.QuickViewStatus, Qv0ReadableStatus, StringComparison.Ordinal);

    private static bool HasDelta(double deltaYFeet, double deltaZFeet) =>
        Math.Abs(deltaYFeet) > CgToleranceFeet || Math.Abs(deltaZFeet) > CgToleranceFeet;
}

internal static class QuickViewBaselineFiles
{
    public static string GetXCameraPath(AircraftVariantViewAnalysis variant)
    {
        var aircraftFolder = Path.GetDirectoryName(variant.AcfPath) ?? "";
        var acfStem = Path.GetFileNameWithoutExtension(variant.AcfPath);
        return Path.Combine(aircraftFolder, $"X-Camera_{acfStem}.csv");
    }

    public static string? ComputeSha256IfExists(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
