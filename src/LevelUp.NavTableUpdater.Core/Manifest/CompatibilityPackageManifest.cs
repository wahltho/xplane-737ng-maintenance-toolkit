namespace LevelUp.NavTableUpdater.Core.Manifest;

public enum CompatibilityModulePolicy
{
    Required,
    Recommended,
    Optional
}

public sealed class CompatibilityPackageManifest
{
    public int SchemaVersion { get; set; }

    public string PackageType { get; set; } = "";

    public string PackageId { get; set; } = "";

    public string PackageVersion { get; set; } = "";

    public string RepositoryUrl { get; set; } = "";

    public string AircraftFamily { get; set; } = "";

    public List<string> SupportedProducts { get; set; } = [];

    public bool RestartRequired { get; set; }

    public List<string> SupportedUpstreamReleases { get; set; } = [];

    public List<CompatibilityPackageModule> Modules { get; set; } = [];

    public List<ResolvedCatalogSource> Sources { get; set; } = [];

    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayVersion => Sources.Count == 0 ? PackageVersion
        : string.Join(", ", Sources.Select(source => $"{source.ModuleId} {source.ReleaseTag}"));
}

public sealed class CompatibilityPackageModule
{
    [System.Text.Json.Serialization.JsonIgnore]
    public int SourceSchemaVersion { get; set; }

    public string ModuleId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Description { get; set; } = "";

    public CompatibilityModulePolicy Policy { get; set; }

    public bool DefaultEnabled { get; set; }

    public int InstallationOrder { get; set; }

    public List<string> SupportedUpstreamReleases { get; set; } = [];

    public List<string> Requires { get; set; } = [];

    public List<string> ConflictsWith { get; set; } = [];

    public List<DeclarativePatchPayload> Payloads { get; set; } = [];

    public List<DeclarativePatchTarget> Targets { get; set; } = [];

    public List<CompatibilityRetiredFile> RetiredFiles { get; set; } = [];

    public List<CompatibilityManagedScope> ManagedScopes { get; set; } = [];
}

public sealed class CompatibilityRetiredFile
{
    public string RelativePath { get; set; } = "";
    public List<string> SourceSha256 { get; set; } = [];
}

public sealed class CompatibilityManagedScope
{
    public string RelativePath { get; set; } = "";
    public string Mode { get; set; } = "";
}

public sealed class ResolvedCatalogSource
{
    public string PackageId { get; set; } = "";
    public string ModuleId { get; set; } = "";
    public string ReleaseTag { get; set; } = "";
    public string AssetSha256 { get; set; } = "";
    public string RepositoryUrl { get; set; } = "";
}
