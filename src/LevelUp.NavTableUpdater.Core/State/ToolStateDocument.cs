namespace LevelUp.NavTableUpdater.Core.State;

public sealed class ToolStateDocument
{
    public int SchemaVersion { get; set; } = 1;

    public Dictionary<string, AircraftToolState> Aircraft { get; set; } = new(StringComparer.Ordinal);
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

    public DateTimeOffset? LastQuickViewAppliedUtc { get; set; }

    public double? LastDefaultViewCgYFeet { get; set; }

    public double? LastDefaultViewCgZFeet { get; set; }

    public DateTimeOffset? LastDefaultViewAppliedUtc { get; set; }

    public DateTimeOffset? LastRestoreUtc { get; set; }

    public string? InstalledContentPackageId { get; set; }

    public string? InstalledContentPackageVersion { get; set; }

    public DateTimeOffset? LastContentOperationUtc { get; set; }

    public string? LastOperation { get; set; }

    public List<BackupRecord> Backups { get; set; } = [];
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
}
