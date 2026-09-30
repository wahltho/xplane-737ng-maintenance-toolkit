using System.IO.Compression;
using System.Text.Json;
using LevelUp.NavTableUpdater.App.Services;
using LevelUp.NavTableUpdater.App.ViewModels;
using LevelUp.NavTableUpdater.Core.Detection;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class MainWindowDiagnosticsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mtk-diagnostic-ui-{Guid.NewGuid():N}");

    [Fact]
    public async Task ExportCommand_WorksOfflineWithoutAircraft_AndKeepsSettingsAndStateUnchanged()
    {
        var store = Settings();
        var state = new ToolStateStore(store.RootPath, Path.Combine(_root, "backups"));
        state.Save(new ToolStateDocument());
        var beforeState = File.ReadAllBytes(state.StatePath);
        var beforeSettings = File.ReadAllBytes(store.SettingsPath);
        using var http = new HttpClient(new NoNetwork());
        var dialogs = new Messages();
        var vm = new MainWindowViewModel(dialogs, new NoUpdates(), settingsStore: store, releaseHttpClient: http,
            detector: new AircraftDetector(Path.Combine(_root, "home")));
        Assert.True(vm.AnonymizeDiagnosticPaths);
        Assert.True(vm.ExportDiagnosticsCommand.CanExecute(null));

        await vm.ExportDiagnosticsCommand.ExecuteAsync(null);

        Assert.False(vm.IsOperationRunning);
        Assert.True(vm.ActionsEnabled);
        Assert.True(vm.ExportDiagnosticsCommand.CanExecute(null));
        Assert.Equal(beforeState, File.ReadAllBytes(state.StatePath));
        Assert.Equal(beforeSettings, File.ReadAllBytes(store.SettingsPath));
        var path = Assert.Single(Directory.GetFiles(Path.Combine(_root, "reports"), "*.zip"));
        Assert.Contains(path, vm.DiagnosticsExportStatus);
        Assert.Contains(path, Assert.Single(dialogs.Shown).Message);
        using var archive = ZipFile.OpenRead(path);
        using var reader = new StreamReader(archive.GetEntry("diagnostics.json")!.Open());
        using var json = JsonDocument.Parse(reader.ReadToEnd());
        Assert.Equal(vm.ToolkitVersion, json.RootElement.GetProperty("context").GetProperty("toolkitVersion").GetString());
        Assert.True(json.RootElement.GetProperty("pathsAnonymized").GetBoolean());
        Assert.Equal(3, archive.Entries.Count);
    }

    [Fact]
    public async Task ExportCommand_RespectsPathOption_AndReportsWriteFailureWithoutLeavingBusyState()
    {
        var settings = Settings();
        using var http = new HttpClient(new NoNetwork());
        var dialogs = new Messages();
        var vm = new MainWindowViewModel(dialogs, new NoUpdates(), settingsStore: settings, releaseHttpClient: http,
            detector: new AircraftDetector(Path.Combine(_root, "home"))) { AnonymizeDiagnosticPaths = false };
        await vm.ExportDiagnosticsCommand.ExecuteAsync(null);
        var path = Assert.Single(Directory.GetFiles(Path.Combine(_root, "reports"), "*.zip"));
        using (var archive = ZipFile.OpenRead(path))
        using (var reader = new StreamReader(archive.GetEntry("diagnostics.json")!.Open()))
        using (var json = JsonDocument.Parse(reader.ReadToEnd()))
            Assert.False(json.RootElement.GetProperty("pathsAnonymized").GetBoolean());

        var occupied = Path.Combine(_root, "occupied");
        File.WriteAllText(occupied, "file");
        vm.SetDiagnosticsExportRootPathFromBrowse(occupied);
        await vm.ExportDiagnosticsCommand.ExecuteAsync(null);
        Assert.False(vm.IsOperationRunning);
        Assert.True(vm.ExportDiagnosticsCommand.CanExecute(null));
        Assert.Contains("Could not export diagnostics", vm.DiagnosticsExportStatus);
        Assert.Equal("Diagnostic export failed", dialogs.Shown.Last().Title);
    }

    [Fact]
    public async Task ExportCommand_DoesNotRunDuringAircraftOrToolOperations()
    {
        using var http = new HttpClient(new NoNetwork());
        var dialogs = new Messages();
        var vm = new MainWindowViewModel(dialogs, new NoUpdates(), settingsStore: Settings(), releaseHttpClient: http,
            detector: new AircraftDetector(Path.Combine(_root, "home")));
        vm.IsOperationRunning = true;
        Assert.False(vm.ExportDiagnosticsCommand.CanExecute(null));
        await vm.ExportDiagnosticsCommand.ExecuteAsync(null);
        Assert.True(vm.IsOperationRunning);
        Assert.False(Directory.Exists(Path.Combine(_root, "reports")));
        vm.IsOperationRunning = false;
        vm.IsToolPackageOperationRunning = true;
        Assert.False(vm.ExportDiagnosticsCommand.CanExecute(null));
        vm.IsToolPackageOperationRunning = false;
        Assert.True(vm.ExportDiagnosticsCommand.CanExecute(null));
        Assert.Empty(dialogs.Shown);
    }

    private ToolkitSettingsStore Settings()
    {
        var store = new ToolkitSettingsStore(Path.Combine(_root, "state"));
        store.Save(new ToolkitSettingsDocument
        {
            CheckToolkitUpdatesOnStartup = false, CheckAircraftAndPatchUpdatesOnStartup = false,
            BackupRootPath = Path.Combine(_root, "backups"), AircraftUpdateCacheRootPath = Path.Combine(_root, "cache"),
            OfflinePackageRootPath = Path.Combine(_root, "offline"), DiagnosticsExportRootPath = Path.Combine(_root, "reports")
        });
        return store;
    }
    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Diagnostic export must not make network requests.");
    }
    private sealed class NoUpdates : IApplicationUpdateService
    {
        public Task<ApplicationUpdateCheckResult> CheckForUpdatesAsync() => throw new InvalidOperationException("No update check expected.");
        public Task DownloadUpdateAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No download expected.");
        public void ApplyUpdateAndRestart() => throw new InvalidOperationException("No restart expected.");
    }
    private sealed class Messages : IUserInteractionService
    {
        public List<MessageRequest> Shown { get; } = [];
        public Task<bool> ConfirmAsync(ConfirmationRequest request) => throw new InvalidOperationException("No install confirmation expected.");
        public Task ShowMessageAsync(MessageRequest request) { Shown.Add(request); return Task.CompletedTask; }
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
