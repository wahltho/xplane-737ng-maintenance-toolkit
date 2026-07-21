namespace LevelUp.NavTableUpdater.Core.State;

public sealed class ToolkitSettingsDocument
{
    public int SchemaVersion { get; set; } = 1;

    public string SelectedAircraftPath { get; set; } = "";

    public string BackupRootPath { get; set; } = "";

    public string AircraftUpdateCacheRootPath { get; set; } = "";

    public string OfflinePackageRootPath { get; set; } = "";

    public string DiagnosticsExportRootPath { get; set; } = "";
}
