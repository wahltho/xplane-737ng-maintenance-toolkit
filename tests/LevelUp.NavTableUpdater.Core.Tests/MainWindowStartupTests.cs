using System.Net;
using LevelUp.NavTableUpdater.App.Services;
using LevelUp.NavTableUpdater.App.ViewModels;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Detection;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class MainWindowStartupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mtk-startup-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Startup_RespectsPreference_ReportsNetworkFailures_AndReleasesControls(bool enabled, bool timeout)
    {
        var folder = Path.Combine(_root, "Aircraft", "LevelUp");
        Directory.CreateDirectory(folder);
        var reference = AircraftReferenceCatalog.All.Single(r => r.AircraftId == "levelup-737-800");
        File.WriteAllText(Path.Combine(folder, reference.AcfFileName), $"1200 Version\nP acf/_name {reference.ExpectedName}\nP acf/_descrip {reference.ExpectedDescription}\nP acf/_studio {reference.ExpectedStudioContains}\nP acf/_cgY 0\nP acf/_cgZ 0\n");
        var before = File.ReadAllBytes(Path.Combine(folder, reference.AcfFileName));
        var store = new ToolkitSettingsStore(Path.Combine(_root, "state"));
        store.Save(new ToolkitSettingsDocument
        {
            SelectedAircraftPath = folder,
            CheckToolkitUpdatesOnStartup = false,
            CheckAircraftAndPatchUpdatesOnStartup = enabled,
            BackupRootPath = Path.Combine(_root, "backups"),
            AircraftUpdateCacheRootPath = Path.Combine(_root, "cache"),
            OfflinePackageRootPath = Path.Combine(_root, "offline"),
            DiagnosticsExportRootPath = Path.Combine(_root, "logs")
        });
        var handler = new OfflineHandler(timeout);
        using var client = new HttpClient(handler);
        var vm = new MainWindowViewModel(new NoDialogs(), new NoApplicationUpdate(), settingsStore: store,
            releaseHttpClient: client, detector: new AircraftDetector(_root));
        await vm.InitializeAsync();
        Assert.True(vm.ActionsEnabled);
        Assert.False(vm.IsUpstreamCheckRunning);
        Assert.False(vm.IsContentPackageCatalogCheckRunning);
        Assert.Equal(enabled, handler.Urls.Any(u => u.Contains("737NG-Updates")));
        Assert.Equal(enabled, handler.Urls.Any(u => u.Contains("/repos/") && !u.Contains("maintenance-toolkit")));
        if (enabled)
        {
            Assert.Contains("could not be checked", vm.UpstreamUpdateSummary);
            if (!timeout) Assert.Contains("503", vm.UpstreamUpdateSummary);
        }
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(folder, reference.AcfFileName)));
        var count = handler.Urls.Count;
        await vm.InitializeAsync();
        Assert.Equal(count, handler.Urls.Count); // Startup cannot accidentally run twice.
        if (!enabled)
        {
            await vm.RefreshAircraftUpdateCheckCommand.ExecuteAsync(null);
            await vm.CheckContentPackageCatalogCommand.ExecuteAsync(null);
            Assert.Contains(handler.Urls, u => u.Contains("737NG-Updates"));
            Assert.True(vm.ActionsEnabled);
        }
    }

    private sealed class OfflineHandler(bool timeout) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Urls.Add(request.RequestUri!.AbsoluteUri);
            if (timeout) throw new TaskCanceledException("Simulated network timeout");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
    private sealed class NoApplicationUpdate : IApplicationUpdateService
    {
        public Task<ApplicationUpdateCheckResult> CheckForUpdatesAsync() => throw new Exception("Application checks disabled");
        public Task DownloadUpdateAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default) => throw new Exception("No downloads expected");
        public void ApplyUpdateAndRestart() => throw new Exception("No restart expected");
    }
    private sealed class NoDialogs : IUserInteractionService
    {
        public Task<bool> ConfirmAsync(ConfirmationRequest request) => throw new Exception("Startup must not ask to install anything.");
        public Task ShowMessageAsync(MessageRequest request) => throw new Exception("Startup should show inline status only.");
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
