using LevelUp.NavTableUpdater.App.Services;
using LevelUp.NavTableUpdater.App.ViewModels;
using System.Security.Cryptography;
using Velopack;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.Sources;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class ApplicationUpdateViewModelTests
{
    [Fact]
    public async Task VelopackService_WithManagedLocator_ChecksAndDownloadsVerifiedFullPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), $"xplane-toolkit-app-update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var source = new FakeVelopackUpdateSource("XPlane737NGMaintenanceToolkit", "0.9.0");
            var locator = new TestVelopackLocator("XPlane737NGMaintenanceToolkit", "0.8.2", root);
            var manager = new UpdateManager(
                source,
                new UpdateOptions { ExplicitChannel = "stable-osx-arm64" },
                locator);
            var service = new VelopackApplicationUpdateService(manager);
            var progress = new List<int>();

            var result = await service.CheckForUpdatesAsync();
            await service.DownloadUpdateAsync(new InlineProgress<int>(progress.Add));

            Assert.True(result.IsManagedInstallation);
            Assert.Equal("0.8.2", result.CurrentVersion);
            Assert.Equal("0.9.0", result.AvailableVersion);
            Assert.Equal("stable-osx-arm64", source.RequestedChannel);
            Assert.Contains(100, progress);
            Assert.Single(Directory.EnumerateFiles(root, "*.nupkg", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenManagedUpdateExists_ShowsDownloadBanner()
    {
        var service = new FakeApplicationUpdateService
        {
            CheckResult = new ApplicationUpdateCheckResult(
                true,
                "0.8.2",
                "0.9.0",
                "Release notes",
                "https://example.invalid/releases/tag/v0.9.0")
        };
        var logs = new List<string>();
        var viewModel = CreateViewModel(service, logs: logs);

        await viewModel.CheckForUpdatesAsync();

        Assert.True(viewModel.BannerVisible);
        Assert.True(viewModel.DownloadVisible);
        Assert.False(viewModel.RestartVisible);
        Assert.Equal("Toolkit 0.9.0 is available", viewModel.Title);
        Assert.Equal("https://example.invalid/releases/tag/v0.9.0", viewModel.ReleaseUrl);
        Assert.Contains(logs, entry => entry.Contains("0.8.2 -> 0.9.0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DownloadCommand_WhenDownloadSucceeds_EnablesConfirmedRestart()
    {
        var service = new FakeApplicationUpdateService();
        var interaction = new FakeUserInteractionService { ConfirmResult = true };
        var maintenanceStates = new List<bool>();
        var viewModel = CreateViewModel(service, interaction, maintenanceStates);
        await viewModel.CheckForUpdatesAsync();

        await viewModel.DownloadCommand.ExecuteAsync(null);

        Assert.Equal(1, service.DownloadCount);
        Assert.Equal([false, true], maintenanceStates);
        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.DownloadVisible);
        Assert.True(viewModel.RestartVisible);
        Assert.Equal(100, viewModel.Progress);

        await viewModel.ApplyCommand.ExecuteAsync(null);

        Assert.True(service.ApplyCalled);
        Assert.Single(interaction.Confirmations);
    }

    [Fact]
    public async Task CancelCommand_DuringDownload_CancelsWithoutEnablingRestart()
    {
        var service = new FakeApplicationUpdateService { WaitForCancellation = true };
        var viewModel = CreateViewModel(service);
        await viewModel.CheckForUpdatesAsync();

        var download = viewModel.DownloadCommand.ExecuteAsync(null);
        await service.DownloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.CancelCommand.Execute(null);
        await download;

        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.DownloadVisible);
        Assert.False(viewModel.RestartVisible);
        Assert.Contains("canceled", viewModel.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenLaunchIsUnmanaged_DoesNotShowBanner()
    {
        var service = new FakeApplicationUpdateService
        {
            CheckResult = new ApplicationUpdateCheckResult(
                false,
                "0.9.0",
                null,
                null,
                "https://example.invalid/releases")
        };
        var viewModel = CreateViewModel(service);

        await viewModel.CheckForUpdatesAsync();

        Assert.False(viewModel.BannerVisible);
        Assert.False(viewModel.DownloadVisible);
    }

    private static ApplicationUpdateViewModel CreateViewModel(
        FakeApplicationUpdateService service,
        FakeUserInteractionService? interaction = null,
        List<bool>? maintenanceStates = null,
        List<string>? logs = null) =>
        new(
            service,
            interaction ?? new FakeUserInteractionService(),
            () => true,
            enabled => maintenanceStates?.Add(enabled),
            message => logs?.Add(message));

    private sealed class FakeApplicationUpdateService : IApplicationUpdateService
    {
        public ApplicationUpdateCheckResult CheckResult { get; set; } = new(
            true,
            "0.8.2",
            "0.9.0",
            "Release notes",
            "https://example.invalid/releases/tag/v0.9.0");

        public bool WaitForCancellation { get; set; }

        public int DownloadCount { get; private set; }

        public bool ApplyCalled { get; private set; }

        public TaskCompletionSource DownloadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ApplicationUpdateCheckResult> CheckForUpdatesAsync() => Task.FromResult(CheckResult);

        public async Task DownloadUpdateAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            DownloadCount++;
            DownloadStarted.TrySetResult();
            progress?.Report(50);
            if (WaitForCancellation)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            progress?.Report(100);
        }

        public void ApplyUpdateAndRestart() => ApplyCalled = true;
    }

    private sealed class FakeUserInteractionService : IUserInteractionService
    {
        public bool ConfirmResult { get; set; }

        public List<ConfirmationRequest> Confirmations { get; } = [];

        public Task<bool> ConfirmAsync(ConfirmationRequest request)
        {
            Confirmations.Add(request);
            return Task.FromResult(ConfirmResult);
        }

        public Task ShowMessageAsync(MessageRequest request) => Task.CompletedTask;
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class FakeVelopackUpdateSource : IUpdateSource
    {
        private readonly byte[] _packageBytes = "verified fake VeloPack full package"u8.ToArray();
        private readonly VelopackAsset _asset;

        public FakeVelopackUpdateSource(string packageId, string version)
        {
            _asset = new VelopackAsset
            {
                PackageId = packageId,
                Version = SemanticVersion.Parse(version),
                Type = VelopackAssetType.Full,
                FileName = $"{packageId}-{version}-stable-osx-arm64-full.nupkg",
                Size = _packageBytes.Length,
                SHA256 = Convert.ToHexString(SHA256.HashData(_packageBytes)),
                NotesMarkdown = "Test release notes"
            };
        }

        public string? RequestedChannel { get; private set; }

        public Task<VelopackAssetFeed> GetReleaseFeed(
            IVelopackLogger logger,
            string? appId,
            string channel,
            Guid? stagedUserId,
            VelopackAsset? latestLocalRelease)
        {
            RequestedChannel = channel;
            return Task.FromResult(new VelopackAssetFeed { Assets = [_asset] });
        }

        public async Task DownloadReleaseEntry(
            IVelopackLogger logger,
            VelopackAsset releaseEntry,
            string localFile,
            Action<int> progress,
            CancellationToken cancellationToken)
        {
            await File.WriteAllBytesAsync(localFile, _packageBytes, cancellationToken);
            progress(100);
        }
    }
}
