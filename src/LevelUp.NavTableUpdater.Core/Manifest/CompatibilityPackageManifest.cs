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
}

public sealed class CompatibilityPackageModule
{
    public string ModuleId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Description { get; set; } = "";

    public CompatibilityModulePolicy Policy { get; set; }

    public bool DefaultEnabled { get; set; }

    public int InstallationOrder { get; set; }

    public List<string> Requires { get; set; } = [];

    public List<string> ConflictsWith { get; set; } = [];

    public List<DeclarativePatchPayload> Payloads { get; set; } = [];

    public List<DeclarativePatchTarget> Targets { get; set; } = [];
}
