namespace LevelUp.NavTableUpdater.Core.Upstream;

public enum AircraftUpdatePackageKind
{
    Unknown = 0,
    FullBaseline,
    CumulativePatch
}

public sealed record AircraftUpdatePackage(
    string Family,
    AircraftUpdatePackageKind Kind,
    AircraftUpstreamVersion Version,
    string FileName,
    string SourceUrl)
{
    public AircraftUpstreamVersion Baseline => Version.Baseline;
}
