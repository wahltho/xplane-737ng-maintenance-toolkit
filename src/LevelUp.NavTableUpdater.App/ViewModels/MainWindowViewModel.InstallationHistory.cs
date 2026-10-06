using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LevelUp.NavTableUpdater.App.Services;

namespace LevelUp.NavTableUpdater.App.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    private InstallationHistorySummary? installationHistory;

    [ObservableProperty]
    private bool installationHistoryExpanded;

    [ObservableProperty]
    private bool isInstallationHistoryLoading;

    [ObservableProperty]
    private string installationHistoryStatus = "Open this section to load the history.";

    [ObservableProperty]
    private IReadOnlyList<QuickStartTopic> quickStartTopics = QuickStartGuide.ForProduct(null);

    public bool CanRefreshInstallationHistory => CanCheckInstallation;

    partial void OnInstallationHistoryExpandedChanged(bool value)
    {
        if (value && InstallationHistory is null && CanRefreshInstallationHistory)
            RefreshInstallationHistoryCommand.Execute(null);
    }

    private void ClearInstallationHistory(bool collapse = false)
    {
        InstallationHistory = null;
        InstallationHistoryStatus = "Open this section or click Refresh history to load it.";
        if (collapse) InstallationHistoryExpanded = false;
    }

    [RelayCommand(CanExecute = nameof(CanRefreshInstallationHistory), IncludeCancelCommand = true)]
    private async Task RefreshInstallationHistory(CancellationToken cancellationToken)
    {
        if (!CanRefreshInstallationHistory) return;
        var folder = SelectedProduct!.AircraftFolderPath;
        var xPlaneRoot = ResolveCurrentXPlaneRoot();
        var names = _contentPackageCatalog.Packages.ToDictionary(p => p.PackageId, p => p.DisplayName, StringComparer.Ordinal);
        var actionsWereEnabled = ActionsEnabled;
        var detectionWasEnabled = CanAutoDetect;
        // This display reads state but does not invalidate an existing file-check result.
        IsInstallationHistoryLoading = true;
        IsOperationRunning = true;
        ActionsEnabled = false;
        CanAutoDetect = false;
        InstallationHistoryStatus = "Loading history…";
        try
        {
            var history = await Task.Run(() => InstallationHistorySummary.Read(
                _stateStore, folder, xPlaneRoot, names, cancellationToken), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (SelectedProduct?.AircraftFolderPath != folder) return;
            InstallationHistory = history;
            InstallationHistoryStatus = history.Status;
        }
        catch (OperationCanceledException)
        {
            InstallationHistoryStatus = "Refresh canceled. No files were changed.";
        }
        finally
        {
            IsOperationRunning = false;
            IsInstallationHistoryLoading = false;
            ActionsEnabled = actionsWereEnabled;
            CanAutoDetect = detectionWasEnabled;
            RefreshInstallationHistoryCommand.NotifyCanExecuteChanged();
        }
    }
}
