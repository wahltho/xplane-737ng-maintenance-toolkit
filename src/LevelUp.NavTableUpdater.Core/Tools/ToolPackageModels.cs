namespace LevelUp.NavTableUpdater.Core.Tools;

public enum ToolReleaseChannel
{
    Stable,
    Beta
}

public enum ToolPackageInstallState
{
    TargetUnavailable,
    NotInstalled,
    InstalledVersionUnknown,
    Current,
    UpdateAvailable,
    SelectedReleaseOlder,
    RepairRequired
}

public sealed class ToolPackageManifest
{
    public int SchemaVersion { get; set; }

    public string PackageId { get; set; } = "";

    public string PackageVersion { get; set; } = "";

    public string ReleaseTag { get; set; } = "";

    public string Channel { get; set; } = "";

    public string Repository { get; set; } = "";

    public string InstallScope { get; set; } = "";

    public string TargetPath { get; set; } = "";

    public List<string> SupportedProducts { get; set; } = [];

    public bool RestartRequired { get; set; }

    public ToolPackageArchive Archive { get; set; } = new();

    public List<string> ProtectedPaths { get; set; } = [];

    public List<ToolPackageFile> Files { get; set; } = [];
}

public sealed class ToolPackageArchive
{
    public string FileName { get; set; } = "";

    public string RootPath { get; set; } = "";

    public long Size { get; set; }

    public string Sha256 { get; set; } = "";
}

public sealed class ToolPackageFile
{
    public string Path { get; set; } = "";

    public long Size { get; set; }

    public string Sha256 { get; set; } = "";
}

public sealed record ToolPackageRelease(
    ToolReleaseChannel Channel,
    string Tag,
    string ReleasePageUrl,
    string ManifestAssetName,
    string ManifestAssetUrl,
    long ManifestAssetSize,
    string ManifestAssetSha256,
    string ArchiveAssetUrl,
    ToolPackageManifest Manifest);

public sealed record ToolPackageProvisionResult(
    ToolPackageRelease Release,
    string PackageDirectory,
    bool Downloaded);

public sealed record ToolPackageInspection(
    ToolPackageInstallState State,
    string XPlaneRoot,
    string TargetPath,
    string InstalledVersion,
    string AvailableVersion,
    string Status,
    IReadOnlyList<string> Findings)
{
    public bool CanInstall => State is ToolPackageInstallState.NotInstalled;

    public bool CanUpdate => State is ToolPackageInstallState.UpdateAvailable
        or ToolPackageInstallState.InstalledVersionUnknown
        or ToolPackageInstallState.SelectedReleaseOlder;

    public bool CanRepair => State is ToolPackageInstallState.RepairRequired
        or ToolPackageInstallState.Current;
}
