using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LevelUp.NavTableUpdater.App.Services;
using LevelUp.NavTableUpdater.Core.Diagnostics;

namespace LevelUp.NavTableUpdater.App.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstallationCheckVisible))]
    private InstallationCheckSummary? installationCheck;

    [ObservableProperty]
    private string installationCheckStatus = "Checks recorded files and backups without changing them.";

    public bool InstallationCheckVisible => InstallationCheck is not null;
    public bool CanCheckInstallation => ActionsEnabled && CanAutoDetect && CanExportDiagnostics && SelectedProduct?.IsDetected == true
        && SelectedViewVariant is not null;

    private void ClearInstallationCheck()
    {
        InstallationCheck = null;
        InstallationCheckStatus = "Checks recorded files and backups without changing them.";
    }

    [RelayCommand(CanExecute = nameof(CanCheckInstallation), IncludeCancelCommand = true)]
    private async Task CheckInstallation(CancellationToken cancellationToken)
    {
        if (!CanCheckInstallation) return;
        var context = CaptureDiagnosticContext();
        var actionsWereEnabled = ActionsEnabled;
        var detectionWasEnabled = CanAutoDetect;
        IsOperationRunning = true;
        ActionsEnabled = false;
        CanAutoDetect = false;
        InstallationCheckStatus = "Checking recorded files and backups…";
        try
        {
            var report = await Task.Run(() => new DiagnosticReportExporter().Collect(
                context, _stateStore, anonymizePaths: false, cancellationToken: cancellationToken), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // Selection is normally locked while busy; discard a snapshot if it nevertheless changed.
            if (SelectedProduct?.AircraftFolderPath != context.AircraftFolder) return;
            InstallationCheck = InstallationCheckSummary.Create(report);
            InstallationCheckStatus = InstallationCheck.Status;
        }
        catch (OperationCanceledException)
        {
            InstallationCheckStatus = "Check canceled. No files were changed.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or System.Text.Json.JsonException)
        {
            InstallationCheckStatus = $"Could not check installation: {ex.Message}";
        }
        finally
        {
            IsOperationRunning = false;
            ActionsEnabled = actionsWereEnabled;
            CanAutoDetect = detectionWasEnabled;
            CheckInstallationCommand.NotifyCanExecuteChanged();
        }
    }
}
