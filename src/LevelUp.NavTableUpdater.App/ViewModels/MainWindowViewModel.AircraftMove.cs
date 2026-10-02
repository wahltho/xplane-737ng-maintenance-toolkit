using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LevelUp.NavTableUpdater.App.Services;
using LevelUp.NavTableUpdater.Core.Aircraft;

namespace LevelUp.NavTableUpdater.App.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty] private string aircraftMoveParentPath = "";
    [ObservableProperty] private string aircraftMoveFolderName = "";
    [ObservableProperty] private string aircraftMoveStatus = "Choose a destination parent folder and an unused aircraft folder name. X-Plane must be closed.";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(AircraftMoveControlsLocked))]
    private bool aircraftMoveRecoveryRequired;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(AircraftMoveControlsLocked))]
    private bool aircraftMoveBusy;
    public bool AircraftMoveControlsLocked => AircraftMoveBusy || AircraftMoveRecoveryRequired;
    public bool CanMoveAircraft => ActionsEnabled && CanAutoDetect && CanExportDiagnostics && !AircraftMoveControlsLocked
        && SelectedProduct?.IsDetected == true;
    private AircraftMoveOperation AircraftMover() => new(_stateStore, _settingsStore, _isXPlaneRunning);

    public void SetAircraftMoveParentFromBrowse(string path)
    {
        if (AircraftMoveControlsLocked) return;
        AircraftMoveParentPath = path;
        if (string.IsNullOrWhiteSpace(AircraftMoveFolderName))
            AircraftMoveFolderName = Path.GetFileName(SelectedProduct?.AircraftFolderPath ?? SelectedAircraftPath);
    }

    [RelayCommand(CanExecute = nameof(CanMoveAircraft), IncludeCancelCommand = true)]
    private async Task MoveAircraft(CancellationToken cancellationToken)
    {
        if (!CanMoveAircraft || SelectedProduct is not { } product) return;
        AircraftMoveBusy = true; IsOperationRunning = true;
        try
        {
            var name = AircraftMoveFolderName.Trim();
            if (string.IsNullOrWhiteSpace(AircraftMoveParentPath) || name is "" or "." or ".."
                || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\'))
                throw new ArgumentException("Choose a destination parent and a single unused folder name.");
            var destination = Path.Combine(Path.GetFullPath(AircraftMoveParentPath), name);
            var mover = AircraftMover();
            AircraftMoveStatus = "Checking aircraft files and destination…";
            var plan = await Task.Run(() => mover.Prepare(product.AircraftFolderPath, destination, cancellationToken), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!await _userInteractionService.ConfirmAsync(new ConfirmationRequest("Move or rename aircraft?",
                $"From: {plan.Source}\nTo: {plan.Destination}\n\n{plan.FileCount} files; {plan.TotalBytes / 1048576d:N1} MiB. "
                + "The complete folder, liveries and aircraft settings will be copied and verified. Existing Toolkit installation and restore history will follow it. "
                + "The old folder is removed only after the move is committed. Close X-Plane and do not edit either folder during the move.", "Move aircraft")))
            { AircraftMoveStatus = "Move canceled. No aircraft files were changed."; return; }
            AppendLog($"[MOVE] {plan.Source} -> {plan.Destination}; {plan.FileCount} files, {plan.TotalBytes} bytes. Existing ownership and backups retained.");
            var acceptingProgress = true;
            var progress = new Progress<AircraftMoveProgress>(p =>
            {
                if (!acceptingProgress) return;
                AircraftMoveStatus = p.Message; OperationProgress = p.Percent;
                OperationProgressText = $"{p.Percent}% - {p.Message}";
            });
            OperationTitle = "Moving aircraft"; OperationStatus = "In progress"; OperationProgress = 0;
            AircraftMoveResult result;
            try { result = await Task.Run(() => mover.Execute(plan, progress, cancellationToken), cancellationToken); }
            finally { acceptingProgress = false; }
            AircraftMoveRecoveryRequired = result.CleanupPending;
            AircraftMoveStatus = result.CleanupPending ? "Aircraft moved; source cleanup needs recovery. Close X-Plane and use Retry move recovery."
                : $"Aircraft moved to {result.Destination}. Installation and restore history updated.";
            OperationStatus = result.CleanupPending ? "Recovery required" : "Complete";
            OperationProgress = 100; OperationProgressText = "100% - " + AircraftMoveStatus;
            AppendLog(AircraftMoveStatus);
            if (!result.CleanupPending) SelectAfterAircraftMove(result.Destination);
            await _userInteractionService.ShowMessageAsync(new MessageRequest("Aircraft move", AircraftMoveStatus));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or System.Text.Json.JsonException or OperationCanceledException)
        {
            AircraftMoveRecoveryRequired = AircraftMover().HasPendingMove;
            AircraftMoveStatus = ex is OperationCanceledException ? "Move canceled; the original aircraft and history were retained."
                : "Aircraft move stopped: " + ex.Message;
            if (AircraftMoveRecoveryRequired) AircraftMoveStatus += " Recovery is required before other actions.";
            OperationStatus = AircraftMoveRecoveryRequired ? "Recovery required" : ex is OperationCanceledException ? "Canceled" : "Stopped";
            AppendLog(AircraftMoveStatus);
            await _userInteractionService.ShowMessageAsync(new MessageRequest("Aircraft move stopped", AircraftMoveStatus));
        }
        finally
        {
            AircraftMoveBusy = false;
            IsOperationRunning = AircraftMoveRecoveryRequired;
            ActionsEnabled = !AircraftMoveRecoveryRequired;
            MoveAircraftCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private async Task RecoverAircraftMove()
    {
        if (AircraftMoveBusy) return;
        AircraftMoveBusy = true; IsOperationRunning = true;
        try
        {
            var path = await Task.Run(() => AircraftMover().Recover());
            AircraftMoveRecoveryRequired = false;
            IsOperationRunning = false; ActionsEnabled = true;
            var saved = _settingsStore.Load();
            _settings.SelectedAircraftPath = saved.SelectedAircraftPath;
            if (!string.IsNullOrWhiteSpace(path)) SelectAfterAircraftMove(saved.SelectedAircraftPath.Length == 0 ? path : saved.SelectedAircraftPath);
            AircraftMoveStatus = "Interrupted move recovered. Aircraft and Toolkit history are consistent.";
            AppendLog(AircraftMoveStatus);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or System.Text.Json.JsonException)
        {
            AircraftMoveRecoveryRequired = true; ActionsEnabled = false;
            AircraftMoveStatus = "Move recovery is blocked: " + ex.Message + " Do not delete move folders or the journal. Export the operation log for support.";
            AppendLog(AircraftMoveStatus);
            await _userInteractionService.ShowMessageAsync(new MessageRequest("Move recovery required", AircraftMoveStatus));
        }
        finally { AircraftMoveBusy = false; IsOperationRunning = AircraftMoveRecoveryRequired; }
    }

    private void SelectAfterAircraftMove(string path)
    {
        SelectedCandidate = null;
        foreach (var candidate in DetectedTargets.Where(c => !Directory.Exists(c.Path)
                     || PathsEqual(c.Path, path)).ToArray())
            DetectedTargets.Remove(candidate);
        SelectedAircraftPath = path;
        var busy = AircraftMoveBusy;
        AircraftMoveBusy = false;
        try { Scan(); }
        finally { AircraftMoveBusy = busy; }
        // Refresh the selected entry immediately while retaining other detected installations.
        DetectedTargets.Add(new Core.AircraftCandidate(Path.GetFileName(path), path, "Moved by the Toolkit."));
        DetectedTargetsVisible = DetectedTargets.Count > 1;
    }
}
