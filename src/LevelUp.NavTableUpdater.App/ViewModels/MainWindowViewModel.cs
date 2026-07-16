using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LevelUp.NavTableUpdater.Core;
using LevelUp.NavTableUpdater.Core.Analysis;
using LevelUp.NavTableUpdater.Core.Detection;
using LevelUp.NavTableUpdater.Core.Manifest;

namespace LevelUp.NavTableUpdater.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly AircraftDetector _detector = new();
    private readonly AircraftInstallAnalyzer _analyzer = new();
    private readonly PackageManifest _manifest;

    [ObservableProperty]
    private string selectedAircraftPath = "";

    [ObservableProperty]
    private AircraftCandidate? selectedCandidate;

    [ObservableProperty]
    private string aircraftStatus = "No aircraft selected";

    [ObservableProperty]
    private string statusSummary = "Select or detect a LevelUp aircraft folder to start.";

    [ObservableProperty]
    private string targetScriptPath = "-";

    [ObservableProperty]
    private string localPackageVersion = "-";

    [ObservableProperty]
    private string availablePackageVersion = "-";

    [ObservableProperty]
    private string lineEnding = "-";

    [ObservableProperty]
    private string packageSource = "Local preview manifest";

    [ObservableProperty]
    private bool isSafeToPatch;

    [ObservableProperty]
    private bool restartNoticeVisible;

    [ObservableProperty]
    private string restartNotice = "X-Plane must be fully restarted after a real install, update, repair, restore or uninstall.";

    [ObservableProperty]
    private string installLog = "";

    [ObservableProperty]
    private bool actionsEnabled = true;

    [ObservableProperty]
    private bool operationPanelVisible;

    [ObservableProperty]
    private bool isOperationRunning;

    [ObservableProperty]
    private string operationTitle = "Ready";

    [ObservableProperty]
    private string operationSubtitle = "No transaction is running.";

    [ObservableProperty]
    private string operationElapsed = "00:00s";

    [ObservableProperty]
    private double operationProgress;

    [ObservableProperty]
    private string operationProgressText = "No manifest operation has started.";

    [ObservableProperty]
    private string operationStatus = "Idle";

    [ObservableProperty]
    private string operationLog = "";

    public string PackageId => _manifest.PackageId;

    public string RepositoryUrl => _manifest.RepositoryUrl;

    public ObservableCollection<AircraftCandidate> DetectedTargets { get; } = [];

    public ObservableCollection<ComponentStatus> Components { get; } = [];

    public ObservableCollection<string> PlannedChanges { get; } = [];

    public ObservableCollection<string> Findings { get; } = [];

    public MainWindowViewModel()
    {
        _manifest = LoadManifest();
        AvailablePackageVersion = _manifest.PackageVersion;
        ApplyAnalysis(AircraftAnalysisResult.Empty(_manifest.PackageVersion));
        AppendLog("Prototype started. No install, update, repair, restore or uninstall action writes files in this build.");
        AppendLog($"Loaded preview manifest {_manifest.PackageId} {_manifest.PackageVersion}.");
    }

    public void SetAircraftPathFromBrowse(string path)
    {
        SelectedAircraftPath = path;
        Scan();
    }

    partial void OnSelectedCandidateChanged(AircraftCandidate? value)
    {
        if (value is null)
        {
            return;
        }

        SelectedAircraftPath = value.Path;
        Scan();
    }

    [RelayCommand]
    private void AutoDetect()
    {
        DetectedTargets.Clear();

        foreach (var candidate in _detector.FindCandidates())
        {
            DetectedTargets.Add(candidate);
        }

        if (DetectedTargets.Count == 0)
        {
            AppendLog("Auto-detection found no candidate in common X-Plane aircraft folders.");
            return;
        }

        AppendLog($"Auto-detection found {DetectedTargets.Count} candidate(s).");
        SelectedCandidate = DetectedTargets[0];
    }

    [RelayCommand]
    private void Scan()
    {
        var result = _analyzer.Analyze(SelectedAircraftPath, _manifest);
        ApplyAnalysis(result);
        AppendLog($"Scan complete: {result.StateLabel}.");
    }

    [RelayCommand]
    private void DryRun()
    {
        var result = _analyzer.Analyze(SelectedAircraftPath, _manifest);
        ApplyAnalysis(result);
        AppendLog("Dry-run complete. Planned changes were calculated without writing files.");
    }

    [RelayCommand]
    private async Task RunPrototypeAction(string action)
    {
        if (IsOperationRunning)
        {
            return;
        }

        var result = _analyzer.Analyze(SelectedAircraftPath, _manifest);
        ApplyAnalysis(result);

        if (!result.IsSafeToPatch)
        {
            AppendLog($"{action}: blocked by current target state ({result.StateLabel}). No files changed.");
            ShowBlockedOperation(action, result);
            return;
        }

        await SimulateTransactionAsync(action, result);

        AppendLog($"{action}: prototype transaction complete. No files changed.");
        foreach (var plannedChange in result.PlannedChanges)
        {
            AppendLog($"  would: {plannedChange}");
        }
    }

    [RelayCommand]
    private void ClearLog()
    {
        InstallLog = "";
        OperationLog = "";
    }

    private void ApplyAnalysis(AircraftAnalysisResult result)
    {
        AircraftStatus = result.StateLabel;
        StatusSummary = result.Summary;
        TargetScriptPath = string.IsNullOrWhiteSpace(result.TargetScriptPath) ? "-" : result.TargetScriptPath;
        LocalPackageVersion = result.LocalPackageVersion;
        AvailablePackageVersion = result.AvailablePackageVersion;
        LineEnding = result.LineEnding;
        IsSafeToPatch = result.IsSafeToPatch;

        Components.ReplaceWith(result.Components);
        PlannedChanges.ReplaceWith(result.PlannedChanges);
        Findings.ReplaceWith(result.Findings);
    }

    private void AppendLog(string message)
    {
        var timestamp = DateTimeOffset.Now.ToString("HH:mm:ss");
        InstallLog += $"[{timestamp}] {message}{Environment.NewLine}";
    }

    private void ShowBlockedOperation(string action, AircraftAnalysisResult result)
    {
        OperationPanelVisible = true;
        OperationTitle = $"{action} blocked";
        OperationSubtitle = "The selected target state must be reviewed before any patch transaction can run.";
        OperationElapsed = "00:00s";
        OperationProgress = 0;
        OperationProgressText = "0% - Transaction did not start";
        OperationStatus = "Review required";
        OperationLog = "";
        AppendOperationLog($"[BLOCKED] {action} blocked by target state: {result.StateLabel}");
        AppendOperationLog("[BLOCKED] No files changed.");
        RestartNoticeVisible = false;
    }

    private async Task SimulateTransactionAsync(string action, AircraftAnalysisResult result)
    {
        var steps = BuildPrototypeSteps(action, result).ToArray();
        var stopwatch = Stopwatch.StartNew();

        OperationPanelVisible = true;
        IsOperationRunning = true;
        ActionsEnabled = false;
        OperationLog = "";
        OperationProgress = 0;
        OperationElapsed = "00:00s";
        OperationStatus = "Transaction in progress";
        OperationTitle = $"{action} - Preparing transaction...";
        OperationSubtitle = "Prototype simulation only. No aircraft files are modified in this build.";
        OperationProgressText = "0% - Preparing manifest-defined transaction";
        RestartNoticeVisible = false;

        try
        {
            for (var index = 0; index < steps.Length; index++)
            {
                var step = steps[index];
                OperationTitle = $"{action} - {step.Title}...";
                OperationSubtitle = step.Detail;
                OperationElapsed = FormatElapsed(stopwatch.Elapsed);
                AppendOperationLog($"[STEP {index + 1}/{steps.Length}] {step.Title}... IN_PROGRESS");

                await Task.Delay(step.DelayMs);

                OperationProgress = step.ProgressPercent;
                OperationProgressText = $"{step.ProgressPercent:0}% - {step.ProgressText}";
                OperationElapsed = FormatElapsed(stopwatch.Elapsed);
                AppendOperationLog($"[STEP {index + 1}/{steps.Length}] {step.Title}... OK");
            }

            OperationTitle = $"{action} complete - Restart required";
            OperationSubtitle = "Prototype transaction finished. A real operation would require a full X-Plane restart.";
            OperationProgress = 100;
            OperationProgressText = "100% - Prototype transaction completed without writing files";
            OperationStatus = "Prototype transaction complete";
            OperationElapsed = FormatElapsed(stopwatch.Elapsed);
            AppendOperationLog("[RESULT] Prototype transaction complete. No files changed.");
            AppendOperationLog("[RESULT] Restart-required notice would be shown after a real operation.");
            RestartNoticeVisible = true;
        }
        finally
        {
            IsOperationRunning = false;
            ActionsEnabled = true;
        }
    }

    private static IEnumerable<PrototypeOperationStep> BuildPrototypeSteps(string action, AircraftAnalysisResult result)
    {
        var safeTarget = string.IsNullOrWhiteSpace(result.TargetScriptPath) ? "selected target" : Path.GetFileName(result.TargetScriptPath);

        yield return new PrototypeOperationStep(
            "Validating target directory",
            "Checking structural LevelUp signatures and selected aircraft state.",
            "Target directory validated",
            14,
            260);

        yield return new PrototypeOperationStep(
            "Preparing transaction backup",
            $"Would create an exact backup for manifest-defined target files, including {safeTarget}.",
            "Prepared backup plan for target files",
            30,
            360);

        yield return new PrototypeOperationStep(
            "Checking markers and anchors",
            "Verifying that patch markers, anchors, and legacy signatures are unambiguous.",
            "Markers and anchors verified",
            48,
            320);

        yield return new PrototypeOperationStep(
            "Generating temporary output",
            $"Would apply {action.ToLowerInvariant()} operations to temporary files before replacement.",
            "Temporary output generated",
            66,
            360);

        yield return new PrototypeOperationStep(
            "Validating transaction result",
            "Would verify hashes, line-ending policy, marker uniqueness, and manifest rules.",
            "Transaction result validated",
            84,
            320);

        yield return new PrototypeOperationStep(
            "Finalizing prototype transaction",
            "Recording simulated install state and leaving the aircraft installation untouched.",
            "Prototype transaction finalized",
            100,
            260);
    }

    private void AppendOperationLog(string message)
    {
        OperationLog += $"{message}{Environment.NewLine}";
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        return elapsed.TotalHours >= 1
            ? elapsed.ToString(@"hh\:mm\:ss")
            : elapsed.ToString(@"mm\:ss") + "s";
    }

    private static PackageManifest LoadManifest()
    {
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "Content", "package-manifest-preview.txt");
        if (!File.Exists(manifestPath))
        {
            manifestPath = Path.Combine(Environment.CurrentDirectory, "src", "LevelUp.NavTableUpdater.App", "Content", "package-manifest-preview.txt");
        }

        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Preview manifest is missing.", manifestPath);
        }

        return ManifestParser.ParsePipeManifest(File.ReadAllText(manifestPath));
    }
}

internal sealed record PrototypeOperationStep(
    string Title,
    string Detail,
    string ProgressText,
    double ProgressPercent,
    int DelayMs);

internal static class ObservableCollectionExtensions
{
    public static void ReplaceWith<T>(this ObservableCollection<T> collection, IEnumerable<T> values)
    {
        collection.Clear();
        foreach (var value in values)
        {
            collection.Add(value);
        }
    }
}
