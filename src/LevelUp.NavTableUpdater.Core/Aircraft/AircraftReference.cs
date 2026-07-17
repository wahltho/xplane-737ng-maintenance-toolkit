namespace LevelUp.NavTableUpdater.Core.Aircraft;

public sealed record AircraftReference(
    string AircraftId,
    string DisplayName,
    string Family,
    string AcfFileName,
    string PrefsFileName,
    string Source,
    string SourceRef,
    string SourceVersion,
    string ExpectedName,
    string ExpectedDescription,
    string ExpectedStudioContains,
    string? ExpectedVersionTxt,
    string? ExpectedAcfVersion,
    string? ExpectedFileWriterVersion,
    double ReferenceCgYFeet,
    double ReferenceCgZFeet);
