using System.Collections.ObjectModel;
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
    private void RunPrototypeAction(string action)
    {
        var result = _analyzer.Analyze(SelectedAircraftPath, _manifest);
        ApplyAnalysis(result);

        if (!result.IsSafeToPatch)
        {
            AppendLog($"{action}: blocked by current target state ({result.StateLabel}). No files changed.");
            return;
        }

        AppendLog($"{action}: prototype simulation only. No files changed.");
        foreach (var plannedChange in result.PlannedChanges)
        {
            AppendLog($"  would: {plannedChange}");
        }

        RestartNoticeVisible = true;
    }

    [RelayCommand]
    private void ClearLog()
    {
        InstallLog = "";
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
