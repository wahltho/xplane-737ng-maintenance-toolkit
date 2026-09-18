using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LevelUp.NavTableUpdater.App.Services;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Tools;

namespace LevelUp.NavTableUpdater.App.ViewModels;

public sealed partial class HardwareConfigTargetOption(HardwareConfigTarget target) : ObservableObject
{
    public HardwareConfigTarget Target { get; } = target;
    public string Label => $"{Target.DisplayName} — {Target.FileName}" + (Target.Exists ? "" : " (will be created)");
    [ObservableProperty] private bool isSelected;
}

public partial class MainWindowViewModel
{
    public ObservableCollection<HardwareConfigTargetOption> HardwareConfigSources { get; } = [];
    public ObservableCollection<HardwareConfigTargetOption> HardwareConfigTargets { get; } = [];
    [ObservableProperty] private HardwareConfigTargetOption? selectedHardwareConfigSource;
    [ObservableProperty] private string hardwareConfigStatus = "Find hardware configurations in the selected aircraft's X-Plane installation. Close X-Plane before copying or restoring.";
    private string? _hardwareConfigRoot;

    partial void OnSelectedHardwareConfigSourceChanged(HardwareConfigTargetOption? value)
    {
        HardwareConfigTargets.Clear();
        foreach (var item in _hardwareConfigs.Where(t => t.FileName != value?.Target.FileName))
            HardwareConfigTargets.Add(new HardwareConfigTargetOption(item));
    }
    private IReadOnlyList<HardwareConfigTarget> _hardwareConfigs = [];

    private void ClearHardwareConfigSelection()
    {
        _hardwareConfigs = [];
        _hardwareConfigRoot = null;
        SelectedHardwareConfigSource = null;
        HardwareConfigSources.Clear();
        HardwareConfigTargets.Clear();
        HardwareConfigStatus = "Find hardware configurations for the selected X-Plane installation.";
    }

    [RelayCommand]
    private async Task FindHardwareConfigs()
    {
        if (!ActionsEnabled || IsOperationRunning) return;
        var root = XPlaneInstallationLocator.Resolve(SelectedAircraftPath);
        if (root is null) { HardwareConfigStatus = "Select an aircraft inside an X-Plane installation first."; return; }
        var selectedPath = SelectedAircraftPath;
        ActionsEnabled = false;
        try
        {
            var configs = await Task.Run(() => HardwareConfigTransferOperation.Discover(root));
            if (SelectedAircraftPath != selectedPath) return;
            _hardwareConfigRoot = root;
            _hardwareConfigs = configs;
            SelectedHardwareConfigSource = null;
            HardwareConfigSources.Clear();
            foreach (var item in _hardwareConfigs.Where(t => t.Exists)) HardwareConfigSources.Add(new HardwareConfigTargetOption(item));
            SelectedHardwareConfigSource = HardwareConfigSources.FirstOrDefault();
            HardwareConfigStatus = $"{root}\n{_hardwareConfigs.Count} compatible configurations; {HardwareConfigSources.Count} existing sources. Choose a source and tick the destinations. All aircraft copies of the same variant share this file.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { HardwareConfigStatus = ex.Message; }
        finally { ActionsEnabled = true; }
    }

    [RelayCommand]
    private async Task CopyHardwareConfigs()
    {
        if (!ActionsEnabled || IsOperationRunning || _hardwareConfigRoot is null || SelectedHardwareConfigSource is null) return;
        var root = _hardwareConfigRoot;
        var source = SelectedHardwareConfigSource.Target;
        var targets = HardwareConfigTargets.Where(t => t.IsSelected).Select(t => t.Target.FileName).ToArray();
        if (targets.Length == 0) { HardwareConfigStatus = "Select at least one destination."; return; }
        await RunHardwareConfigAction("Copy hardware configurations?",
            $"X-Plane: {root}\nSource: {source.FileName}\nTargets: {string.Join(", ", targets)}\n\nCopies the entire hardware configuration, including calibration and other saved options. Existing files are backed up. Missing files are created. Close X-Plane first.",
            operation => operation.Copy(root, source.FileName, targets));
    }

    [RelayCommand]
    private async Task RestoreHardwareConfigs()
    {
        if (!ActionsEnabled || IsOperationRunning) return;
        var root = XPlaneInstallationLocator.Resolve(SelectedAircraftPath);
        if (root is null) { HardwareConfigStatus = "Select an aircraft inside an X-Plane installation first."; return; }
        await RunHardwareConfigAction("Restore hardware configurations?",
            $"Undo the last hardware copy for {root}. Restore previous files and remove files created by that copy. Files modified since copying will be protected. Close X-Plane first.",
            operation => operation.Restore(root));
    }

    private async Task RunHardwareConfigAction(string title, string message, Func<HardwareConfigTransferOperation, MaintenanceOperationResult> action)
    {
        ActionsEnabled = false;
        try
        {
            if (!await _userInteractionService.ConfirmAsync(new ConfirmationRequest(title, message, "Continue"))) return;
            // Root is captured with the confirmation; changing the selected aircraft cannot redirect the write.
            var operation = new HardwareConfigTransferOperation(_stateStore.BackupRootPath);
            var result = await Task.Run(() => action(operation));
            HardwareConfigStatus = result.Message;
            foreach (var line in result.Log) AppendLog(line);
            await _userInteractionService.ShowMessageAsync(new MessageRequest($"Hardware configurations: {result.Status}", result.Message));
        }
        finally { ActionsEnabled = true; }
    }
}
