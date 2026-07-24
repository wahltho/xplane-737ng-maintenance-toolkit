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
using LevelUp.NavTableUpdater.Core.Platform;
using LevelUp.NavTableUpdater.Core.State;
using LevelUp.NavTableUpdater.Core.Upstream;

namespace LevelUp.NavTableUpdater.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly AircraftDetector _detector = new();
    private readonly AircraftInstallAnalyzer _analyzer = new();
    private readonly AircraftViewAnalyzer _viewAnalyzer = new();
    private readonly ToolkitSettingsStore _settingsStore = ToolkitSettingsStore.CreateDefault();
    private readonly ToolkitSettingsDocument _settings;
    private readonly ToolStateStore _stateStore;
    private readonly QuickViewBaselineAnalyzer _quickViewBaselineAnalyzer;
    private readonly ApplyDefaultViewFromQv0Operation _applyDefaultViewOperation;
    private readonly ApplyQuickViewCgAdaptOperation _applyQuickViewCgAdaptOperation;
    private readonly AdoptQuickViewBaselineOperation _adoptQuickViewBaselineOperation;
    private readonly ConfigBackupOperation _configBackupOperation;
    private readonly RestoreLatestBackupOperation _restoreLatestBackupOperation;
    private readonly VnavContentOperation _vnavContentOperation;
    private readonly AircraftUpstreamUpdateChecker _ziboUpdateChecker;
    private readonly AircraftUpdateOperation _aircraftUpdateOperation;
    private AircraftUpdatePackageCache _aircraftUpdatePackageCache;
    private readonly AircraftUpdateDryRunAnalyzer _aircraftUpdateDryRunAnalyzer = new();
    private readonly HttpClient _aircraftUpdateHttpClient = new();
    private readonly IPackageManifestSource _packageManifestSource = new GitHubReleasePackageManifestSource();
    private readonly IReadOnlyList<PackageManifest> _manifests;
    private PackageManifest _manifest;
    private AircraftUpstreamUpdateCheckResult? _lastUpstreamUpdateCheck;

    [ObservableProperty]
    private string selectedAircraftPath = "";

    [ObservableProperty]
    private AircraftCandidate? selectedCandidate;

    [ObservableProperty]
    private ProductTargetStatus? selectedProduct;

    [ObservableProperty]
    private string selectedProductName = "No supported product";

    [ObservableProperty]
    private string selectedProductDetail = "Select a Zibo or LevelUp installation folder.";

    [ObservableProperty]
    private string selectedProductVariants = "-";

    [ObservableProperty]
    private string selectedProductFolderPath = "-";

    [ObservableProperty]
    private bool productSelectorVisible;

    [ObservableProperty]
    private bool fixedProductVisible = true;

    [ObservableProperty]
    private bool productFolderVisible;

    [ObservableProperty]
    private bool detectedTargetsVisible;

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
    private bool productActionsEnabled;

    [ObservableProperty]
    private bool aircraftProductUpdateEnabled;

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

    [ObservableProperty]
    private string quickViewBaselineStatus = "No aircraft selected";

    [ObservableProperty]
    private string quickViewBaselineSource = "-";

    [ObservableProperty]
    private string quickViewBaselineConfidence = "-";

    [ObservableProperty]
    private string quickViewBaselineDelta = "-";

    [ObservableProperty]
    private string quickViewBaselineRecommendation = "Select a supported aircraft variant.";

    [ObservableProperty]
    private string quickViewBaselineDetail = "";

    [ObservableProperty]
    private bool canAdoptQuickViewBaseline;

    [ObservableProperty]
    private bool canAdaptQuickViewsForCg;

    [ObservableProperty]
    private string upstreamUpdateStatus = "No Zibo aircraft selected";

    [ObservableProperty]
    private string upstreamUpdateSummary = "Select a Zibo aircraft folder to check upstream aircraft packages.";

    [ObservableProperty]
    private string upstreamLocalVersion = "-";

    [ObservableProperty]
    private string upstreamAvailableVersion = "-";

    [ObservableProperty]
    private string upstreamPlanAction = "Not checked";

    [ObservableProperty]
    private string upstreamUpdateMode = "-";

    [ObservableProperty]
    private string upstreamSource = ZiboUpstreamFeedParser.DefaultFeedUrl;

    [ObservableProperty]
    private string upstreamLastChecked = "Not checked";

    [ObservableProperty]
    private bool isUpstreamCheckRunning;

    [ObservableProperty]
    private string upstreamCacheRoot = AircraftUpdatePackageCache.DefaultRootPath;

    [ObservableProperty]
    private string upstreamDryRunSummary = "No aircraft package review has been calculated.";

    [ObservableProperty]
    private string upstreamActionStatus = "Check for updates to enable package download or import.";

    [ObservableProperty]
    private bool canImportAircraftUpdateZip;

    [ObservableProperty]
    private bool canDownloadAircraftUpdateZip;

    [ObservableProperty]
    private bool canDryRunAircraftUpdateZip;

    [ObservableProperty]
    private bool canApplyAircraftUpdateZip;

    [ObservableProperty]
    private bool canRestoreAircraftUpdate;

    [ObservableProperty]
    private string backupRootPath = "";

    [ObservableProperty]
    private string defaultBackupRootPath = ToolkitPaths.DefaultBackupRootPath;

    [ObservableProperty]
    private string aircraftUpdateCacheRootPath = "";

    [ObservableProperty]
    private string defaultAircraftUpdateCacheRootPath = ToolkitPaths.DefaultAircraftUpdateCacheRootPath;

    [ObservableProperty]
    private string offlinePackageRootPath = "";

    [ObservableProperty]
    private string defaultOfflinePackageRootPath = ToolkitPaths.DefaultOfflinePackageRootPath;

    [ObservableProperty]
    private string diagnosticsExportRootPath = "";

    [ObservableProperty]
    private string defaultDiagnosticsExportRootPath = ToolkitPaths.DefaultDiagnosticsExportRootPath;

    [ObservableProperty]
    private string toolkitDataRoot = "";

    [ObservableProperty]
    private string toolkitStatePath = "";

    [ObservableProperty]
    private string toolkitSettingsPath = "";

    [ObservableProperty]
    private string settingsStatus = "Backup settings are ready.";

    public ObservableCollection<AircraftCandidate> DetectedTargets { get; } = [];

    public ObservableCollection<ProductTargetStatus> ProductTargets { get; } = [];

    public ObservableCollection<ProductTargetStatus> DetectedProductTargets { get; } = [];

    public ObservableCollection<ComponentStatus> Components { get; } = [];

    public ObservableCollection<string> PlannedChanges { get; } = [];

    public ObservableCollection<string> Findings { get; } = [];

    public ObservableCollection<AircraftVariantViewAnalysis> ViewVariants { get; } = [];

    public ObservableCollection<AircraftVariantViewAnalysis> FilteredViewVariants { get; } = [];

    public ObservableCollection<string> ViewFindings { get; } = [];

    public ObservableCollection<AircraftUpdatePackage> UpstreamRequiredPackages { get; } = [];

    public ObservableCollection<AircraftUpdatePackageCacheEntry> UpstreamPackageCacheEntries { get; } = [];

    public ObservableCollection<AircraftUpdateDryRunEntry> UpstreamDryRunEntries { get; } = [];

    public ObservableCollection<string> UpstreamFindings { get; } = [];

    public MainWindowViewModel()
    {
        _settings = _settingsStore.Load();
        _stateStore = ToolStateStore.CreateDefault(_settings.BackupRootPath);
        _aircraftUpdatePackageCache = new AircraftUpdatePackageCache(_settings.AircraftUpdateCacheRootPath);
        SelectedAircraftPath = _settings.SelectedAircraftPath;
        BackupRootPath = _stateStore.BackupRootPath;
        AircraftUpdateCacheRootPath = _aircraftUpdatePackageCache.RootPath;
        OfflinePackageRootPath = _settings.OfflinePackageRootPath;
        DiagnosticsExportRootPath = _settings.DiagnosticsExportRootPath;
        DefaultBackupRootPath = ToolkitPaths.DefaultBackupRootPath;
        DefaultAircraftUpdateCacheRootPath = ToolkitPaths.DefaultAircraftUpdateCacheRootPath;
        DefaultOfflinePackageRootPath = ToolkitPaths.DefaultOfflinePackageRootPath;
        DefaultDiagnosticsExportRootPath = ToolkitPaths.DefaultDiagnosticsExportRootPath;
        UpstreamCacheRoot = _aircraftUpdatePackageCache.RootPath;
        ToolkitDataRoot = _stateStore.RootPath;
        ToolkitStatePath = _stateStore.StatePath;
        ToolkitSettingsPath = _settingsStore.SettingsPath;
        _manifests = LoadManifests();
        _manifest = _manifests[0];
        _quickViewBaselineAnalyzer = new QuickViewBaselineAnalyzer(_stateStore);
        _applyDefaultViewOperation = new ApplyDefaultViewFromQv0Operation(_stateStore);
        _applyQuickViewCgAdaptOperation = new ApplyQuickViewCgAdaptOperation(_stateStore, _quickViewBaselineAnalyzer);
        _adoptQuickViewBaselineOperation = new AdoptQuickViewBaselineOperation(_stateStore);
        _configBackupOperation = new ConfigBackupOperation(_stateStore);
        _restoreLatestBackupOperation = new RestoreLatestBackupOperation(_stateStore);
        _vnavContentOperation = new VnavContentOperation(_stateStore, CreatePayloadSource());
        _ziboUpdateChecker = new AircraftUpstreamUpdateChecker(
            new ZiboFeedAircraftUpdateIndexSource(_aircraftUpdateHttpClient));
        _aircraftUpdateOperation = new AircraftUpdateOperation(_stateStore, _aircraftUpdateDryRunAnalyzer);
        ApplyManifest(_manifest);
        ApplyAnalysis(AircraftAnalysisResult.Empty(_manifest.PackageVersion));
        ApplyViewAnalysis(AircraftViewAnalysisResult.Empty());
        AppendLog("Toolkit started. VNAV package and view-maintenance actions can write after validation and backup.");
        AppendLog($"Loaded {_manifests.Count} bundled manifest(s). Active: {_manifest.PackageId} {_manifest.PackageVersion}.");
        AppendLog($"Settings loaded. Backup folder: {_stateStore.BackupRootPath}");
        AppendLog($"Settings loaded. Aircraft update cache: {_aircraftUpdatePackageCache.RootPath}");
        if (!string.IsNullOrWhiteSpace(SelectedAircraftPath))
        {
            AppendLog($"Settings loaded. Selected aircraft folder: {SelectedAircraftPath}");
            Scan();
        }
    }

    public void SetAircraftPathFromBrowse(string path)
    {
        SelectedAircraftPath = path;
        SaveSelectedAircraftPathSetting();
        Scan();
    }

    public void SetBackupRootPathFromBrowse(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        BackupRootPath = path;
        SaveBackupSettings();
    }

    public void SetAircraftUpdateCacheRootPathFromBrowse(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        AircraftUpdateCacheRootPath = path;
        SaveAircraftUpdateCacheSettings();
    }

    public void SetOfflinePackageRootPathFromBrowse(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        OfflinePackageRootPath = path;
        SaveOfflinePackageSettings();
    }

    public void SetDiagnosticsExportRootPathFromBrowse(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        DiagnosticsExportRootPath = path;
        SaveDiagnosticsExportSettings();
    }

    public void ImportAircraftUpdateZip(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            RefreshUpstreamActionAvailability("Package selection canceled. No package was imported.");
            return;
        }

        if (_lastUpstreamUpdateCheck is null || _lastUpstreamUpdateCheck.RequiredPackages.Count == 0)
        {
            RefreshUpstreamActionAvailability("Import blocked. Check for updates on a non-custom aircraft package plan first.");
            AppendLog("Aircraft package import blocked: check for updates first.");
            UpstreamFindings.ReplaceWith(["Check for updates on a non-custom aircraft package plan before importing packages."]);
            return;
        }

        if (_lastUpstreamUpdateCheck.IsCustomDistribution)
        {
            RefreshUpstreamActionAvailability("Import blocked. Custom distributions use upstream package information as review-only.");
            AppendLog("Aircraft package import blocked: selected target is a custom distribution.");
            UpstreamFindings.ReplaceWith([
                "Custom distribution detected. Official upstream package import is disabled for this target.",
                "Use a normal upstream Zibo install for package import/review, or define a dedicated custom-port update source."
            ]);
            return;
        }

        var fileName = Path.GetFileName(path);
        var expectedPackage = _lastUpstreamUpdateCheck.RequiredPackages
            .FirstOrDefault(package => string.Equals(package.FileName, fileName, StringComparison.OrdinalIgnoreCase));
        if (expectedPackage is null)
        {
            RefreshUpstreamActionAvailability($"Import blocked. Selected '{fileName}', expected: {BuildRequiredPackageList()}.");
            AppendLog($"Aircraft package import blocked: {fileName} is not required by the current plan.");
            UpstreamFindings.ReplaceWith([
                $"Selected package '{fileName}' is not required by the current upstream package plan.",
                "Check for updates again or select the exact package listed under Required packages."
            ]);
            return;
        }

        try
        {
            var imported = _aircraftUpdatePackageCache.ImportZip(path, expectedPackage);
            RefreshUpstreamCacheEntries();
            UpstreamDryRunEntries.Clear();
            UpstreamDryRunSummary = "Package cache changed. Review aircraft changes before applying.";
            RefreshUpstreamActionAvailability(BuildImportSuccessStatus(imported.Package.FileName));
            AppendLog($"Imported aircraft package into cache: {imported.Package.FileName} ({imported.SizeBytes} bytes, sha256 {imported.Sha256}).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            RefreshUpstreamActionAvailability($"Import failed. {ex.Message}");
            AppendLog($"Aircraft package import failed: {ex.Message}");
            UpstreamFindings.ReplaceWith(["Aircraft package import failed.", ex.Message]);
        }
    }

    [RelayCommand]
    private async Task DownloadAircraftUpdateZips()
    {
        if (IsUpstreamCheckRunning || IsOperationRunning)
        {
            return;
        }

        if (_lastUpstreamUpdateCheck is null || _lastUpstreamUpdateCheck.RequiredPackages.Count == 0)
        {
            RefreshUpstreamActionAvailability("Download blocked. Check for updates on a non-custom aircraft package plan first.");
            AppendLog("Aircraft package download blocked: check for updates first.");
            return;
        }

        if (_lastUpstreamUpdateCheck.IsCustomDistribution)
        {
            RefreshUpstreamActionAvailability("Download blocked. Custom distributions use upstream package information as review-only.");
            AppendLog("Aircraft package download blocked: selected target is a custom distribution.");
            return;
        }

        RefreshUpstreamCacheEntries();
        var missingPackages = UpstreamPackageCacheEntries
            .Where(entry => !entry.IsCached)
            .Select(entry => entry.Package)
            .ToArray();
        if (missingPackages.Length == 0)
        {
            RefreshUpstreamActionAvailability("All required packages are already cached.");
            AppendLog("Aircraft package download skipped: all required packages are cached.");
            return;
        }

        IsUpstreamCheckRunning = true;
        ActionsEnabled = false;
        RefreshUpstreamActionAvailability($"Downloading {missingPackages.Length} required package(s) into the aircraft update cache.");
        UpstreamFindings.ReplaceWith(["Downloading required aircraft packages. No aircraft files are changed."]);

        try
        {
            foreach (var package in missingPackages)
            {
                AppendLog($"Downloading aircraft package: {package.FileName}");
                var downloaded = await _aircraftUpdatePackageCache.DownloadAsync(package, _aircraftUpdateHttpClient);
                AppendLog($"Downloaded aircraft package into cache: {downloaded.Package.FileName} ({downloaded.SizeBytes} bytes, sha256 {downloaded.Sha256}).");
            }

            RefreshUpstreamCacheEntries();
            UpstreamDryRunEntries.Clear();
            UpstreamDryRunSummary = "Package cache changed. Review aircraft changes before applying.";
            RefreshUpstreamActionAvailability("Download complete. Review aircraft changes or apply the cached packages.");
            UpstreamFindings.ReplaceWith(["Required package download completed. No aircraft files were changed."]);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            RefreshUpstreamActionAvailability($"Download failed. Import the exact package manually if the source does not expose a direct ZIP URL. {ex.Message}");
            UpstreamFindings.ReplaceWith([
                "Aircraft package download failed.",
                "The feed may expose torrent links or a Google Drive flow instead of direct ZIP downloads.",
                ex.Message
            ]);
            AppendLog($"Aircraft package download failed: {ex.Message}");
        }
        finally
        {
            IsUpstreamCheckRunning = false;
            ActionsEnabled = true;
            RefreshUpstreamCacheEntries();
        }
    }

    partial void OnSelectedCandidateChanged(AircraftCandidate? value)
    {
        if (value is null)
        {
            return;
        }

        SelectedAircraftPath = value.Path;
        SaveSelectedAircraftPathSetting();
        Scan();
    }

    partial void OnSelectedProductChanged(ProductTargetStatus? value)
    {
        RefreshSelectedProductSummary(value);
        if (value is null)
        {
            RefreshFilteredViewVariants();
            return;
        }

        if (!value.IsDetected)
        {
            RefreshFilteredViewVariants();
            return;
        }

        RefreshFilteredViewVariants(SelectedViewVariant?.AcfPath);
        RefreshProductScopedPackageAnalysis();
    }

    [RelayCommand]
    private void AutoDetect()
    {
        SelectedCandidate = null;
        DetectedTargets.Clear();
        DetectedTargetsVisible = false;

        var additionalRoots = string.IsNullOrWhiteSpace(SelectedAircraftPath)
            ? []
            : new[] { SelectedAircraftPath };
        foreach (var candidate in _detector.FindCandidates(additionalRoots))
        {
            DetectedTargets.Add(candidate);
        }

        if (DetectedTargets.Count == 0)
        {
            AppendLog("Auto-detection found no candidate in common X-Plane aircraft folders.");
            return;
        }

        AppendLog($"Auto-detection found {DetectedTargets.Count} candidate(s).");
        DetectedTargetsVisible = DetectedTargets.Count > 1;
        SelectedCandidate = DetectedTargets[0];
    }

    [RelayCommand]
    private void Scan()
    {
        SaveSelectedAircraftPathSetting();
        var viewResult = _viewAnalyzer.Analyze(SelectedAircraftPath);
        ApplyViewAnalysis(viewResult);
        ApplyManifest(SelectManifest(viewResult));
        var result = _analyzer.Analyze(CurrentProductAircraftFolderPath(), _manifest);
        ApplyAnalysis(result);
        AppendLog($"Scan complete using {_manifest.PackageId}: {result.StateLabel}.");
        AppendLog($"View utility scan complete: {viewResult.StateLabel}.");
    }

    [RelayCommand]
    private void DryRun()
    {
        var viewResult = _viewAnalyzer.Analyze(SelectedAircraftPath);
        ApplyViewAnalysis(viewResult);
        ApplyManifest(SelectManifest(viewResult));
        var result = _analyzer.Analyze(CurrentProductAircraftFolderPath(), _manifest);
        ApplyAnalysis(result);
        AppendLog("VNAV review complete. Planned changes were calculated without writing files.");
    }

    [RelayCommand]
    private async Task RunPackageAction(string action)
    {
        if (IsOperationRunning)
        {
            return;
        }

        var viewResult = _viewAnalyzer.Analyze(SelectedAircraftPath);
        ApplyViewAnalysis(viewResult);
        ApplyManifest(SelectManifest(viewResult));
        var result = _analyzer.Analyze(CurrentProductAircraftFolderPath(), _manifest);
        ApplyAnalysis(result);

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
    private void ExportLog()
    {
        try
        {
            Directory.CreateDirectory(DiagnosticsExportRootPath);
            var fileName = $"xplane-737ng-maintenance-log-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.txt";
            var path = Path.Combine(DiagnosticsExportRootPath, fileName);
            File.WriteAllText(
                path,
                string.Join(
                    Environment.NewLine,
                    [
                        "X-Plane 737NG Maintenance Toolkit Log",
                        $"Exported: {DateTimeOffset.Now:O}",
                        $"Selected aircraft: {SelectedAircraftPath}",
                        $"Detected product: {SelectedProductName}",
                        $"Product folder: {SelectedProductFolderPath}",
                        $"Target script: {TargetScriptPath}",
                        $"Line endings: {LineEnding}",
                        $"Repository: {RepositoryUrl}",
                        "",
                        "Install Log",
                        InstallLog,
                        "",
                        "Operation Log",
                        OperationLog
                    ]));
            AppendLog($"Log exported: {path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            AppendLog($"Log export failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task UpdateAircraftPackages()
    {
        if (IsOperationRunning || IsUpstreamCheckRunning)
        {
            return;
        }

        await RefreshZiboUpdateCheck();

        if (_lastUpstreamUpdateCheck is null || _lastUpstreamUpdateCheck.IsCustomDistribution)
        {
            return;
        }

        if (CanDownloadAircraftUpdateZip)
        {
            await DownloadAircraftUpdateZips();
        }

        if (CanDryRunAircraftUpdateZip)
        {
            DryRunAircraftUpdate();
        }

        if (CanApplyAircraftUpdateZip)
        {
            ApplyAircraftUpdate();
        }
    }

    [RelayCommand]
    private async Task RefreshZiboUpdateCheck()
    {
        if (IsUpstreamCheckRunning)
        {
            return;
        }

        var viewResult = _viewAnalyzer.Analyze(SelectedAircraftPath);
        ApplyViewAnalysis(viewResult);
        ApplyManifest(SelectManifest(viewResult));
        ApplyAnalysis(_analyzer.Analyze(CurrentProductAircraftFolderPath(), _manifest));

        var selectedVariant = SelectedViewVariant;
        IsUpstreamCheckRunning = true;
        ActionsEnabled = false;
        _lastUpstreamUpdateCheck = null;
        UpstreamUpdateStatus = "Checking Zibo feed";
        UpstreamUpdateSummary = "Reading upstream index and planning baseline/cumulative package requirements.";
        UpstreamPlanAction = "Checking";
        UpstreamRequiredPackages.Clear();
        UpstreamPackageCacheEntries.Clear();
        UpstreamDryRunEntries.Clear();
        UpstreamDryRunSummary = "No aircraft package review has been calculated.";
        RefreshUpstreamActionAvailability("Checking aircraft package plan. Download and import are disabled while the feed is refreshed.");
        UpstreamFindings.ReplaceWith(["Read-only check in progress. No aircraft files will be changed."]);

        try
        {
            var result = await _ziboUpdateChecker.CheckZiboAsync(selectedVariant);
            ApplyUpstreamUpdateCheck(result);
            AppendLog($"Zibo upstream check: {result.StateLabel} - {result.Summary}");
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException or TaskCanceledException or System.Xml.XmlException)
        {
            UpstreamUpdateStatus = "Feed check failed";
            UpstreamUpdateSummary = ex.Message;
            UpstreamAvailableVersion = "-";
            UpstreamPlanAction = "Not checked";
            UpstreamUpdateMode = "-";
            UpstreamLastChecked = DateTimeOffset.Now.ToString("HH:mm:ss");
            UpstreamRequiredPackages.Clear();
            UpstreamPackageCacheEntries.Clear();
            UpstreamDryRunEntries.Clear();
            UpstreamDryRunSummary = "No aircraft package review has been calculated.";
            RefreshUpstreamActionAvailability("Import unavailable. Upstream package check failed before a plan was available.");
            UpstreamFindings.ReplaceWith([
                "Read-only check failed before a package plan could be built.",
                ex.Message
            ]);
            AppendLog($"Zibo upstream check failed: {ex.Message}");
        }
        finally
        {
            IsUpstreamCheckRunning = false;
            ActionsEnabled = true;
        }
    }

    [RelayCommand]
    private void DryRunAircraftUpdate()
    {
        if (_lastUpstreamUpdateCheck is null)
        {
            RefreshUpstreamActionAvailability("Review blocked. Check for updates before reviewing aircraft package changes.");
            AppendLog("Aircraft package review blocked: check for updates first.");
            UpstreamFindings.ReplaceWith(["Check for updates before reviewing aircraft package changes."]);
            return;
        }

        if (_lastUpstreamUpdateCheck.IsCustomDistribution)
        {
            RefreshUpstreamActionAvailability("Review blocked. Custom distributions use upstream package information as review-only.");
            AppendLog("Aircraft package review blocked: selected target is a custom distribution.");
            UpstreamFindings.ReplaceWith([
                "Custom distribution detected. Official upstream packages are review-only for this target.",
                "Use a normal upstream Zibo install for package import/review, or define a dedicated custom-port update source."
            ]);
            return;
        }

        if (_lastUpstreamUpdateCheck.RequiredPackages.Count == 0)
        {
            AppendLog("Aircraft package review: no upstream packages are required by the current plan.");
            UpstreamDryRunEntries.Clear();
            UpstreamDryRunSummary = "No upstream package changes are required.";
            RefreshUpstreamActionAvailability("No upstream package review is required for this target.");
            return;
        }

        RefreshUpstreamCacheEntries();
        var missing = UpstreamPackageCacheEntries
            .Where(entry => !entry.IsCached)
            .Select(entry => entry.Package.FileName)
            .ToArray();
        if (missing.Length > 0)
        {
            RefreshUpstreamActionAvailability($"Review blocked. Missing cached package(s): {string.Join(", ", missing)}.");
            AppendLog($"Aircraft package review blocked: missing cached package(s): {string.Join(", ", missing)}.");
            UpstreamFindings.ReplaceWith(missing.Select(name => $"Missing cached package: {name}"));
            return;
        }

        var result = _aircraftUpdateDryRunAnalyzer.Analyze(CurrentProductAircraftFolderPath(), UpstreamPackageCacheEntries);
        UpstreamDryRunSummary = result.Summary;
        UpstreamDryRunEntries.ReplaceWith(result.Entries);
        UpstreamFindings.ReplaceWith(result.Findings);
        RefreshUpstreamActionAvailability("Review complete. No aircraft files were changed.");
        AppendLog($"Aircraft package review: {result.Summary}");
    }

    [RelayCommand]
    private void ApplyAircraftUpdate()
    {
        if (IsOperationRunning)
        {
            return;
        }

        var selectedVariant = SelectedViewVariant;
        if (selectedVariant is null)
        {
            AppendLog("Apply aircraft update: blocked because no view variant is selected.");
            return;
        }

        if (_lastUpstreamUpdateCheck is null)
        {
            AppendLog("Apply aircraft update: blocked because no upstream package plan is available.");
            RefreshUpstreamActionAvailability("Apply blocked. Check for updates before applying cached packages.");
            return;
        }

        RefreshUpstreamCacheEntries();
        var updateCheck = _lastUpstreamUpdateCheck;
        var cacheEntries = UpstreamPackageCacheEntries.ToArray();
        RunAircraftUpdateAction(
            "Apply aircraft update",
            "Preparing aircraft update transaction",
            "Aircraft update applied",
            "Aircraft update blocked",
            selectedVariant,
            () => _aircraftUpdateOperation.Apply(selectedVariant, updateCheck, cacheEntries));
    }

    [RelayCommand]
    private void RestoreAircraftUpdate()
    {
        if (IsOperationRunning)
        {
            return;
        }

        var selectedVariant = SelectedViewVariant;
        if (selectedVariant is null)
        {
            AppendLog("Restore aircraft update: blocked because no view variant is selected.");
            return;
        }

        RunAircraftUpdateAction(
            "Restore aircraft update",
            "Preparing aircraft update restore",
            "Aircraft update restored",
            "Aircraft update restore blocked",
            selectedVariant,
            () => _aircraftUpdateOperation.RestoreLatest(selectedVariant));
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
            var viewResult = _viewAnalyzer.Analyze(SelectedAircraftPath);
            ApplyViewAnalysis(viewResult, selectedPath);
            ApplyManifest(SelectManifest(viewResult));
            ApplyAnalysis(_analyzer.Analyze(CurrentProductAircraftFolderPath(), _manifest));
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
    private void AdoptQuickViewBaseline()
    {
        if (IsOperationRunning)
        {
            return;
        }

        var selectedVariant = SelectedViewVariant;
        if (selectedVariant is null)
        {
            AppendLog("Adopt current Quick View baseline: blocked because no view variant is selected.");
            return;
        }

        RunViewMaintenanceAction(
            "Adopt current Quick View baseline",
            "Recording Quick View baseline",
            "Quick View baseline recorded",
            "Quick View baseline blocked",
            selectedVariant,
            () => _adoptQuickViewBaselineOperation.Adopt(selectedVariant),
            showRestartNoticeOnChanged: false);
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

    [RelayCommand]
    private void CreateConfigBackup()
    {
        if (IsOperationRunning)
        {
            return;
        }

        var selectedVariant = SelectedViewVariant;
        if (selectedVariant is null)
        {
            AppendLog("Create Config Backup: blocked because no view variant is selected.");
            return;
        }

        RunViewMaintenanceAction(
            "Create Config Backup",
            "Creating config backup",
            "Config backup created",
            "Config backup blocked",
            selectedVariant,
            () => _configBackupOperation.CreateBackup(selectedVariant),
            showRestartNoticeOnChanged: false);
    }

    [RelayCommand]
    private void RestoreConfigBackup()
    {
        if (IsOperationRunning)
        {
            return;
        }

        var selectedVariant = SelectedViewVariant;
        if (selectedVariant is null)
        {
            AppendLog("Restore Config Backup: blocked because no view variant is selected.");
            return;
        }

        RunViewMaintenanceAction(
            "Restore Config Backup",
            "Restoring config backup",
            "Config backup restored",
            "Config restore blocked",
            selectedVariant,
            () => _configBackupOperation.RestoreLatestConfigBackup(selectedVariant));
    }

    private void RunViewMaintenanceAction(
        string actionName,
        string preparingTitle,
        string successTitle,
        string blockedTitle,
        AircraftVariantViewAnalysis selectedVariant,
        Func<MaintenanceOperationResult> action,
        bool showRestartNoticeOnChanged = true)
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
            RestartNoticeVisible = result.Changed && showRestartNoticeOnChanged;

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
            ApplyViewAnalysis(viewResult, selectedPath);
            ApplyManifest(SelectManifest(viewResult));
            ApplyAnalysis(_analyzer.Analyze(CurrentProductAircraftFolderPath(), _manifest));
        }
    }

    private void RunAircraftUpdateAction(
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
        OperationSubtitle = $"Preparing aircraft package transaction for {selectedVariant.DisplayName}.";
        OperationProgressText = "0% - Validating target, cache, review and X-Plane process state";
        RestartNoticeVisible = false;

        IsOperationRunning = true;
        ActionsEnabled = false;
        var stopwatch = Stopwatch.StartNew();
        MaintenanceOperationResult? operationResult = null;
        try
        {
            var result = action();
            operationResult = result;
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
                ? result.Changed ? "100% - Aircraft update transaction completed and state recorded" : "100% - No file change required"
                : "0% - Transaction did not start or was rolled back";
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
            ApplyViewAnalysis(viewResult, selectedPath);
            ApplyManifest(SelectManifest(viewResult));
            ApplyAnalysis(_analyzer.Analyze(CurrentProductAircraftFolderPath(), _manifest));
            if (operationResult is not null)
            {
                UpstreamDryRunEntries.Clear();
                UpstreamDryRunSummary = operationResult.Changed
                    ? "Aircraft files changed. Check for updates to re-check the installed version."
                    : "No aircraft update file changes were applied.";
                RefreshUpstreamActionAvailability(operationResult.Changed
                    ? "Aircraft files changed. Check for updates to re-check the installed version."
                    : "No aircraft update file changes were applied.");
            }
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
            ApplyViewAnalysis(viewResult, selectedPath);
            ApplyManifest(SelectManifest(viewResult));
            ApplyAnalysis(_analyzer.Analyze(CurrentProductAircraftFolderPath(), _manifest));
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
        RefreshProductTargets(currentSelection);
        RefreshFilteredViewVariants(currentSelection);
    }

    partial void OnSelectedViewVariantChanged(AircraftVariantViewAnalysis? value)
    {
        SelectProductForVariant(value);
        ApplySelectedVariantReadiness(value);
        RefreshProductScopedPackageAnalysis();
    }

    private void RefreshProductTargets(string? preferredAcfPath = null)
    {
        var products = new[]
        {
            BuildProductTarget("zibo-737ng", "Zibo", preferredAcfPath),
            BuildProductTarget("levelup-737ng", "LevelUp", preferredAcfPath)
        };

        ProductTargets.ReplaceWith(products);
        var detectedProducts = products
            .Where(product => product.IsDetected)
            .ToArray();
        DetectedProductTargets.ReplaceWith(detectedProducts);
        ProductSelectorVisible = detectedProducts.Length > 1;
        FixedProductVisible = !ProductSelectorVisible;

        var selected = products.FirstOrDefault(product => product.HasSelection && product.IsDetected)
            ?? products.FirstOrDefault(product => string.Equals(product.Family, SelectedProduct?.Family, StringComparison.OrdinalIgnoreCase) && product.IsDetected)
            ?? detectedProducts.FirstOrDefault();
        if (!ReferenceEquals(SelectedProduct, selected))
        {
            SelectedProduct = selected;
        }
        else
        {
            RefreshSelectedProductSummary(selected);
        }
    }

    private void RefreshFilteredViewVariants(string? preferredAcfPath = null)
    {
        var selectedFamily = SelectedProduct?.IsDetected == true
            ? SelectedProduct.Family
            : null;
        var variants = string.IsNullOrWhiteSpace(selectedFamily)
            ? ViewVariants.ToArray()
            : ViewVariants
                .Where(variant => string.Equals(variant.Family, selectedFamily, StringComparison.OrdinalIgnoreCase))
                .ToArray();

        FilteredViewVariants.ReplaceWith(variants);

        var nextSelection = variants.FirstOrDefault(variant => string.Equals(variant.AcfPath, preferredAcfPath, StringComparison.Ordinal))
            ?? (SelectedViewVariant is not null && variants.Any(variant => ReferenceEquals(variant, SelectedViewVariant))
                ? SelectedViewVariant
                : null)
            ?? variants.FirstOrDefault();
        if (!ReferenceEquals(SelectedViewVariant, nextSelection))
        {
            SelectedViewVariant = nextSelection;
        }
        else
        {
            ApplySelectedVariantReadiness(nextSelection);
        }
    }

    private ProductTargetStatus BuildProductTarget(string family, string name, string? preferredAcfPath)
    {
        var variants = ViewVariants
            .Where(variant => string.Equals(variant.Family, family, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var selected = variants.FirstOrDefault(variant => string.Equals(variant.AcfPath, preferredAcfPath, StringComparison.Ordinal))
            ?? (SelectedViewVariant is not null && string.Equals(SelectedViewVariant.Family, family, StringComparison.OrdinalIgnoreCase)
                ? SelectedViewVariant
                : null);

        if (variants.Length == 0)
        {
            return new ProductTargetStatus(
                name,
                family,
                "Not detected",
                name == "Zibo"
                    ? "No Zibo 737-800X installation was detected in the selected folder."
                    : "No LevelUp 737NG installation was detected in the selected folder.",
                "-",
                "",
                selected is not null);
        }

        var folderPaths = variants
            .Select(GetAircraftFolderPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var selectedFolderPath = selected is null
            ? folderPaths.FirstOrDefault() ?? ""
            : GetAircraftFolderPath(selected);
        var detail = folderPaths.Length <= 1
            ? $"{variants.Length} supported variant(s) found."
            : $"{variants.Length} supported variant(s) found across {folderPaths.Length} installation folders.";

        return new ProductTargetStatus(
            name,
            family,
            "Detected",
            detail,
            string.Join(", ", variants.Select(variant => variant.DisplayName)),
            selectedFolderPath,
            selected is not null);
    }

    private void SelectProductForVariant(AircraftVariantViewAnalysis? variant)
    {
        if (variant is null)
        {
            ProductActionsEnabled = false;
            AircraftProductUpdateEnabled = false;
            return;
        }

        var product = ProductTargets.FirstOrDefault(item => string.Equals(item.Family, variant.Family, StringComparison.OrdinalIgnoreCase));
        if (product is not null && !ReferenceEquals(SelectedProduct, product))
        {
            SelectedProduct = product;
        }

        ProductActionsEnabled = ActionsEnabled && product is not null && product.IsDetected;
        AircraftProductUpdateEnabled = ProductActionsEnabled
            && string.Equals(product?.Family, "zibo-737ng", StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshSelectedProductSummary(ProductTargetStatus? product)
    {
        if (product is null)
        {
            SelectedProductName = "No supported product";
            SelectedProductDetail = "Select a Zibo or LevelUp installation folder.";
            SelectedProductVariants = "-";
            SelectedProductFolderPath = "-";
            ProductFolderVisible = false;
            ProductActionsEnabled = false;
            AircraftProductUpdateEnabled = false;
            return;
        }

        SelectedProductName = product.Name;
        SelectedProductDetail = product.Detail;
        SelectedProductVariants = product.Variants;
        SelectedProductFolderPath = string.IsNullOrWhiteSpace(product.AircraftFolderPath) ? "-" : product.AircraftFolderPath;
        ProductFolderVisible = product.IsDetected
            && !string.IsNullOrWhiteSpace(product.AircraftFolderPath)
            && !PathsEqual(product.AircraftFolderPath, SelectedAircraftPath);
        ProductActionsEnabled = ActionsEnabled && product.IsDetected;
        AircraftProductUpdateEnabled = ProductActionsEnabled
            && string.Equals(product.Family, "zibo-737ng", StringComparison.OrdinalIgnoreCase);
    }

    private string CurrentProductAircraftFolderPath()
    {
        if (!string.IsNullOrWhiteSpace(SelectedProduct?.AircraftFolderPath))
        {
            return SelectedProduct.AircraftFolderPath;
        }

        return SelectedViewVariant is null ? SelectedAircraftPath : GetAircraftFolderPath(SelectedViewVariant);
    }

    private static string GetAircraftFolderPath(AircraftVariantViewAnalysis variant)
    {
        return Path.GetDirectoryName(variant.AcfPath) ?? "";
    }

    private static bool PathsEqual(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(left, right, StringComparison.Ordinal);
        }
    }

    private void RefreshProductScopedPackageAnalysis()
    {
        ApplyManifest(SelectManifest(AircraftViewAnalysisResult.Empty()));
        ApplyAnalysis(_analyzer.Analyze(CurrentProductAircraftFolderPath(), _manifest));
    }

    private void ApplySelectedVariantReadiness(AircraftVariantViewAnalysis? variant)
    {
        ApplyQuickViewBaselineAssessment(variant);
        ApplyUpstreamReadiness(variant);
    }

    private void ApplyQuickViewBaselineAssessment(AircraftVariantViewAnalysis? variant)
    {
        var assessment = _quickViewBaselineAnalyzer.Assess(variant);
        QuickViewBaselineStatus = assessment.Status;
        QuickViewBaselineSource = FormatQuickViewBaselineSource(assessment.Source);
        QuickViewBaselineConfidence = assessment.Confidence.ToString();
        QuickViewBaselineDelta = FormatQuickViewBaselineDelta(assessment);
        QuickViewBaselineRecommendation = assessment.Recommendation;
        QuickViewBaselineDetail = assessment.Detail;
        CanAdoptQuickViewBaseline = ActionsEnabled && variant is not null && assessment.CanAdoptCurrent;
        CanAdaptQuickViewsForCg = ActionsEnabled && variant is not null && assessment.CanAdapt;
    }

    private void ApplyUpstreamReadiness(AircraftVariantViewAnalysis? variant)
    {
        UpstreamSource = ZiboUpstreamFeedParser.DefaultFeedUrl;
        UpstreamAvailableVersion = "-";
        UpstreamPlanAction = "Not checked";
        UpstreamUpdateMode = "-";
        UpstreamLastChecked = "Not checked";
        _lastUpstreamUpdateCheck = null;
        UpstreamRequiredPackages.Clear();
        UpstreamPackageCacheEntries.Clear();
        UpstreamDryRunEntries.Clear();
        UpstreamDryRunSummary = "No aircraft package review has been calculated.";
        RefreshUpstreamActionAvailability("Check for updates to enable package download or import.");

        if (variant is null)
        {
            UpstreamUpdateStatus = "No Zibo aircraft selected";
            UpstreamUpdateSummary = "Select a Zibo aircraft folder to check upstream aircraft packages.";
            UpstreamLocalVersion = "-";
            RefreshUpstreamActionAvailability("Select a Zibo aircraft folder and check for updates before importing packages.");
            UpstreamFindings.ReplaceWith(["The upstream aircraft package check is read-only."]);
            return;
        }

        UpstreamLocalVersion = variant.LocalVersion ?? "-";

        if (!string.Equals(variant.Family, "zibo-737ng", StringComparison.OrdinalIgnoreCase))
        {
            UpstreamUpdateStatus = "LU source not configured";
            UpstreamUpdateSummary = "LevelUp aircraft updates need a LevelUp package source. Embedded Zibomod updates are a separate layer and require a Zibo-declared LU-compatible package signal; normal Zibo packages are not applied directly to LevelUp.";
            RefreshUpstreamActionAvailability("LevelUp aircraft update is disabled until a LevelUp update source and Zibo LU-compatibility metadata are available.");
            UpstreamFindings.ReplaceWith([
                "LevelUp aircraft updates need their own package source.",
                "Embedded Zibomod updates require Zibo LU-compatibility metadata; normal Zibo upstream packages are not applied directly to LevelUp aircraft."
            ]);
            return;
        }

        UpstreamUpdateStatus = "Ready to check";
        UpstreamUpdateSummary = "Check for updates reads the Zibo feed and plans full-baseline/cumulative-patch requirements without changing files.";
        RefreshUpstreamActionAvailability("Click Check for updates to calculate required upstream packages.");
        UpstreamFindings.ReplaceWith(["No aircraft files will be downloaded, backed up, or changed by this check."]);
    }

    private void ApplyUpstreamUpdateCheck(AircraftUpstreamUpdateCheckResult result)
    {
        _lastUpstreamUpdateCheck = result;
        UpstreamUpdateStatus = result.StateLabel;
        UpstreamUpdateSummary = result.Summary;
        UpstreamLocalVersion = result.LocalVersionDisplay;
        UpstreamAvailableVersion = result.AvailableVersionDisplay;
        UpstreamPlanAction = result.ActionDisplay;
        UpstreamUpdateMode = FormatAircraftUpdateMode(result.UpdateMode);
        UpstreamSource = string.IsNullOrWhiteSpace(result.SourceUrl) ? ZiboUpstreamFeedParser.DefaultFeedUrl : result.SourceUrl;
        UpstreamLastChecked = DateTimeOffset.Now.ToString("HH:mm:ss");
        UpstreamRequiredPackages.ReplaceWith(result.RequiredPackages);
        RefreshUpstreamCacheEntries();
        UpstreamDryRunEntries.Clear();
        UpstreamDryRunSummary = result.IsCustomDistribution
            ? "Custom distribution detected. Official upstream packages are review-only for this target."
            : "No aircraft package review has been calculated.";
        RefreshUpstreamActionAvailability();
        UpstreamFindings.ReplaceWith(result.Findings);
    }

    private void RefreshUpstreamCacheEntries()
    {
        UpstreamPackageCacheEntries.ReplaceWith(_lastUpstreamUpdateCheck?.RequiredPackages.Select(_aircraftUpdatePackageCache.Inspect)
            ?? []);
        RefreshUpstreamActionAvailability();
    }

    partial void OnActionsEnabledChanged(bool value)
    {
        RefreshUpstreamActionAvailability();
        ApplyQuickViewBaselineAssessment(SelectedViewVariant);
        RefreshSelectedProductSummary(SelectedProduct);
    }

    private void RefreshUpstreamActionAvailability(string? statusOverride = null)
    {
        var selectedVariant = SelectedViewVariant;
        var aircraftUpdateSupported = selectedVariant is not null
            && string.Equals(selectedVariant.Family, "zibo-737ng", StringComparison.OrdinalIgnoreCase);
        var requiredPackages = _lastUpstreamUpdateCheck?.RequiredPackages ?? [];
        var hasRequiredPackages = requiredPackages.Count > 0;
        var isCustomDistribution = _lastUpstreamUpdateCheck?.IsCustomDistribution == true;
        var allRequiredPackagesCached = hasRequiredPackages
            && UpstreamPackageCacheEntries.Count == requiredPackages.Count
            && UpstreamPackageCacheEntries.All(entry => entry.IsCached);
        var dryRunHasBlockingEntries = UpstreamDryRunEntries.Any(entry => entry.Action is AircraftUpdateDryRunEntryAction.BlockedUnsafePath
            or AircraftUpdateDryRunEntryAction.BlockedInvalidPackage);

        CanImportAircraftUpdateZip = ActionsEnabled && aircraftUpdateSupported && hasRequiredPackages && !isCustomDistribution;
        CanDownloadAircraftUpdateZip = ActionsEnabled && aircraftUpdateSupported && hasRequiredPackages && !isCustomDistribution && !allRequiredPackagesCached;
        CanDryRunAircraftUpdateZip = ActionsEnabled && aircraftUpdateSupported && hasRequiredPackages && !isCustomDistribution && allRequiredPackagesCached;
        CanApplyAircraftUpdateZip = ActionsEnabled && aircraftUpdateSupported && hasRequiredPackages && !isCustomDistribution && allRequiredPackagesCached && !dryRunHasBlockingEntries;
        CanRestoreAircraftUpdate = ActionsEnabled && aircraftUpdateSupported;

        if (!string.IsNullOrWhiteSpace(statusOverride))
        {
            UpstreamActionStatus = statusOverride;
            return;
        }

        if (!ActionsEnabled)
        {
            UpstreamActionStatus = "Upstream package actions are disabled while another operation is running.";
            return;
        }

        if (_lastUpstreamUpdateCheck is null)
        {
            UpstreamActionStatus = "Check for updates before importing packages.";
            return;
        }

        if (isCustomDistribution)
        {
            UpstreamActionStatus = "Custom distribution detected. Official upstream package import is disabled; package information is review-only.";
            return;
        }

        if (!hasRequiredPackages)
        {
            UpstreamActionStatus = "No upstream package import is required by the current plan.";
            return;
        }

        if (!allRequiredPackagesCached)
        {
            var missing = UpstreamPackageCacheEntries
                .Where(entry => !entry.IsCached)
                .Select(entry => entry.Package.FileName)
                .ToArray();
            UpstreamActionStatus = $"Ready to download or import: {string.Join(", ", missing)}.";
            return;
        }

        if (dryRunHasBlockingEntries)
        {
            UpstreamActionStatus = "Review found blocked package entries. Apply is disabled until the package cache or plan is corrected.";
            return;
        }

        UpstreamActionStatus = "All required packages are cached. Review changes or apply with backup and rollback.";
    }

    private string BuildImportSuccessStatus(string importedFileName)
    {
        var missing = UpstreamPackageCacheEntries
            .Where(entry => !entry.IsCached)
            .Select(entry => entry.Package.FileName)
            .ToArray();

        return missing.Length == 0
            ? $"Imported {importedFileName}. All required packages are cached; review is now available."
            : $"Imported {importedFileName}. Still missing: {string.Join(", ", missing)}.";
    }

    private string BuildRequiredPackageList()
    {
        var required = _lastUpstreamUpdateCheck?.RequiredPackages.Select(package => package.FileName).ToArray() ?? [];
        return required.Length == 0 ? "no package" : string.Join(", ", required);
    }

    private static string FormatAircraftUpdateMode(AircraftUpdateMode mode) =>
        mode switch
        {
            AircraftUpdateMode.Full => "Full",
            AircraftUpdateMode.Incremental => "Incremental",
            _ => "-"
        };

    private static string FormatQuickViewBaselineSource(QuickViewBaselineSource source) =>
        source switch
        {
            LevelUp.NavTableUpdater.Core.Aircraft.QuickViewBaselineSource.StoredToolkitState => "Stored toolkit state",
            LevelUp.NavTableUpdater.Core.Aircraft.QuickViewBaselineSource.ReferenceCatalog => "Reference catalog",
            LevelUp.NavTableUpdater.Core.Aircraft.QuickViewBaselineSource.InferredCurrentDefaultView => "Current Default View/QV0",
            _ => "Unknown"
        };

    private static string FormatQuickViewBaselineDelta(QuickViewBaselineAssessment assessment)
    {
        if (assessment.BaselineYFeet is null
            || assessment.BaselineZFeet is null
            || assessment.DeltaYFeet is null
            || assessment.DeltaZFeet is null)
        {
            return "-";
        }

        const double feetToMeters = 0.3048;
        var deltaYMeters = assessment.DeltaYFeet.Value * feetToMeters;
        var deltaZMeters = assessment.DeltaZFeet.Value * feetToMeters;
        return $"Y {assessment.DeltaYFeet.Value:+0.000000;-0.000000;0.000000} ft / {deltaYMeters:+0.000000;-0.000000;0.000000} m, Z {assessment.DeltaZFeet.Value:+0.000000;-0.000000;0.000000} ft / {deltaZMeters:+0.000000;-0.000000;0.000000} m";
    }

    [RelayCommand]
    private void SaveBackupSettings()
    {
        SaveDirectorySetting(
            "Backup folder",
            BackupRootPath,
            fullPath => _settings.BackupRootPath = fullPath,
            fullPath =>
            {
                _stateStore.SetBackupRootPath(fullPath);
                BackupRootPath = fullPath;
            });
    }

    [RelayCommand]
    private void UseDefaultBackupSettings()
    {
        BackupRootPath = ToolkitPaths.DefaultBackupRootPath;
        SaveBackupSettings();
    }

    [RelayCommand]
    private void SaveAircraftUpdateCacheSettings()
    {
        SaveDirectorySetting(
            "Aircraft update cache folder",
            AircraftUpdateCacheRootPath,
            fullPath => _settings.AircraftUpdateCacheRootPath = fullPath,
            fullPath =>
            {
                _aircraftUpdatePackageCache = new AircraftUpdatePackageCache(fullPath);
                _aircraftUpdatePackageCache.EnsureRoot();
                AircraftUpdateCacheRootPath = _aircraftUpdatePackageCache.RootPath;
                UpstreamCacheRoot = _aircraftUpdatePackageCache.RootPath;
                RefreshUpstreamCacheEntries();
                UpstreamDryRunEntries.Clear();
                UpstreamDryRunSummary = "Cache folder changed. Review aircraft changes after required packages are cached.";
            });
    }

    [RelayCommand]
    private void UseDefaultAircraftUpdateCacheSettings()
    {
        AircraftUpdateCacheRootPath = ToolkitPaths.DefaultAircraftUpdateCacheRootPath;
        SaveAircraftUpdateCacheSettings();
    }

    [RelayCommand]
    private void ClearAircraftUpdateCache()
    {
        if (!ActionsEnabled || IsOperationRunning)
        {
            SettingsStatus = "Cache can be cleared after the current operation finishes.";
            return;
        }

        try
        {
            var removed = _aircraftUpdatePackageCache.Clear();
            RefreshUpstreamCacheEntries();
            UpstreamDryRunEntries.Clear();
            UpstreamDryRunSummary = "Aircraft update cache was cleared. Import required packages again before review.";
            RefreshUpstreamActionAvailability($"Aircraft update cache cleared. Removed {removed} top-level item(s).");
            SettingsStatus = $"Aircraft update cache cleared. Removed {removed} top-level item(s).";
            AppendLog($"Settings: aircraft update cache cleared at {_aircraftUpdatePackageCache.RootPath}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SettingsStatus = $"Aircraft update cache was not cleared: {ex.Message}";
            AppendLog($"Settings: aircraft update cache clear failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void SaveOfflinePackageSettings()
    {
        SaveDirectorySetting(
            "Offline VNAV package folder",
            OfflinePackageRootPath,
            fullPath => _settings.OfflinePackageRootPath = fullPath,
            fullPath =>
            {
                OfflinePackageRootPath = fullPath;
            });
    }

    [RelayCommand]
    private void UseDefaultOfflinePackageSettings()
    {
        OfflinePackageRootPath = ToolkitPaths.DefaultOfflinePackageRootPath;
        SaveOfflinePackageSettings();
    }

    [RelayCommand]
    private void SaveDiagnosticsExportSettings()
    {
        SaveDirectorySetting(
            "Diagnostics export folder",
            DiagnosticsExportRootPath,
            fullPath => _settings.DiagnosticsExportRootPath = fullPath,
            fullPath =>
            {
                DiagnosticsExportRootPath = fullPath;
            });
    }

    [RelayCommand]
    private void UseDefaultDiagnosticsExportSettings()
    {
        DiagnosticsExportRootPath = ToolkitPaths.DefaultDiagnosticsExportRootPath;
        SaveDiagnosticsExportSettings();
    }

    private void SaveDirectorySetting(
        string label,
        string requestedPath,
        Action<string> updateSettings,
        Action<string> applyRuntime)
    {
        if (!ActionsEnabled || IsOperationRunning)
        {
            SettingsStatus = "Settings can be changed after the current operation finishes.";
            return;
        }

        try
        {
            var fullPath = NormalizeUserPath(requestedPath);
            Directory.CreateDirectory(fullPath);
            VerifyWritableDirectory(fullPath);

            updateSettings(fullPath);
            _settingsStore.Save(_settings);
            applyRuntime(fullPath);
            SettingsStatus = $"{label} saved: {fullPath}";
            AppendLog($"Settings: {label} set to {fullPath}");
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            SettingsStatus = $"{label} was not changed: {ex.Message}";
            AppendLog($"Settings: {label} rejected: {ex.Message}");
        }
    }

    private void SaveSelectedAircraftPathSetting()
    {
        if (string.IsNullOrWhiteSpace(SelectedAircraftPath))
        {
            return;
        }

        try
        {
            var fullPath = NormalizeUserPath(SelectedAircraftPath);
            if (string.Equals(_settings.SelectedAircraftPath, fullPath, StringComparison.Ordinal))
            {
                SelectedAircraftPath = fullPath;
                return;
            }

            _settings.SelectedAircraftPath = fullPath;
            _settingsStore.Save(_settings);
            SelectedAircraftPath = fullPath;
            AppendLog($"Settings: selected aircraft folder saved: {fullPath}");
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            AppendLog($"Settings: selected aircraft folder was not saved: {ex.Message}");
        }
    }

    private static string NormalizeUserPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Backup folder is empty.");
        }

        var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        if (expanded == "~")
        {
            expanded = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        else if (expanded.StartsWith("~/", StringComparison.Ordinal) || expanded.StartsWith("~\\", StringComparison.Ordinal))
        {
            expanded = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                expanded[2..]);
        }

        return Path.GetFullPath(expanded);
    }

    private static void VerifyWritableDirectory(string directory)
    {
        var probePath = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(probePath, "ok");
        }
        finally
        {
            if (File.Exists(probePath))
            {
                File.Delete(probePath);
            }
        }
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
        var family = SelectedProduct?.IsDetected == true
            ? SelectedProduct.Family
            : SelectedViewVariant?.Family ?? viewResult.Variants.FirstOrDefault()?.Family;
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

    private IPackagePayloadSource CreatePayloadSource() =>
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

    private IEnumerable<string> BuildLocalPackageDirectories()
    {
        var explicitDirectory = Environment.GetEnvironmentVariable("XPLANE_737NG_PACKAGE_DIR");
        if (!string.IsNullOrWhiteSpace(explicitDirectory))
        {
            yield return explicitDirectory;
        }

        if (!string.IsNullOrWhiteSpace(_settings.OfflinePackageRootPath))
        {
            yield return _settings.OfflinePackageRootPath;
        }

        var contentDir = Path.Combine(AppContext.BaseDirectory, "Content");
        yield return contentDir;

        var sourceContentDir = Path.Combine(Environment.CurrentDirectory, "src", "LevelUp.NavTableUpdater.App", "Content");
        yield return sourceContentDir;
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

public sealed record ProductTargetStatus(
    string Name,
    string Family,
    string Status,
    string Detail,
    string Variants,
    string AircraftFolderPath,
    bool HasSelection)
{
    public bool IsDetected => !string.Equals(Status, "Not detected", StringComparison.OrdinalIgnoreCase);
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
