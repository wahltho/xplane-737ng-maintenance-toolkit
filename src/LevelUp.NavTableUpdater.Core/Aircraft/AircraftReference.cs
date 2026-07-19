using LevelUp.NavTableUpdater.Core.Upstream;

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

public sealed record AircraftReferenceCgRange(
    string AircraftId,
    AircraftUpstreamVersion FromVersion,
    AircraftUpstreamVersion ToVersion,
    string SourceRef,
    double ReferenceCgYFeet,
    double ReferenceCgZFeet)
{
    public bool Contains(AircraftUpstreamVersion version) =>
        version >= FromVersion && version <= ToVersion;

    public string SourceVersion =>
        FromVersion == ToVersion ? FromVersion.ToString() : $"{FromVersion}-{ToVersion}";
}
