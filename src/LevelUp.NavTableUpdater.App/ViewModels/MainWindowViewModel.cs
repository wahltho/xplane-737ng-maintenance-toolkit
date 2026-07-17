using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LevelUp.NavTableUpdater.Core;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Analysis;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.Detection;
using LevelUp.NavTableUpdater.Core.Manifest;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly AircraftDetector _detector = new();
    private readonly AircraftInstallAnalyzer _analyzer = new();
    private readonly AircraftViewAnalyzer _viewAnalyzer = new();
    private readonly ToolStateStore _stateStore = ToolStateStore.CreateDefault();
    private readonly ApplyDefaultViewFromQv0Operation _applyDefaultViewOperation;
    private readonly ApplyQuickViewCgAdaptOperation _applyQuickViewCgAdaptOperation;
    private readonly RestoreLatestBackupOperation _restoreLatestBackupOperation;
    private readonly VnavContentOperation _vnavContentOperation;
    private readonly IPackageManifestSource _packageManifestSource = new GitHubReleasePackageManifestSource();
    private readonly IReadOnlyList<PackageManifest> _manifests;
    private PackageManifest _manifest;

    [ObservableProperty]
    private string selectedAircraftPath = "";

    [ObservableProperty]
    private AircraftCandidate? selectedCandidate;

    [ObservableProperty]
    private string aircraftStatus = "No aircraft selected";

    [ObservableProperty]
    private string statusSummary = "Select or detect a Zibo or LevelUp aircraft folder to start.";

    [ObservableProperty]
    private string targetScriptPath = "-";

    [ObservableProperty]
    private string localPackageVersion = "-";

    [ObservableProperty]
    private string availablePackageVersion = "-";

    [ObservableProperty]
    private string lineEnding = "-";

    [ObservableProperty]
    private string packageSource = "Bundled manifest";

    [ObservableProperty]
    private string packageId = "-";

    [ObservableProperty]
    private string repositoryUrl = "-";

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

    [ObservableProperty]
    private string viewUtilityStatus = "No aircraft selected";

    [ObservableProperty]
    private string viewUtilitySummary = "Select a Zibo or LevelUp aircraft folder to inspect CG, quick-view, and default-view state.";

    [ObservableProperty]
    private string xPlaneProcessStatus = "Not checked";

    [ObservableProperty]
    private AircraftVariantViewAnalysis? selectedViewVariant;

    public ObservableCollection<AircraftCandidate> DetectedTargets { get; } = [];

    public ObservableCollection<ComponentStatus> Components { get; } = [];

    public ObservableCollection<string> PlannedChanges { get; } = [];

    public ObservableCollection<string> Findings { get; } = [];

    public ObservableCollection<AircraftVariantViewAnalysis> ViewVariants { get; } = [];

    public ObservableCollection<string> ViewFindings { get; } = [];

    public MainWindowViewModel()
    {
        _manifests = LoadManifests();
        _manifest = _manifests[0];
        _applyDefaultViewOperation = new ApplyDefaultViewFromQv0Operation(_stateStore);
        _applyQuickViewCgAdaptOperation = new ApplyQuickViewCgAdaptOperation(_stateStore);
        _restoreLatestBackupOperation = new RestoreLatestBackupOperation(_stateStore);
        _vnavContentOperation = new VnavContentOperation(_stateStore, CreatePayloadSource());
        ApplyManifest(_manifest);
        ApplyAnalysis(AircraftAnalysisResult.Empty(_manifest.PackageVersion));
        ApplyViewAnalysis(AircraftViewAnalysisResult.Empty());
        AppendLog("Toolkit started. VNAV package and view-maintenance actions can write after validation and backup.");
        AppendLog($"Loaded {_manifests.Count} bundled manifest(s). Active: {_manifest.PackageId} {_manifest.PackageVersion}.");
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
        var viewResult = _viewAnalyzer.Analyze(SelectedAircraftPath);
        ApplyManifest(SelectManifest(viewResult));
        var result = _analyzer.Analyze(SelectedAircraftPath, _manifest);
        ApplyAnalysis(result);
        ApplyViewAnalysis(viewResult);
        AppendLog($"Scan complete using {_manifest.PackageId}: {result.StateLabel}.");
        AppendLog($"View utility scan complete: {viewResult.StateLabel}.");
    }

    [RelayCommand]
    private void DryRun()
    {
        var viewResult = _viewAnalyzer.Analyze(SelectedAircraftPath);
        ApplyManifest(SelectManifest(viewResult));
        var result = _analyzer.Analyze(SelectedAircraftPath, _manifest);
        ApplyAnalysis(result);
        ApplyViewAnalysis(viewResult);
        AppendLog("Dry-run complete. Planned changes were calculated without writing files.");
    }

    [RelayCommand]
    private async Task RunPackageAction(string action)
    {
        if (IsOperationRunning)
        {
            return;
        }

        var viewResult = _viewAnalyzer.Analyze(SelectedAircraftPath);
        ApplyManifest(SelectManifest(viewResult));
        var result = _analyzer.Analyze(SelectedAircraftPath, _manifest);
        ApplyAnalysis(result);
        ApplyViewAnalysis(viewResult);

        var selectedVariant = SelectedViewVariant;
        if (selectedVariant is null)
        {
            AppendLog($"{action}: blocked because no aircraft variant is selected.");
            return;
        }

        if (string.Equals(action, "Restore", StringComparison.OrdinalIgnoreCase))
        {
            RunViewMaintenanceAction(
                "Restore latest backup",
                "Preparing restore transaction",
                "Backup restored",
                "Restore blocked",
                selectedVariant,
                () => _restoreLatestBackupOperation.Restore(selectedVariant));
            return;
        }

        if (!TryParseContentAction(action, out var contentAction))
        {
            AppendLog($"{action}: unknown VNAV action.");
            return;
        }

        await RunVnavContentAction(contentAction, selectedVariant);
    }

    [RelayCommand]
    private void ClearLog()
    {
        InstallLog = "";
        OperationLog = "";
    }

    [RelayCommand]
    private void ApplyQv0ToDefaultView()
    {
        if (IsOperationRunning)
        {
            return;
        }

        var selectedVariant = SelectedViewVariant;
        if (selectedVariant is null)
        {
            AppendLog("Apply QV0 to Default View: blocked because no view variant is selected.");
            return;
        }

        OperationPanelVisible = true;
        OperationLog = "";
        OperationElapsed = "00:00s";
        OperationProgress = 0;
        OperationStatus = "Transaction in progress";
        OperationTitle = "Apply QV0 to Default View";
        OperationSubtitle = $"Preparing default-view transaction for {selectedVariant.DisplayName}.";
        OperationProgressText = "0% - Validating target and X-Plane process state";
        RestartNoticeVisible = false;

        IsOperationRunning = true;
        ActionsEnabled = false;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = _applyDefaultViewOperation.Apply(selectedVariant);
            foreach (var line in result.Log)
            {
                AppendOperationLog(line);
            }

            OperationElapsed = FormatElapsed(stopwatch.Elapsed);
            OperationStatus = result.Status;
            OperationTitle = result.Succeeded
                ? result.Changed ? "Default View updated" : "Default View unchanged"
                : "Default View update blocked";
            OperationSubtitle = result.Message;
            OperationProgress = result.Succeeded ? 100 : 0;
            OperationProgressText = result.Succeeded
                ? result.Changed ? "100% - ACF updated and backup recorded" : "100% - No file change required"
                : "0% - Transaction did not start";
            RestartNoticeVisible = result.Changed;

            if (result.BackupPath is not null)
            {
                AppendLog($"Apply QV0 to Default View: backup created at {result.BackupPath}");
            }

            AppendLog($"Apply QV0 to Default View: {result.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            OperationElapsed = FormatElapsed(stopwatch.Elapsed);
            OperationStatus = "Failed";
            OperationTitle = "Default View update failed";
            OperationSubtitle = ex.Message;
            OperationProgress = 0;
            OperationProgressText = "0% - Transaction failed before completion";
            AppendOperationLog($"[FAILED] {ex.Message}");
            AppendLog($"Apply QV0 to Default View failed: {ex.Message}");
        }
        finally
        {
            IsOperationRunning = false;
            ActionsEnabled = true;
            var selectedPath = selectedVariant.AcfPath;
            ApplyAnalysis(_analyzer.Analyze(SelectedAircraftPath, _manifest));
            ApplyViewAnalysis(_viewAnalyzer.Analyze(SelectedAircraftPath), selectedPath);
        }
    }

    [RelayCommand]
    private void AdaptQuickViewsForCg()
    {
        if (IsOperationRunning)
        {
            return;
        }

        var selectedVariant = SelectedViewVariant;
        if (selectedVariant is null)
        {
            AppendLog("Adapt Quick Views after CG change: blocked because no view variant is selected.");
            return;
        }

        RunViewMaintenanceAction(
            "Adapt Quick Views after CG change",
            "Preparing quick-view CG transaction",
            "Quick Views adjusted",
            "Quick View adaptation blocked",
            selectedVariant,
            () => _applyQuickViewCgAdaptOperation.Apply(selectedVariant));
    }

    [RelayCommand]
    private void RestoreLatestBackup()
    {
        if (IsOperationRunning)
        {
            return;
        }

        var selectedVariant = SelectedViewVariant;
        if (selectedVariant is null)
        {
            AppendLog("Restore latest backup: blocked because no view variant is selected.");
            return;
        }

        RunViewMaintenanceAction(
            "Restore latest backup",
            "Preparing restore transaction",
            "Backup restored",
            "Restore blocked",
            selectedVariant,
            () => _restoreLatestBackupOperation.Restore(selectedVariant));
    }

    private void RunViewMaintenanceAction(
        string actionName,
        string preparingTitle,
        string successTitle,
        string blockedTitle,
        AircraftVariantViewAnalysis selectedVariant,
        Func<MaintenanceOperationResult> action)
    {
        OperationPanelVisible = true;
        OperationLog = "";
        OperationElapsed = "00:00s";
        OperationProgress = 0;
        OperationStatus = "Transaction in progress";
        OperationTitle = preparingTitle;
        OperationSubtitle = $"Preparing transaction for {selectedVariant.DisplayName}.";
        OperationProgressText = "0% - Validating target and X-Plane process state";
        RestartNoticeVisible = false;

        IsOperationRunning = true;
        ActionsEnabled = false;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = action();
            foreach (var line in result.Log)
            {
                AppendOperationLog(line);
            }

            OperationElapsed = FormatElapsed(stopwatch.Elapsed);
            OperationStatus = result.Status;
            OperationTitle = result.Succeeded
                ? result.Changed ? successTitle : $"{actionName} unchanged"
                : blockedTitle;
            OperationSubtitle = result.Message;
            OperationProgress = result.Succeeded ? 100 : 0;
            OperationProgressText = result.Succeeded
                ? result.Changed ? "100% - Transaction completed and backup state recorded" : "100% - No file change required"
                : "0% - Transaction did not start";
            RestartNoticeVisible = result.Changed;

            foreach (var backupPath in result.BackupPaths)
            {
                AppendLog($"{actionName}: backup created at {backupPath}");
            }

            AppendLog($"{actionName}: {result.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or FileNotFoundException)
        {
            OperationElapsed = FormatElapsed(stopwatch.Elapsed);
            OperationStatus = "Failed";
            OperationTitle = $"{actionName} failed";
            OperationSubtitle = ex.Message;
            OperationProgress = 0;
            OperationProgressText = "0% - Transaction failed before completion";
            AppendOperationLog($"[FAILED] {ex.Message}");
            AppendLog($"{actionName} failed: {ex.Message}");
        }
        finally
        {
            IsOperationRunning = false;
            ActionsEnabled = true;
            var selectedPath = selectedVariant.AcfPath;
            var viewResult = _viewAnalyzer.Analyze(SelectedAircraftPath);
            ApplyManifest(SelectManifest(viewResult));
            ApplyAnalysis(_analyzer.Analyze(SelectedAircraftPath, _manifest));
            ApplyViewAnalysis(viewResult, selectedPath);
        }
    }

    private async Task RunVnavContentAction(VnavContentAction action, AircraftVariantViewAnalysis selectedVariant)
    {
        OperationPanelVisible = true;
        OperationLog = "";
        OperationElapsed = "00:00s";
        OperationProgress = 0;
        OperationStatus = "Transaction in progress";
        OperationTitle = $"VNAV {action} - Preparing transaction";
        OperationSubtitle = $"Preparing manifest transaction for {selectedVariant.DisplayName}.";
        OperationProgressText = "0% - Validating target, X-Plane process state, manifest and payload source";
        RestartNoticeVisible = false;

        IsOperationRunning = true;
        ActionsEnabled = false;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var manifest = await ResolveManifestForActionAsync(_manifest);
            ApplyManifest(manifest);
            var result = await _vnavContentOperation.RunAsync(action, selectedVariant, manifest);
            foreach (var line in result.Log)
            {
                AppendOperationLog(line);
            }

            OperationElapsed = FormatElapsed(stopwatch.Elapsed);
            OperationStatus = result.Status;
            OperationTitle = result.Succeeded
                ? result.Changed ? $"VNAV {action} complete" : $"VNAV {action} unchanged"
                : $"VNAV {action} blocked";
            OperationSubtitle = result.Message;
            OperationProgress = result.Succeeded ? 100 : 0;
            OperationProgressText = result.Succeeded
                ? result.Changed ? "100% - VNAV transaction completed and backup state recorded" : "100% - No file change required"
                : "0% - Transaction did not start";
            RestartNoticeVisible = result.Changed;

            foreach (var backupPath in result.BackupPaths)
            {
                AppendLog($"VNAV {action}: backup created at {backupPath}");
            }

            AppendLog($"VNAV {action}: {result.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException)
        {
            OperationElapsed = FormatElapsed(stopwatch.Elapsed);
            OperationStatus = "Failed";
            OperationTitle = $"VNAV {action} failed";
            OperationSubtitle = ex.Message;
            OperationProgress = 0;
            OperationProgressText = "0% - Transaction failed before completion";
            AppendOperationLog($"[FAILED] {ex.Message}");
            AppendLog($"VNAV {action} failed: {ex.Message}");
        }
        finally
        {
            IsOperationRunning = false;
            ActionsEnabled = true;
            var selectedPath = selectedVariant.AcfPath;
            var viewResult = _viewAnalyzer.Analyze(SelectedAircraftPath);
            ApplyManifest(SelectManifest(viewResult));
            ApplyAnalysis(_analyzer.Analyze(SelectedAircraftPath, _manifest));
            ApplyViewAnalysis(viewResult, selectedPath);
        }
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

    private void ApplyViewAnalysis(AircraftViewAnalysisResult result, string? preferredAcfPath = null)
    {
        var currentSelection = preferredAcfPath ?? SelectedViewVariant?.AcfPath;
        ViewUtilityStatus = result.StateLabel;
        ViewUtilitySummary = result.Summary;
        XPlaneProcessStatus = result.IsXPlaneRunning ? "Running - write actions blocked" : "Not running";
        ViewVariants.ReplaceWith(result.Variants);
        ViewFindings.ReplaceWith(result.Findings);
        SelectedViewVariant = ViewVariants.FirstOrDefault(variant => string.Equals(variant.AcfPath, currentSelection, StringComparison.Ordinal))
            ?? ViewVariants.FirstOrDefault();
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

    private void ApplyManifest(PackageManifest manifest)
    {
        _manifest = manifest;
        PackageId = manifest.PackageId;
        RepositoryUrl = manifest.RepositoryUrl;
        AvailablePackageVersion = manifest.PackageVersion;
        PackageSource = manifest.PackageId.Contains("zibo", StringComparison.OrdinalIgnoreCase)
            ? "Zibo GitHub Release package with local/offline fallback"
            : "LevelUp GitHub Release package with local/offline fallback";
    }

    private PackageManifest SelectManifest(AircraftViewAnalysisResult viewResult)
    {
        var family = viewResult.Variants.FirstOrDefault()?.Family;
        if (string.Equals(family, "zibo-737ng", StringComparison.OrdinalIgnoreCase))
        {
            if (_manifest.PackageId.Contains("zibo", StringComparison.OrdinalIgnoreCase))
            {
                return _manifest;
            }

            return _manifests.FirstOrDefault(manifest => manifest.PackageId.Contains("zibo", StringComparison.OrdinalIgnoreCase))
                ?? _manifest;
        }

        if (string.Equals(family, "levelup-737ng", StringComparison.OrdinalIgnoreCase))
        {
            if (_manifest.PackageId.Contains("levelup", StringComparison.OrdinalIgnoreCase))
            {
                return _manifest;
            }

            return _manifests.FirstOrDefault(manifest => manifest.PackageId.Contains("levelup", StringComparison.OrdinalIgnoreCase))
                ?? _manifest;
        }

        return _manifest;
    }

    private static IReadOnlyList<PackageManifest> LoadManifests()
    {
        var contentDir = Path.Combine(AppContext.BaseDirectory, "Content");
        if (!Directory.Exists(contentDir))
        {
            contentDir = Path.Combine(Environment.CurrentDirectory, "src", "LevelUp.NavTableUpdater.App", "Content");
        }

        if (!Directory.Exists(contentDir))
        {
            throw new DirectoryNotFoundException($"Bundled manifest directory is missing: {contentDir}");
        }

        var manifests = Directory.EnumerateFiles(contentDir, "*manifest*.txt")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => ManifestParser.ParsePipeManifest(File.ReadAllText(path)))
            .Where(manifest => !string.IsNullOrWhiteSpace(manifest.PackageId))
            .OrderBy(manifest => manifest.PackageId.Contains("levelup", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ToArray();

        if (manifests.Length == 0)
        {
            throw new FileNotFoundException("No bundled manifests were found.", contentDir);
        }

        return manifests;
    }

    private static IPackagePayloadSource CreatePayloadSource() =>
        new CompositePackagePayloadSource(
            new GitHubReleasePackagePayloadSource(),
            new LocalDirectoryPackagePayloadSource(BuildLocalPackageDirectories()));

    private async Task<PackageManifest> ResolveManifestForActionAsync(PackageManifest seedManifest)
    {
        try
        {
            var refreshed = await _packageManifestSource.RefreshAsync(seedManifest);
            AppendLog($"Loaded release manifest {refreshed.PackageId} {refreshed.PackageVersion} from GitHub Releases.");
            return refreshed;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException)
        {
            AppendLog($"Using bundled manifest for {seedManifest.PackageId}: {ex.Message}");
            return seedManifest;
        }
    }

    private static IEnumerable<string> BuildLocalPackageDirectories()
    {
        var explicitDirectory = Environment.GetEnvironmentVariable("XPLANE_737NG_PACKAGE_DIR");
        if (!string.IsNullOrWhiteSpace(explicitDirectory))
        {
            yield return explicitDirectory;
        }

        var contentDir = Path.Combine(AppContext.BaseDirectory, "Content");
        yield return contentDir;

        var sourceContentDir = Path.Combine(Environment.CurrentDirectory, "src", "LevelUp.NavTableUpdater.App", "Content");
        yield return sourceContentDir;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
        {
            yield return Path.Combine(home, "Documents", "Projects", "X-Plane-ZIBO-Descent-Tables");
            yield return Path.Combine(home, "Documents", "Projects", "X-Plane-LevelUp-737NG-Descent-Tables");
        }
    }

    private static bool TryParseContentAction(string action, out VnavContentAction contentAction)
    {
        return Enum.TryParse(action, ignoreCase: true, out contentAction)
            && contentAction is VnavContentAction.Install
                or VnavContentAction.Update
                or VnavContentAction.Repair
                or VnavContentAction.Uninstall;
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
