namespace LevelUp.NavTableUpdater.Core.Aircraft;

public sealed record AircraftViewAnalysisResult(
    string StateLabel,
    string Summary,
    bool IsXPlaneRunning,
    IReadOnlyList<AircraftVariantViewAnalysis> Variants,
    IReadOnlyList<string> Findings)
{
    public static AircraftViewAnalysisResult Empty() =>
        new(
            StateLabel: "No aircraft selected",
            Summary: "Select a Zibo or LevelUp aircraft folder to inspect CG, quick-view, and default-view state.",
            IsXPlaneRunning: false,
            Variants: [],
            Findings: []);
}

public sealed record AircraftVariantViewAnalysis(
    string AircraftId,
    string DisplayName,
    string Family,
    string AcfPath,
    string PrefsPath,
    string Source,
    string SourceRef,
    string SourceVersion,
    string? LocalVersion,
    string? AcfVersion,
    string? FileWriterVersion,
    double? CurrentCgYFeet,
    double? CurrentCgZFeet,
    double ReferenceCgYFeet,
    double ReferenceCgZFeet,
    double? DeltaYFeet,
    double? DeltaZFeet,
    double? DeltaYMeters,
    double? DeltaZMeters,
    string Status,
    string IdentityStatus,
    string QuickViewStatus,
    string DefaultViewStatus)
{
    public string CurrentCgDisplay => FormatPair(CurrentCgYFeet, CurrentCgZFeet, "ft");

    public string ReferenceCgDisplay => FormatPair(ReferenceCgYFeet, ReferenceCgZFeet, "ft");

    public string DeltaDisplay => DeltaYFeet.HasValue && DeltaZFeet.HasValue && DeltaYMeters.HasValue && DeltaZMeters.HasValue
        ? $"Y {DeltaYFeet.Value:+0.000000;-0.000000;0.000000} ft / {DeltaYMeters.Value:+0.000000;-0.000000;0.000000} m, Z {DeltaZFeet.Value:+0.000000;-0.000000;0.000000} ft / {DeltaZMeters.Value:+0.000000;-0.000000;0.000000} m"
        : "-";

    private static string FormatPair(double? y, double? z, string unit)
    {
        return y.HasValue && z.HasValue
            ? $"Y {y.Value:0.000000000}, Z {z.Value:0.000000000} {unit}"
            : "-";
    }
}
