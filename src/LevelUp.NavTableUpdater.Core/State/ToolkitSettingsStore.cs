using System.Text;
using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Platform;

namespace LevelUp.NavTableUpdater.Core.State;

public sealed class ToolkitSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public ToolkitSettingsStore(string rootPath)
    {
        RootPath = Path.GetFullPath(rootPath);
        SettingsPath = Path.Combine(RootPath, "settings.json");
    }

    public string RootPath { get; }

    public string SettingsPath { get; }

    public static ToolkitSettingsStore CreateDefault() => new(ToolStateStore.DefaultRootPath);

    public ToolkitSettingsDocument Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return CreateDefaultDocument();
        }

        try
        {
            var json = File.ReadAllText(SettingsPath, Encoding.UTF8);
            return Normalize(JsonSerializer.Deserialize<ToolkitSettingsDocument>(json, JsonOptions)
                ?? CreateDefaultDocument());
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or NotSupportedException)
        {
            return CreateDefaultDocument();
        }
    }

    public void Save(ToolkitSettingsDocument document)
    {
        var normalized = Normalize(document);
        Directory.CreateDirectory(RootPath);
        var tempPath = SettingsPath + $".tmp-{Guid.NewGuid():N}";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(normalized, JsonOptions), new UTF8Encoding(false));
        File.Move(tempPath, SettingsPath, overwrite: true);
    }

    private static ToolkitSettingsDocument CreateDefaultDocument() =>
        new()
        {
            SelectedAircraftPath = "",
            BackupRootPath = ToolkitPaths.DefaultBackupRootPath,
            AircraftUpdateCacheRootPath = ToolkitPaths.DefaultAircraftUpdateCacheRootPath,
            OfflinePackageRootPath = ToolkitPaths.DefaultOfflinePackageRootPath,
            DiagnosticsExportRootPath = ToolkitPaths.DefaultDiagnosticsExportRootPath
        };

    private static ToolkitSettingsDocument Normalize(ToolkitSettingsDocument document)
    {
        document.SchemaVersion = document.SchemaVersion <= 0 ? 1 : document.SchemaVersion;
        document.SelectedAircraftPath = NormalizeOptionalPath(document.SelectedAircraftPath);
        document.BackupRootPath = NormalizePath(document.BackupRootPath, ToolkitPaths.DefaultBackupRootPath);
        document.AircraftUpdateCacheRootPath = NormalizePath(document.AircraftUpdateCacheRootPath, ToolkitPaths.DefaultAircraftUpdateCacheRootPath);
        document.OfflinePackageRootPath = NormalizePath(document.OfflinePackageRootPath, ToolkitPaths.DefaultOfflinePackageRootPath);
        document.DiagnosticsExportRootPath = NormalizePath(document.DiagnosticsExportRootPath, ToolkitPaths.DefaultDiagnosticsExportRootPath);
        return document;
    }

    private static string NormalizePath(string path, string fallback) =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? fallback : path);

    private static string NormalizeOptionalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "";
        }
    }
}
