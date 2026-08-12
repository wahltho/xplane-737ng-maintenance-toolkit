namespace LevelUp.NavTableUpdater.Core.State;

public sealed class ToolStateDocument
{
    public int SchemaVersion { get; set; } = 5;

    public Dictionary<string, AircraftToolState> Aircraft { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, ContentInstallationToolState> ContentInstallations { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, ToolInstallationState> ToolInstallations { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, ResourceInstallationState> ResourceInstallations { get; set; } = new(StringComparer.Ordinal);
}

public sealed class ResourceInstallationState
{
    public string PackageId { get; set; } = "";

    public string PackageVersion { get; set; } = "";

    public string ReleaseTag { get; set; } = "";

    public string Channel { get; set; } = "stable";

    public string DestinationDirectory { get; set; } = "";

    public string TargetPath { get; set; } = "";

    public List<ResourceInstalledFileState> InstalledFiles { get; set; } = [];

    public DateTimeOffset LastOperationUtc { get; set; }
}

public sealed class ResourceInstalledFileState
{
    public string RelativePath { get; set; } = "";

    public long Size { get; set; }

    public string Sha256 { get; set; } = "";
}

public sealed class ToolInstallationState
{
    public string XPlaneRoot { get; set; } = "";

    public string PackageId { get; set; } = "";

    public string TargetPath { get; set; } = "";

    public string InstalledVersion { get; set; } = "";

    public string Channel { get; set; } = "stable";

    public DateTimeOffset LastOperationUtc { get; set; }

    public string LastOperation { get; set; } = "";

    public List<ToolInstalledFileState> InstalledFiles { get; set; } = [];

    public List<string> ProtectedPaths { get; set; } = [];

    public List<ToolBackupGenerationState> Backups { get; set; } = [];
}

public sealed class ToolInstalledFileState
{
    public string RelativePath { get; set; } = "";

    public long Size { get; set; }

    public string Sha256 { get; set; } = "";

    public bool Protected { get; set; }
}

public sealed class ToolBackupGenerationState
{
    public string BackupId { get; set; } = "";

    public string BackupPath { get; set; } = "";

    public DateTimeOffset CreatedUtc { get; set; }

    public bool SourceExisted { get; set; }

    public string PreviousVersion { get; set; } = "";

    public string PreviousChannel { get; set; } = "stable";

    public string InstalledVersion { get; set; } = "";

    public List<ToolInstalledFileState> InstalledFiles { get; set; } = [];

    public List<ToolOverlayBackupFileState> OverlayFiles { get; set; } = [];
}

public sealed class ToolOverlayBackupFileState
{
    public string RelativePath { get; set; } = "";

    public bool OriginalExisted { get; set; }

    public long? OriginalSize { get; set; }

    public string? OriginalSha256 { get; set; }
}

public sealed class ContentInstallationToolState
{
    public string AircraftFolder { get; set; } = "";

    public Dictionary<string, ContentComponentState> ContentComponents { get; set; } = new(StringComparer.Ordinal);

    public List<BackupRecord> Backups { get; set; } = [];
}

public sealed class AircraftToolState
{
    public string AircraftId { get; set; } = "";

    public string AircraftFolder { get; set; } = "";

    public string AcfPath { get; set; } = "";

    public string PrefsPath { get; set; } = "";

    public double? LastObservedCgYFeet { get; set; }

    public double? LastObservedCgZFeet { get; set; }

    public double? LastQuickViewCgYFeet { get; set; }

    public double? LastQuickViewCgZFeet { get; set; }

    public string? LastQuickViewBaselineSource { get; set; }

    public string? LastQuickViewPrefsSha256 { get; set; }

    public string? LastQuickViewXCameraSha256 { get; set; }

    public DateTimeOffset? LastQuickViewAppliedUtc { get; set; }

    public double? LastDefaultViewCgYFeet { get; set; }

    public double? LastDefaultViewCgZFeet { get; set; }

    public DateTimeOffset? LastDefaultViewAppliedUtc { get; set; }

    public DateTimeOffset? LastRestoreUtc { get; set; }

    public string? InstalledContentPackageId { get; set; }

    public string? InstalledContentPackageVersion { get; set; }

    public DateTimeOffset? LastContentOperationUtc { get; set; }

    public Dictionary<string, ContentComponentState> ContentComponents { get; set; } = new(StringComparer.Ordinal);

    public string? InstalledAircraftUpdateFamily { get; set; }

    public string? InstalledAircraftUpdateVersion { get; set; }

    public string? LastAircraftUpdateMode { get; set; }

    public DateTimeOffset? LastAircraftUpdateUtc { get; set; }

    public List<string> LastAircraftUpdatePackages { get; set; } = [];

    public string? LastOperation { get; set; }

    public List<BackupRecord> Backups { get; set; } = [];
}

public sealed class ContentComponentState
{
    public string ComponentId { get; set; } = "";

    public string PackageVersion { get; set; } = "";

    public DateTimeOffset InstalledUtc { get; set; }

    public DateTimeOffset LastOperationUtc { get; set; }

    public string LastOperation { get; set; } = "";

    public List<ContentComponentFileState> Files { get; set; } = [];
}

public sealed class ContentComponentFileState
{
    public string RelativePath { get; set; } = "";

    public string TargetPath { get; set; } = "";

    public string BackupPath { get; set; } = "";

    public bool OriginalExisted { get; set; }

    public long? OriginalSizeBytes { get; set; }

    public string? OriginalSha256 { get; set; }

    public long? InstalledSizeBytes { get; set; }

    public string? InstalledSha256 { get; set; }
}

public sealed class BackupRecord
{
    public string Operation { get; set; } = "";

    public string SourcePath { get; set; } = "";

    public string BackupPath { get; set; } = "";

    public DateTimeOffset CreatedUtc { get; set; }

    public double? CgYFeet { get; set; }

    public double? CgZFeet { get; set; }

    public string? PackageId { get; set; }

    public string? PackageVersion { get; set; }

    public string? PackageFileName { get; set; }

    public bool SourceExisted { get; set; } = true;

    public long? SourceSizeBytes { get; set; }

    public string? SourceSha256 { get; set; }

    public long? WrittenSizeBytes { get; set; }

    public string? WrittenSha256 { get; set; }
}
