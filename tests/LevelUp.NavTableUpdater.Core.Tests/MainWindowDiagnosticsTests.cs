using System.IO.Compression;
using System.Text.Json;
using System.Security.Cryptography;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.App.Services;
using LevelUp.NavTableUpdater.App.ViewModels;
using LevelUp.NavTableUpdater.Core.Detection;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class MainWindowDiagnosticsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mtk-diagnostic-ui-{Guid.NewGuid():N}");

    [Fact]
    public async Task CheckInstallation_UsesRecordedHashesOffline_AndDoesNotWriteOrExport()
    {
        var settings = Settings();
        using var http = new HttpClient(new NoNetwork());
        var dialogs = new Messages();
        var vm = new MainWindowViewModel(dialogs, new NoUpdates(), settingsStore: settings, releaseHttpClient: http,
            detector: new AircraftDetector(Path.Combine(_root, "home")), isXPlaneRunning: () => false);
        var aircraft = Path.Combine(_root, "Aircraft", "LevelUp");
        Directory.CreateDirectory(aircraft);
        var reference = AircraftReferenceCatalog.All.Single(r => r.AircraftId == "levelup-737-800");
        File.WriteAllText(Path.Combine(aircraft, reference.AcfFileName), $"1200 Version\nP acf/_name {reference.ExpectedName}\nP acf/_descrip {reference.ExpectedDescription}\nP acf/_studio {reference.ExpectedStudioContains}\nP acf/_cgY 0\nP acf/_cgZ 0\n");
        foreach (var script in new[] { "B738.a_fms", "B738.tablet" })
        {
            var path = Path.Combine(aircraft, "plugins", "xlua", "scripts", script, script + ".lua");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "script");
        }
        var managed = Path.Combine(aircraft, "managed.lua");
        var backup = Path.Combine(_root, "backups", "original.lua");
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        File.WriteAllText(managed, "patched");
        File.WriteAllText(backup, "original");
        vm.SetAircraftPathFromBrowse(aircraft);
        var store = new ToolStateStore(settings.RootPath, settings.Load().BackupRootPath);
        store.UpdateContentAndProduct(vm.SelectedViewVariant!, (installation, _) =>
            installation.ContentComponents["test.patch"] = new()
            {
                ComponentId = "test.patch", PackageVersion = "1.0.0", RestoreAvailable = true,
                Files = [new() { RelativePath = "managed.lua", InstalledSizeBytes = 7,
                    InstalledSha256 = Hash("patched"), OriginalExisted = true, OriginalSizeBytes = 8,
                    OriginalSha256 = Hash("original"), BackupPath = backup }]
            });
        var before = Snapshot();
        Assert.True(vm.CheckInstallationCommand.CanExecute(null));
        await vm.CheckInstallationCommand.ExecuteAsync(null);
        Assert.Equal("Recorded file checks passed", vm.InstallationCheck!.Status);
        Assert.Contains(vm.InstallationCheck.Files, f => f.Path == managed && f.Status == "Matches recorded hash");
        Assert.Equal(before, Snapshot());
        Assert.Empty(dialogs.Shown);
        Assert.False(Directory.Exists(Path.Combine(_root, "reports")));

        File.WriteAllText(managed, "modified");
        File.Delete(backup);
        before = Snapshot();
        await vm.CheckInstallationCommand.ExecuteAsync(null);
        Assert.Equal("Needs review", vm.InstallationCheck!.Status);
        Assert.Contains(vm.InstallationCheck.Files, f => f.Path == managed && f.Status == "Differs from recorded snapshot");
        Assert.Contains(vm.InstallationCheck.Files, f => f.Path == backup && f.Status == "File missing");
        Assert.Equal(before, Snapshot());
        Assert.False(vm.IsOperationRunning);
        Assert.True(vm.ActionsEnabled);
        Assert.True(vm.CanAutoDetect);
        vm.CanAutoDetect = false;
        Assert.False(vm.CheckInstallationCommand.CanExecute(null));
        vm.CanAutoDetect = true;
        vm.IsToolPackageOperationRunning = true;
        Assert.False(vm.CheckInstallationCommand.CanExecute(null));
        vm.IsToolPackageOperationRunning = false;
        vm.IsContentPackageCatalogCheckRunning = true;
        Assert.False(vm.CheckInstallationCommand.CanExecute(null));
        vm.IsContentPackageCatalogCheckRunning = false;
        vm.IsOperationRunning = true;
        Assert.Null(vm.InstallationCheck);
        Assert.False(vm.CheckInstallationCommand.CanExecute(null));
        vm.IsOperationRunning = false;
        // Cancel at the synchronous status notification, before hashing is scheduled.
        before = Snapshot();
        System.ComponentModel.PropertyChangedEventHandler cancelAtStart = (_, e) =>
        {
            if (e.PropertyName == nameof(vm.InstallationCheckStatus)
                && vm.InstallationCheckStatus == "Checking recorded files and backups…")
                vm.CheckInstallationCommand.Cancel();
        };
        vm.PropertyChanged += cancelAtStart;
        try { await vm.CheckInstallationCommand.ExecuteAsync(null); }
        finally { vm.PropertyChanged -= cancelAtStart; }
        Assert.Null(vm.InstallationCheck);
        Assert.Equal("Check canceled. No files were changed.", vm.InstallationCheckStatus);
        Assert.True(vm.ActionsEnabled);
        Assert.True(vm.CanAutoDetect);
        Assert.False(vm.IsOperationRunning);
        Assert.Equal(before, Snapshot());
        await vm.CheckInstallationCommand.ExecuteAsync(null);
        vm.SetAircraftPathFromBrowse(Path.Combine(_root, "missing"));
        Assert.Null(vm.InstallationCheck);
        Assert.False(vm.CheckInstallationCommand.CanExecute(null));
    }

    private string[] Snapshot() => Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(path => path + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).ToArray();
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

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
        vm.OperationPanelVisible = true;
        vm.OperationStatus = "Blocked";
        vm.OperationSubtitle = "Original backup is missing for plugins/xlua/scripts/B738.a_fms/B738.a_fms.lua.";
        Assert.True(vm.OperationHelpVisible);
        var help = vm.OperationHelp;
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
        Assert.Contains("required original backup", json.RootElement.GetProperty("context").GetProperty("status").GetProperty("Result help").GetString());
        Assert.Equal(help, vm.OperationHelp);
        Assert.True(json.RootElement.GetProperty("pathsAnonymized").GetBoolean());
        Assert.Equal(3, archive.Entries.Count);
    }

    [Fact]
    public void ResultHelp_UpdatesWhenResultChangesAndClearsForSuccessfulOrRunningOperations()
    {
        using var http = new HttpClient(new NoNetwork());
        var vm = new MainWindowViewModel(new Messages(), new NoUpdates(), settingsStore: Settings(), releaseHttpClient: http,
            detector: new AircraftDetector(Path.Combine(_root, "home")));
        vm.OperationPanelVisible = true;
        vm.OperationSubtitle = "Managed target changed after installation: objects/cockpit.obj.";
        vm.OperationStatus = "Blocked";
        Assert.Equal("objects/cockpit.obj", vm.OperationHelp!.AffectedPath);
        vm.OperationSubtitle = "X-Plane is running. Close X-Plane before changing aircraft files.";
        Assert.False(vm.OperationHelp!.HasAffectedPath);
        Assert.Contains("X-Plane", vm.OperationHelp.Reason);
        vm.OperationPanelVisible = false;
        Assert.False(vm.OperationHelpVisible);
        vm.OperationPanelVisible = true;
        Assert.True(vm.OperationHelpVisible);
        vm.OperationStatus = "Transaction in progress";
        Assert.Null(vm.OperationHelp);
        Assert.False(vm.OperationHelpVisible);
        vm.OperationStatus = "Applied";
        Assert.False(vm.OperationHelpVisible);
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
