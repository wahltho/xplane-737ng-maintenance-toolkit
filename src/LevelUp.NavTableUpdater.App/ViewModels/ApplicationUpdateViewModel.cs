using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LevelUp.NavTableUpdater.App.Services;

namespace LevelUp.NavTableUpdater.App.ViewModels;

public partial class ApplicationUpdateViewModel : ViewModelBase
{
    private readonly IApplicationUpdateService _updateService;
    private readonly IUserInteractionService _userInteractionService;
    private readonly Func<bool> _canStartOperation;
    private readonly Action<bool> _setMaintenanceActionsEnabled;
    private readonly Action<string> _log;
    private CancellationTokenSource? _cancellationSource;

    [ObservableProperty]
    private bool bannerVisible;

    [ObservableProperty]
    private string title = "Toolkit update";

    [ObservableProperty]
    private string status = "Checking for updates...";

    [ObservableProperty]
    private string releaseUrl = VelopackApplicationUpdateService.ReleasesUrl;

    [ObservableProperty]
    private double progress;

    [ObservableProperty]
    private bool progressVisible;

    [ObservableProperty]
    private bool downloadVisible;

    [ObservableProperty]
    private bool restartVisible;

    [ObservableProperty]
    private bool cancelVisible;

    [ObservableProperty]
    private bool isBusy;

    public ApplicationUpdateViewModel(
        IApplicationUpdateService updateService,
        IUserInteractionService userInteractionService,
        Func<bool> canStartOperation,
        Action<bool> setMaintenanceActionsEnabled,
        Action<string> log)
    {
        _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));
        _userInteractionService = userInteractionService ?? throw new ArgumentNullException(nameof(userInteractionService));
        _canStartOperation = canStartOperation ?? throw new ArgumentNullException(nameof(canStartOperation));
        _setMaintenanceActionsEnabled = setMaintenanceActionsEnabled ?? throw new ArgumentNullException(nameof(setMaintenanceActionsEnabled));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task CheckForUpdatesAsync()
    {
        try
        {
            var result = await _updateService.CheckForUpdatesAsync();
            ReleaseUrl = result.ReleaseUrl;
            if (!result.IsManagedInstallation)
            {
                _log($"Toolkit app update check skipped for unmanaged development/manual launch ({result.CurrentVersion}).");
                return;
            }

            if (!result.HasUpdate)
            {
                _log($"Toolkit app update check: {result.CurrentVersion} is current.");
                return;
            }

            Title = $"Toolkit {result.AvailableVersion} is available";
            Status = $"Current version: {result.CurrentVersion}. Download the verified VeloPack update when convenient.";
            Progress = 0;
            ProgressVisible = false;
            DownloadVisible = true;
            RestartVisible = false;
            CancelVisible = false;
            BannerVisible = true;
            _log($"Toolkit app update available: {result.CurrentVersion} -> {result.AvailableVersion}.");
        }
        catch (Exception ex)
        {
            _log($"Toolkit app update check failed without affecting maintenance functions: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task Download()
    {
        if (!DownloadVisible || IsBusy || !_canStartOperation())
        {
            return;
        }

        _cancellationSource?.Dispose();
        _cancellationSource = new CancellationTokenSource();
        var cancellationToken = _cancellationSource.Token;
        IsBusy = true;
        _setMaintenanceActionsEnabled(false);
        DownloadVisible = false;
        RestartVisible = false;
        CancelVisible = true;
        ProgressVisible = true;
        Progress = 0;
        Status = "Downloading and verifying the application update...";
        var downloadProgress = new Progress<int>(value =>
        {
            Progress = Math.Clamp(value, 0, 100);
            Status = $"Downloading and verifying the application update: {Progress:F0}%";
        });

        try
        {
            await _updateService.DownloadUpdateAsync(downloadProgress, cancellationToken);
            Progress = 100;
            ProgressVisible = false;
            CancelVisible = false;
            RestartVisible = true;
            Status = "Update downloaded and verified. Restart the Toolkit to apply it.";
            _log("Toolkit app update downloaded and verified by VeloPack.");
        }
        catch (OperationCanceledException)
        {
            Progress = 0;
            ProgressVisible = false;
            CancelVisible = false;
            DownloadVisible = true;
            Status = "Update download canceled. The current Toolkit version was not changed.";
            _log("Toolkit app update download canceled.");
        }
        catch (Exception ex)
        {
            Progress = 0;
            ProgressVisible = false;
            CancelVisible = false;
            DownloadVisible = true;
            Status = "Update download failed. Normal maintenance functions remain available.";
            _log($"Toolkit app update download failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            _setMaintenanceActionsEnabled(true);
            _cancellationSource.Dispose();
            _cancellationSource = null;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (!IsBusy || _cancellationSource is null)
        {
            return;
        }

        CancelVisible = false;
        Status = "Canceling application update download...";
        _cancellationSource.Cancel();
    }

    [RelayCommand]
    private async Task Apply()
    {
        if (!RestartVisible || IsBusy || !_canStartOperation())
        {
            return;
        }

        var confirmed = await _userInteractionService.ConfirmAsync(new ConfirmationRequest(
            "Restart and update the Toolkit?",
            "The Toolkit will close, apply the downloaded application update and restart. No aircraft, VNAV, patch, tool or resource files are changed by this app-update step.",
            "Restart and update",
            "Later"));
        if (!confirmed)
        {
            return;
        }

        try
        {
            Status = "Closing the Toolkit and applying the update...";
            RestartVisible = false;
            _updateService.ApplyUpdateAndRestart();
        }
        catch (Exception ex)
        {
            RestartVisible = true;
            Status = "The update could not be started. The current Toolkit version remains installed.";
            _log($"Toolkit app update apply failed: {ex.Message}");
            await _userInteractionService.ShowMessageAsync(new MessageRequest(
                "Toolkit update could not start",
                $"The current Toolkit version remains installed.\n\n{ex.Message}"));
        }
    }

    [RelayCommand]
    private void OpenRelease()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ReleaseUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _log($"Could not open Toolkit release page: {ex.Message}");
        }
    }

    [RelayCommand]
    private void Dismiss()
    {
        if (!IsBusy)
        {
            BannerVisible = false;
        }
    }
}
