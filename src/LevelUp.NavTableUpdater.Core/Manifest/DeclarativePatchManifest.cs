namespace LevelUp.NavTableUpdater.Core.Manifest;

public sealed class DeclarativePatchManifest
{
    public int SchemaVersion { get; set; }

    public string PackageId { get; set; } = "";

    public string PackageVersion { get; set; } = "";

    public string RepositoryUrl { get; set; } = "";

    public string AircraftFamily { get; set; } = "";

    public List<string> SupportedProducts { get; set; } = [];

    public bool RestartRequired { get; set; }

    public List<string> SupportedUpstreamReleases { get; set; } = [];

    public List<DeclarativePatchPayload> Payloads { get; set; } = [];

    public List<DeclarativePatchTarget> Targets { get; set; } = [];
}

public sealed class DeclarativePatchPayload
{
    public string Path { get; set; } = "";

    public long Size { get; set; }

    public string Sha256 { get; set; } = "";
}

public sealed class DeclarativePatchTarget
{
    public string Operation { get; set; } = "";

    public string Payload { get; set; } = "";

    public string RelativePath { get; set; } = "";

    public List<string> SourceSha256 { get; set; } = [];

    public string? ResultSha256 { get; set; }
}
