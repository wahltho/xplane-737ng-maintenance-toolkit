namespace LevelUp.NavTableUpdater.Core.Resources;

public enum ResourceReleaseChannel
{
    Stable,
    Beta
}

public enum ResourcePackageState
{
    NotInstalled,
    Current,
    UpdateAvailable,
    Missing,
    VerificationFailed
}

public sealed class ResourcePackageManifest
{
    public int SchemaVersion { get; set; }

    public string PackageType { get; set; } = "";

    public string PackageId { get; set; } = "";

    public string PackageVersion { get; set; } = "";

    public string ReleaseTag { get; set; } = "";

    public string Channel { get; set; } = "";

    public string Repository { get; set; } = "";

    public List<string> SupportedProducts { get; set; } = [];

    public string DeliveryMode { get; set; } = "";

    public string ArchiveRoot { get; set; } = "";

    public string TargetDirectory { get; set; } = "";

    public long ExtractedSize { get; set; }

    public List<ResourcePackageFile> Files { get; set; } = [];

    public ResourcePackageArchive Archive { get; set; } = new();
}

public sealed class ResourcePackageFile
{
    public string Path { get; set; } = "";

    public long Size { get; set; }

    public string Sha256 { get; set; } = "";
}

public sealed class ResourcePackageArchive
{
    public string FileName { get; set; } = "";

    public long Size { get; set; }

    public string Sha256 { get; set; } = "";
}

public sealed record ResourcePackageRelease(
    ResourceReleaseChannel Channel,
    string Tag,
    string ReleasePageUrl,
    string ManifestAssetName,
    string ManifestAssetUrl,
    long ManifestAssetSize,
    string ManifestAssetSha256,
    string ArchiveAssetUrl,
    ResourcePackageManifest Manifest);

public sealed record ResourcePackageProvisionResult(
    ResourcePackageRelease Release,
    string ArchivePath,
    bool Downloaded,
    bool Temporary = false);

public sealed record ResourcePackageInspection(
    ResourcePackageState State,
    string InstalledVersion,
    string AvailableVersion,
    string DestinationDirectory,
    string InstalledPath,
    string Status)
{
    public bool CanInstall => State is ResourcePackageState.NotInstalled
        or ResourcePackageState.UpdateAvailable
        or ResourcePackageState.Missing
        or ResourcePackageState.VerificationFailed;
}

public sealed record ResourcePackageOperationResult(
    bool Succeeded,
    bool Changed,
    string Message,
    string InstalledPath);
