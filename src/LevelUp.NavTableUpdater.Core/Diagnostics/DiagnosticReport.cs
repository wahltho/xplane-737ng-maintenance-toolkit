namespace LevelUp.NavTableUpdater.Core.Diagnostics;

public sealed record DiagnosticExportContext
{
    public string ToolkitVersion { get; init; } = "";
    public string CatalogVersion { get; init; } = "";
    public string CatalogStatus { get; init; } = "";
    public string AircraftFolder { get; init; } = "";
    public string SelectedAircraftPath { get; init; } = "";
    public string Product { get; init; } = "";
    public string ProductFamily { get; init; } = "";
    public string InstalledAircraftVersion { get; init; } = "";
    public string AvailableAircraftVersion { get; init; } = "";
    public string XPlaneRoot { get; init; } = "";
    public string PreviousAircraftFolder { get; init; } = "";
    public string InstallLog { get; init; } = "";
    public string OperationLog { get; init; } = "";
    public IReadOnlyDictionary<string, string> Status { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> KeyFiles { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<DiagnosticPackageSummary> Packages { get; init; } = [];
    public IReadOnlyList<DiagnosticModuleSelection> ModuleSelections { get; init; } = [];
    public IReadOnlyList<string> Findings { get; init; } = [];
}

public sealed record DiagnosticPackageSummary(string PackageId, string InstalledVersion,
    string AvailableVersion, string Status);
public sealed record DiagnosticModuleSelection(string ModuleId, string Policy, bool Selected);

public sealed class DiagnosticReport
{
    public int SchemaVersion { get; init; } = 1;
    public DateTimeOffset ExportedUtc { get; init; } = DateTimeOffset.UtcNow;
    public bool PathsAnonymized { get; init; }
    public string OperatingSystem { get; init; } = "";
    public string Runtime { get; init; } = "";
    public string ProcessArchitecture { get; init; } = "";
    public DiagnosticExportContext Context { get; init; } = new();
    public string StatePath { get; init; } = "";
    public string BackupRoot { get; init; } = "";
    public string StateStatus { get; set; } = "";
    public int? StateSchemaVersion { get; set; }
    public List<DiagnosticAircraftRecord> RecordedAircraft { get; } = [];
    public List<DiagnosticComponentSummary> Components { get; } = [];
    public List<DiagnosticFileCheck> Files { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<string> Limitations { get; } =
    [
        "This is a read-only snapshot, not a simulator test or an installation/restore approval.",
        "The package statuses are the app's last displayed results; no online refresh is performed.",
        "A hash difference from a recorded component snapshot can also be caused by a later patch on the same file.",
        "Directory backups are checked for presence only; their complete contents and restore chain are not verified.",
        "No aircraft, backup, settings or raw state files are included. Review logs before sharing; they may contain details you entered."
    ];
}

public sealed record DiagnosticAircraftRecord(string Folder, bool DirectoryExists,
    string InstalledUpdateVersion, IReadOnlyList<string> ComponentIds);
public sealed record DiagnosticComponentSummary(string Kind, string PackageId, string Version,
    string Root, IReadOnlyList<string> EnabledModules, IReadOnlyList<string> Sources,
    bool RestoreRecorded);

public sealed record DiagnosticFileCheck(string Kind, string Owner, string Path,
    long? ExpectedSize, string? ExpectedSha256, long? ActualSize, string? ActualSha256,
    string Status, string Detail = "");
