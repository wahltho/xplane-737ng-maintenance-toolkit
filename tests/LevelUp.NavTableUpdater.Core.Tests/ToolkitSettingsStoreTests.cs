using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class ToolkitSettingsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"xplane-737ng-settings-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Load_WhenSettingsFileIsMissing_ReturnsDefaultBackupRoot()
    {
        var store = new ToolkitSettingsStore(_root);

        var settings = store.Load();

        Assert.Equal("", settings.SelectedAircraftPath);
        Assert.Equal(Path.GetFullPath(ToolStateStore.DefaultBackupRootPath), settings.BackupRootPath);
        Assert.False(string.IsNullOrWhiteSpace(settings.AircraftUpdateCacheRootPath));
        Assert.False(string.IsNullOrWhiteSpace(settings.OfflinePackageRootPath));
        Assert.False(string.IsNullOrWhiteSpace(settings.DiagnosticsExportRootPath));
    }

    [Fact]
    public void Save_ThenLoad_PersistsBackupRoot()
    {
        var backupRoot = Path.Combine(_root, "custom-backups");
        var cacheRoot = Path.Combine(_root, "custom-cache");
        var packagesRoot = Path.Combine(_root, "custom-packages");
        var diagnosticsRoot = Path.Combine(_root, "custom-diagnostics");
        var selectedAircraftPath = Path.Combine(_root, "B737-800X");
        var store = new ToolkitSettingsStore(_root);

        store.Save(new ToolkitSettingsDocument
        {
            SelectedAircraftPath = selectedAircraftPath,
            BackupRootPath = backupRoot,
            AircraftUpdateCacheRootPath = cacheRoot,
            OfflinePackageRootPath = packagesRoot,
            DiagnosticsExportRootPath = diagnosticsRoot
        });

        var settings = store.Load();
        Assert.Equal(Path.GetFullPath(selectedAircraftPath), settings.SelectedAircraftPath);
        Assert.Equal(Path.GetFullPath(backupRoot), settings.BackupRootPath);
        Assert.Equal(Path.GetFullPath(cacheRoot), settings.AircraftUpdateCacheRootPath);
        Assert.Equal(Path.GetFullPath(packagesRoot), settings.OfflinePackageRootPath);
        Assert.Equal(Path.GetFullPath(diagnosticsRoot), settings.DiagnosticsExportRootPath);
        Assert.True(File.Exists(store.SettingsPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
