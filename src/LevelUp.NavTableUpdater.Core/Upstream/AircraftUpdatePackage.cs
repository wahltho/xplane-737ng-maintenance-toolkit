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
    string SourceUrl,
    string? ReleaseVersion = null,
    string? BaselineVersion = null,
    long? ExpectedSizeBytes = null,
    string? ExpectedSha256 = null,
    AircraftUpdatePackageManifest? Manifest = null)
{
    public AircraftUpstreamVersion Baseline => Version.Baseline;

    public string VersionDisplay => string.IsNullOrWhiteSpace(ReleaseVersion)
        ? Version.ToString()
        : ReleaseVersion;

    public string BaselineVersionDisplay => string.IsNullOrWhiteSpace(BaselineVersion)
        ? Version.ToBaselineString()
        : BaselineVersion;
}
