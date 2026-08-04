using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LevelUp.NavTableUpdater.App.Services;
using LevelUp.NavTableUpdater.Core;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Analysis;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.Detection;
using LevelUp.NavTableUpdater.Core.Manifest;
using LevelUp.NavTableUpdater.Core.Platform;
using LevelUp.NavTableUpdater.Core.Resources;
using LevelUp.NavTableUpdater.Core.State;
using LevelUp.NavTableUpdater.Core.Tools;
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
    private readonly DeclarativeContentPatchOperation _declarativeContentPatchOperation;
    private readonly AircraftUpstreamUpdateChecker _ziboUpdateChecker;
    private readonly LevelUpReleaseUpdateChecker _levelUpUpdateChecker;
    private readonly LevelUpAircraftUpdatePackageLoader _levelUpUpdatePackageLoader = new();
    private readonly AircraftUpdateOperation _aircraftUpdateOperation;
    private AircraftUpdatePackageCache _aircraftUpdatePackageCache;
    private readonly AircraftUpdateDryRunAnalyzer _aircraftUpdateDryRunAnalyzer = new();
    private readonly IUserInteractionService _userInteractionService;
    private readonly HttpClient _aircraftUpdateHttpClient = new();
    private readonly IPackageManifestSource _packageManifestSource = new GitHubReleasePackageManifestSource();
    private readonly IReadOnlyList<PackageManifest> _manifests;
    private readonly ContentPackageCatalog _contentPackageCatalog;
    private GitHubContentPatchReleaseSource _contentPatchReleaseSource;
    private GitHubToolPackageReleaseSource _toolPackageReleaseSource;
    private GitHubResourcePackageReleaseSource _resourcePackageReleaseSource;
    private readonly ToolPackageManager _toolPackageManager;
    private readonly ResourcePackageManager _resourcePackageManager;
    private readonly Dictionary<string, ContentPatchRelease> _contentPatchReleases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _contentPatchReleaseErrors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ToolPackageRelease> _toolPackageReleases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ResourcePackageRelease> _resourcePackageReleases = new(StringComparer.Ordinal);
    private bool _synchronizingToolSelection;
    private bool _synchronizingResourceSelection;
    private PackageManifest _manifest;
    private AircraftUpstreamUpdateCheckResult? _lastUpstreamUpdateCheck;
    private AircraftUpdateDryRunResult? _lastAircraftUpdateDryRun;
    private AircraftAnalysisResult? _lastAircraftAnalysis;
    private CancellationTokenSource? _operationCancellationSource;
    private Stopwatch? _operationElapsedStopwatch;
    private DispatcherTimer? _operationElapsedTimer;
    private bool _isInitialized;

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
    private bool canAutoDetect = true;

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
    private string installLog = "";

    [ObservableProperty]
    private bool actionsEnabled = true;

    [ObservableProperty]
    private bool productActionsEnabled;

    [ObservableProperty]
    private bool aircraftProductUpdateEnabled;

    [ObservableProperty]
    private bool unifiedUpdateVisible;

    [ObservableProperty]
    private bool operationPanelVisible;

    [ObservableProperty]
    private bool isOperationRunning;

    [ObservableProperty]
    private bool canCancelOperation;

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
    private bool canImportAircraftUpdatePackage;

    [ObservableProperty]
    private bool canDownloadAircraftUpdatePackage;

    [ObservableProperty]
    private bool canDryRunAircraftUpdatePackage;

    [ObservableProperty]
    private bool canApplyAircraftUpdatePackage;

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

    [ObservableProperty]
    private string optionalPatchPackagePath = "";

    [ObservableProperty]
    private string optionalPatchName = "No optional patch package selected";

    [ObservableProperty]
    private string optionalPatchStatus = "Select a declarative package folder containing package-manifest.json.";

    [ObservableProperty]
    private bool canRunOptionalPatch;

    [ObservableProperty]
    private string contentPackageCatalogStatus = "Select a supported product to view its managed content and optional patches.";

    [ObservableProperty]
    private bool isContentPackageCatalogCheckRunning;

    [ObservableProperty]
    private bool canCheckContentPackageCatalog;

    [ObservableProperty]
    private bool contentPackageOverviewVisible;

    [ObservableProperty]
    private bool toolPackageVisible;

    [ObservableProperty]
    private ContentPackageCatalogEntry? selectedToolPackage;

    [ObservableProperty]
    private string toolPackageName = "Yet Another Linda";

    [ObservableProperty]
    private string toolPackageDescription = "Virtual copilot and maintenance assistant for supported Zibo and LevelUp aircraft.";

    [ObservableProperty]
    private string selectedToolReleaseChannel = "stable";

    [ObservableProperty]
    private string toolInstalledVersion = "-";

    [ObservableProperty]
    private string toolAvailableVersion = "Not checked";

    [ObservableProperty]
    private string toolPackageStatus = "Select a supported product.";

    [ObservableProperty]
    private string toolXPlaneRoot = "-";

    [ObservableProperty]
    private string toolTargetPath = "-";

    [ObservableProperty]
    private string toolActionLabel = "Install";

    [ObservableProperty]
    private bool canCheckToolRelease;

    [ObservableProperty]
    private bool canRunToolPackage;

    [ObservableProperty]
    private bool canRestoreToolPackage;

    [ObservableProperty]
    private bool isToolPackageOperationRunning;

    [ObservableProperty]
    private bool resourcePackageVisible;

    [ObservableProperty]
    private ContentPackageCatalogEntry? selectedResourcePackage;

    [ObservableProperty]
    private string resourcePackageName = "Resource";

    [ObservableProperty]
    private string resourcePackageDescription = "Optional verified download for the selected product.";

    [ObservableProperty]
    private string selectedResourceReleaseChannel = "stable";

    [ObservableProperty]
    private IReadOnlyList<string> resourceReleaseChannelOptions = ["stable"];

    [ObservableProperty]
    private string resourceDownloadedVersion = "-";

    [ObservableProperty]
    private string resourceAvailableVersion = "Not checked";

    [ObservableProperty]
    private string resourcePackageStatus = "Select a supported product.";

    [ObservableProperty]
    private string resourceDestinationPath = "";

    [ObservableProperty]
    private string resourceFilePath = "-";

    [ObservableProperty]
    private string resourceActionLabel = "Download";

    [ObservableProperty]
    private bool canCheckResourceRelease;

    [ObservableProperty]
    private bool canDownloadResourcePackage;

    [ObservableProperty]
    private bool canVerifyResourcePackage;

    [ObservableProperty]
    private bool canOpenResourceFolder;

    [ObservableProperty]
    private bool canRemoveResourcePackage;

    [ObservableProperty]
    private bool isResourcePackageOperationRunning;

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

    public ObservableCollection<AvailableContentPackageStatus> AvailableContentPackages { get; } = [];

    public ObservableCollection<ContentPackageCatalogEntry> AvailableToolPackages { get; } = [];

    public ObservableCollection<ContentPackageCatalogEntry> AvailableResourcePackages { get; } = [];

    public IReadOnlyList<string> ToolReleaseChannelOptions { get; } = ["stable", "beta"];

    public MainWindowViewModel()
        : this(RejectingUserInteractionService.Instance)
    {
    }

    public MainWindowViewModel(IUserInteractionService userInteractionService)
    {
        _userInteractionService = userInteractionService ?? throw new ArgumentNullException(nameof(userInteractionService));
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
        _contentPackageCatalog = LoadContentPackageCatalog();
        _contentPatchReleaseSource = new GitHubContentPatchReleaseSource(
            _aircraftUpdateHttpClient,
            _aircraftUpdatePackageCache.RootPath);
        _toolPackageReleaseSource = new GitHubToolPackageReleaseSource(
            _aircraftUpdateHttpClient,
            _aircraftUpdatePackageCache.RootPath);
        _resourcePackageReleaseSource = new GitHubResourcePackageReleaseSource(
            _aircraftUpdateHttpClient);
        selectedToolReleaseChannel = "stable";
        selectedResourceReleaseChannel = "stable";
        _manifest = _manifests[0];
        _quickViewBaselineAnalyzer = new QuickViewBaselineAnalyzer(_stateStore);
        _applyDefaultViewOperation = new ApplyDefaultViewFromQv0Operation(_stateStore);
        _applyQuickViewCgAdaptOperation = new ApplyQuickViewCgAdaptOperation(_stateStore, _quickViewBaselineAnalyzer);
        _adoptQuickViewBaselineOperation = new AdoptQuickViewBaselineOperation(_stateStore);
        _configBackupOperation = new ConfigBackupOperation(_stateStore);
        _restoreLatestBackupOperation = new RestoreLatestBackupOperation(_stateStore);
        _vnavContentOperation = new VnavContentOperation(_stateStore, CreatePayloadSource());
        _declarativeContentPatchOperation = new DeclarativeContentPatchOperation(_stateStore);
        _toolPackageManager = new ToolPackageManager(_stateStore);
        _resourcePackageManager = new ResourcePackageManager(_stateStore);
        _ziboUpdateChecker = new AircraftUpstreamUpdateChecker(
            new ZiboFeedAircraftUpdateIndexSource(_aircraftUpdateHttpClient));
        var toolkitVersion = typeof(MainWindowViewModel).Assembly.GetName().Version
            ?? throw new InvalidOperationException("Toolkit assembly version is unavailable.");
        _levelUpUpdateChecker = new LevelUpReleaseUpdateChecker(
            new LevelUpGitHubReleaseIndexSource(_aircraftUpdateHttpClient, toolkitVersion));
        _aircraftUpdateOperation = new AircraftUpdateOperation(_stateStore, _aircraftUpdateDryRunAnalyzer);
        ApplyManifest(_manifest);
        ApplyAnalysis(AircraftAnalysisResult.Empty(_manifest.PackageVersion));
        ApplyViewAnalysis(AircraftViewAnalysisResult.Empty());
        AppendLog("Toolkit started. VNAV package and view-maintenance actions can write after validation and backup.");
        AppendLog($"Loaded {_manifests.Count} bundled manifest(s). Active: {_manifest.PackageId} {_manifest.PackageVersion}.");
        AppendLog($"Settings loaded. Backup folder: {_stateStore.BackupRootPath}");
        AppendLog($"Settings loaded. Downloaded package cache: {_aircraftUpdatePackageCache.RootPath}");
        AppendLog($"Loaded content package catalog {_contentPackageCatalog.CatalogVersion} with {_contentPackageCatalog.Packages.Count} package(s).");
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

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        await AutoDetect();
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

    public void SetOptionalPatchPackagePathFromBrowse(string path)
    {
        OptionalPatchPackagePath = path;
        RefreshOptionalPatchStatus();
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

    public async Task ImportAircraftUpdatePackageAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            RefreshUpstreamActionAvailability("Package selection canceled. No package was imported.");
            return;
        }

        if (SelectedViewVariant is not null
            && string.Equals(SelectedViewVariant.Family, LevelUpAircraftUpdatePackageLoader.Family, StringComparison.OrdinalIgnoreCase))
        {
            await ImportLevelUpAircraftUpdatePackageAsync(path, SelectedViewVariant);
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

        var cancellationToken = BeginPackageImport($"Importing {fileName}");
        try
        {
            var imported = await Task.Run(
                () => _aircraftUpdatePackageCache.ImportPackage(path, expectedPackage, cancellationToken),
                cancellationToken);
            CanCancelOperation = false;
            await RefreshUpstreamCacheEntriesAsync();
            _lastAircraftUpdateDryRun = null;
            UpstreamDryRunEntries.Clear();
            UpstreamDryRunSummary = "Package cache changed. Review aircraft changes before applying.";
            RefreshUpstreamActionAvailability(BuildImportSuccessStatus(imported.Package.FileName));
            AppendLog($"Imported aircraft package into cache: {imported.Package.FileName} ({imported.SizeBytes} bytes, sha256 {imported.Sha256}).");
            CompletePackageImport("Aircraft update package imported", "The package was copied to the toolkit cache and verified.");
        }
        catch (OperationCanceledException)
        {
            RefreshUpstreamActionAvailability("Package import canceled. No aircraft files were changed.");
            AppendLog("Aircraft package import canceled before any aircraft files were changed.");
            UpstreamFindings.ReplaceWith(["Aircraft package import canceled. No aircraft files were changed."]);
            CancelPackageImport();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        {
            RefreshUpstreamActionAvailability($"Import failed. {ex.Message}");
            AppendLog($"Aircraft package import failed: {ex.Message}");
            UpstreamFindings.ReplaceWith(["Aircraft package import failed.", ex.Message]);
            FailPackageImport("Aircraft package import failed", ex.Message);
        }
        finally
        {
            StopOperationElapsedTimer();
            EndCancellableOperation();
            IsOperationRunning = false;
            ActionsEnabled = true;
        }
    }

    private async Task ImportLevelUpAircraftUpdatePackageAsync(string path, AircraftVariantViewAnalysis variant)
    {
        var cancellationToken = BeginPackageImport($"Importing {Path.GetFileName(path)}");
        try
        {
            var selection = await Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var loaded = _levelUpUpdatePackageLoader.Load(path, variant);
                    cancellationToken.ThrowIfCancellationRequested();
                    return loaded;
                },
                cancellationToken);
            ApplyUpstreamUpdateCheck(selection.UpdateCheck);
            if (selection.Package is null || string.IsNullOrWhiteSpace(selection.ArchivePath))
            {
                RefreshUpstreamActionAvailability(selection.UpdateCheck.Summary);
                AppendLog($"LevelUp package plan: {selection.UpdateCheck.StateLabel} - {selection.UpdateCheck.Summary}");
                CompletePackageImport("LevelUp package reviewed", selection.UpdateCheck.Summary);
                return;
            }

            var imported = await Task.Run(
                () => _aircraftUpdatePackageCache.ImportPackage(selection.ArchivePath, selection.Package, cancellationToken),
                cancellationToken);
            CanCancelOperation = false;
            await RefreshUpstreamCacheEntriesAsync();
            _lastAircraftUpdateDryRun = null;
            UpstreamDryRunEntries.Clear();
            UpstreamDryRunSummary = "LevelUp package imported and verified. Review aircraft changes before applying.";
            RefreshUpstreamActionAvailability(BuildImportSuccessStatus(imported.Package.FileName));
            AppendLog($"Imported LevelUp aircraft package and manifest: {imported.Package.FileName} ({imported.SizeBytes} bytes, sha256 {imported.Sha256}).");
            CompletePackageImport("LevelUp update package imported", "The manifest and archive were copied to the toolkit cache and verified.");
        }
        catch (OperationCanceledException)
        {
            RefreshUpstreamActionAvailability("LevelUp package import canceled. No aircraft files were changed.");
            AppendLog("LevelUp aircraft package import canceled before any aircraft files were changed.");
            UpstreamFindings.ReplaceWith(["LevelUp package import canceled. No aircraft files were changed."]);
            CancelPackageImport();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        {
            RefreshUpstreamActionAvailability($"LevelUp package import failed. {ex.Message}");
            AppendLog($"LevelUp aircraft package import failed: {ex.Message}");
            UpstreamFindings.ReplaceWith(["LevelUp aircraft package import failed.", ex.Message]);
            FailPackageImport("LevelUp package import failed", ex.Message);
        }
        finally
        {
            StopOperationElapsedTimer();
            EndCancellableOperation();
            IsOperationRunning = false;
            ActionsEnabled = true;
        }
    }

    private CancellationToken BeginPackageImport(string title)
    {
        OperationPanelVisible = true;
        OperationLog = "";
        OperationElapsed = "00:00s";
        OperationProgress = 20;
        OperationStatus = "Import in progress";
        OperationTitle = title;
        OperationSubtitle = "Copying the selected package to the toolkit cache and verifying its integrity.";
        OperationProgressText = "20% - Reading, copying and hashing the package archive";
        IsOperationRunning = true;
        ActionsEnabled = false;
        StartOperationElapsedTimer();
        return BeginCancellableOperation();
    }

    private void CompletePackageImport(string title, string message)
    {
        OperationProgress = 100;
        OperationStatus = "Import complete";
        OperationTitle = title;
        OperationSubtitle = message;
        OperationProgressText = "100% - Package import and cache validation completed";
    }

    private void FailPackageImport(string title, string message)
    {
        OperationProgress = 0;
        OperationStatus = "Failed";
        OperationTitle = title;
        OperationSubtitle = message;
        OperationProgressText = "0% - No aircraft files were changed";
    }

    private void CancelPackageImport()
    {
        OperationProgress = 0;
        OperationStatus = "Canceled";
        OperationTitle = "Package import canceled";
        OperationSubtitle = "No aircraft files were changed.";
        OperationProgressText = "0% - Import canceled before aircraft update review";
    }

    [RelayCommand]
    private async Task DownloadAircraftUpdatePackages()
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

        await RefreshUpstreamCacheEntriesAsync();
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
        OperationPanelVisible = true;
        OperationLog = "";
        OperationElapsed = "00:00s";
        OperationProgress = 10;
        OperationStatus = "Download in progress";
        OperationTitle = "Downloading aircraft update";
        OperationSubtitle = $"Downloading {missingPackages.Length} required package(s) into the toolkit cache.";
        OperationProgressText = "10% - Downloading and validating package archives";
        var stopwatch = StartOperationElapsedTimer();
        var cancellationToken = BeginCancellableOperation();
        RefreshUpstreamActionAvailability($"Downloading {missingPackages.Length} required package(s) into the aircraft update cache.");
        UpstreamFindings.ReplaceWith(["Downloading required aircraft packages. No aircraft files are changed."]);

        try
        {
            foreach (var package in missingPackages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AppendLog($"Downloading aircraft package: {package.FileName}");
                var downloaded = await _aircraftUpdatePackageCache.DownloadAsync(package, _aircraftUpdateHttpClient, cancellationToken);
                AppendLog($"Downloaded aircraft package into cache: {downloaded.Package.FileName} ({downloaded.SizeBytes} bytes, sha256 {downloaded.Sha256}).");
            }

            await RefreshUpstreamCacheEntriesAsync();
            _lastAircraftUpdateDryRun = null;
            UpstreamDryRunEntries.Clear();
            UpstreamDryRunSummary = "Package cache changed. Review aircraft changes before applying.";
            RefreshUpstreamActionAvailability("Download complete. Review aircraft changes or apply the cached packages.");
            UpstreamFindings.ReplaceWith(["Required package download completed. No aircraft files were changed."]);
            OperationElapsed = FormatElapsed(stopwatch.Elapsed);
            OperationProgress = 100;
            OperationStatus = "Download complete";
            OperationTitle = "Aircraft update packages ready";
            OperationSubtitle = "Required packages were downloaded and verified in the toolkit cache.";
            OperationProgressText = "100% - Download and cache validation completed";
        }
        catch (OperationCanceledException)
        {
            RefreshUpstreamActionAvailability("Download canceled. No aircraft files were changed.");
            UpstreamFindings.ReplaceWith(["Aircraft package download canceled. No aircraft files were changed."]);
            AppendLog("Aircraft package download canceled.");
            OperationElapsed = FormatElapsed(stopwatch.Elapsed);
            OperationProgress = 0;
            OperationStatus = "Canceled";
            OperationTitle = "Aircraft package download canceled";
            OperationSubtitle = "No aircraft files were changed.";
            OperationProgressText = "0% - Download canceled before aircraft update review";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            RefreshUpstreamActionAvailability($"Download failed. Import the exact package manually if the source does not expose a direct archive URL. {ex.Message}");
            UpstreamFindings.ReplaceWith([
                "Aircraft package download failed.",
                "The source may expose torrent links or a cloud-drive flow instead of direct archive downloads.",
                ex.Message
            ]);
            AppendLog($"Aircraft package download failed: {ex.Message}");
        }
        finally
        {
            StopOperationElapsedTimer();
            EndCancellableOperation();
            IsUpstreamCheckRunning = false;
            ActionsEnabled = true;
            await RefreshUpstreamCacheEntriesAsync();
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
            RefreshContentPackageOverview();
            RefreshToolPackageOverview();
            RefreshResourcePackageOverview();
            return;
        }

        if (!value.IsDetected)
        {
            RefreshFilteredViewVariants();
            RefreshContentPackageOverview();
            RefreshToolPackageOverview();
            RefreshResourcePackageOverview();
            return;
        }

        RefreshFilteredViewVariants(SelectedViewVariant?.AcfPath);
        RefreshProductScopedPackageAnalysis();
        RefreshContentPackageOverview();
        RefreshToolPackageOverview();
        RefreshResourcePackageOverview();
    }

    [RelayCommand]
    private async Task AutoDetect()
    {
        if (!CanAutoDetect)
        {
            return;
        }

        CanAutoDetect = false;
        var preferredPath = SelectedAircraftPath;
        SelectedCandidate = null;
        DetectedTargets.Clear();
        DetectedTargetsVisible = false;

        var additionalRoots = string.IsNullOrWhiteSpace(SelectedAircraftPath)
            ? []
            : new[] { SelectedAircraftPath };
        AppendLog("Auto-detection started.");

        IReadOnlyList<AircraftCandidate> candidates;
        try
        {
            candidates = await Task.Run(() => _detector.FindCandidates(additionalRoots));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            AppendLog($"Auto-detection failed: {ex.Message}");
            return;
        }
        finally
        {
            CanAutoDetect = true;
        }

        foreach (var candidate in candidates)
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
        var preferredCandidate = DetectedTargets.FirstOrDefault(candidate => PathsEqual(candidate.Path, preferredPath));
        if (preferredCandidate is not null)
        {
            SelectedCandidate = preferredCandidate;
            return;
        }

        if (!string.IsNullOrWhiteSpace(preferredPath) && Directory.Exists(preferredPath))
        {
            Scan();
            return;
        }

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
            var product = AircraftProductIdentity.FromVariant(selectedVariant);
            var restoreResult = RunViewMaintenanceAction(
                "Restore VNAV backup",
                "Preparing VNAV restore transaction",
                "VNAV backup restored",
                "VNAV restore blocked",
                selectedVariant,
                () => _vnavContentOperation.RestoreLatest(selectedVariant, _manifest),
                targetDisplayName: product.DisplayName);
            await ShowUpdateResultAsync(selectedVariant, aircraftResult: null, vnavResult: restoreResult);
            return;
        }

        if (!TryParseContentAction(action, out var contentAction))
        {
            AppendLog($"{action}: unknown VNAV action.");
            return;
        }

        var resultAfterAction = await RunVnavContentAction(contentAction, selectedVariant);
        await ShowUpdateResultAsync(selectedVariant, aircraftResult: null, vnavResult: resultAfterAction);
    }

    [RelayCommand]
    private async Task RunOptionalPatchAction(string action)
    {
        if (IsOperationRunning)
        {
            return;
        }

        var isRestore = string.Equals(action, "Restore", StringComparison.OrdinalIgnoreCase);
        var patchAction = ContentPatchAction.Update;
        if (!isRestore && !Enum.TryParse(action, ignoreCase: true, out patchAction))
        {
            return;
        }

        var operationName = isRestore ? "Restore" : patchAction.ToString();
        var selectedVariant = SelectedViewVariant;
        if (selectedVariant is null || string.IsNullOrWhiteSpace(OptionalPatchPackagePath))
        {
            AppendLog("Optional patch blocked: select a LevelUp variant and a declarative package folder first.");
            return;
        }

        DeclarativePatchPackage package;
        try
        {
            package = DeclarativePatchPackageLoader.LoadDirectory(OptionalPatchPackagePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            OptionalPatchStatus = ex.Message;
            AppendLog($"Optional patch package rejected: {ex.Message}");
            return;
        }

        var confirmation = new ConfirmationRequest(
            $"{operationName} optional patch?",
            string.Join(
                Environment.NewLine,
                [
                    $"Package: {package.Manifest.PackageId} {package.Manifest.PackageVersion}",
                    $"Aircraft: {selectedVariant.DisplayName}",
                    $"Files: {package.Manifest.Targets.Count}",
                    "",
                    "This is an explicit optional transaction. Payloads are hash-validated; targets are hash- or structurally validated and backed up before any file is changed."
                ]),
            operationName,
            "Cancel");
        if (!await _userInteractionService.ConfirmAsync(confirmation))
        {
            AppendLog($"Optional patch {operationName} canceled before validation and file writes.");
            return;
        }

        OperationPanelVisible = true;
        OperationLog = "";
        OperationElapsed = "00:00s";
        OperationProgress = 0;
        OperationStatus = "Transaction in progress";
        OperationTitle = $"{OptionalPatchName} {operationName}";
        OperationSubtitle = "Validating declarative targets and preparing a multi-file transaction.";
        OperationProgressText = "0% - Validating package, source hashes and operation handlers";
        IsOperationRunning = true;
        ActionsEnabled = false;
        CanRunOptionalPatch = false;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = isRestore
                ? await Task.Run(() =>
                    _declarativeContentPatchOperation.Restore(
                        selectedVariant,
                        OptionalPatchPackagePath))
                : await Task.Run(async () =>
                    await _declarativeContentPatchOperation.RunAsync(
                        patchAction,
                        selectedVariant,
                        OptionalPatchPackagePath));
            foreach (var line in result.Log)
            {
                AppendOperationLog(line);
            }

            OperationElapsed = FormatElapsed(stopwatch.Elapsed);
            OperationStatus = result.Status;
            OperationTitle = result.Succeeded
                ? result.Changed ? $"{OptionalPatchName} {operationName} complete" : $"{OptionalPatchName} unchanged"
                : $"{OptionalPatchName} {operationName} blocked";
            OperationSubtitle = result.Message;
            OperationProgress = result.Succeeded ? 100 : 0;
            OperationProgressText = result.Succeeded
                ? result.Changed ? "100% - Optional patch transaction completed" : "100% - No file change required"
                : "0% - Transaction did not start";
            AppendLog($"Optional patch {operationName}: {result.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            OperationElapsed = FormatElapsed(stopwatch.Elapsed);
            OperationStatus = "Failed";
            OperationTitle = $"{OptionalPatchName} {operationName} failed";
            OperationSubtitle = ex.Message;
            OperationProgress = 0;
            OperationProgressText = "0% - Transaction failed and was rolled back";
            AppendOperationLog($"[FAILED] {ex.Message}");
            AppendLog($"Optional patch {patchAction} failed: {ex.Message}");
        }
        finally
        {
            IsOperationRunning = false;
            ActionsEnabled = true;
            RefreshOptionalPatchStatus();
            RefreshContentPackageOverview();
        }
    }

    [RelayCommand]
    private async Task ReviewOptionalPatch()
    {
        var selectedVariant = SelectedViewVariant;
        if (IsOperationRunning || !CanRunOptionalPatch || selectedVariant is null)
        {
            return;
        }

        IsOperationRunning = true;
        ActionsEnabled = false;
        CanRunOptionalPatch = false;
        OperationPanelVisible = true;
        OperationLog = "";
        OperationTitle = $"Reviewing {OptionalPatchName}";
        OperationSubtitle = "Calculating the declarative file plan without changing aircraft files.";
        OperationStatus = "Dry-run in progress";
        OperationProgress = 0;
        OperationProgressText = "0% - Validating source hashes and generating target bytes";
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var plan = await Task.Run(async () =>
                await _declarativeContentPatchOperation.PlanAsync(
                    ContentPatchAction.Update,
                    selectedVariant,
                    OptionalPatchPackagePath));
            foreach (var line in plan.Log)
            {
                AppendOperationLog(line);
            }

            foreach (var mutation in plan.Mutations)
            {
                AppendOperationLog($"[DRY-RUN] {mutation.Kind} {mutation.RelativePath}: {mutation.Description}");
            }

            OperationElapsed = FormatElapsed(stopwatch.Elapsed);
            OperationStatus = plan.IsSafe ? "Dry-run complete" : "Review required";
            OperationTitle = plan.IsSafe ? $"{OptionalPatchName} review complete" : $"{OptionalPatchName} review blocked";
            OperationSubtitle = plan.StatusMessage;
            OperationProgress = plan.IsSafe ? 100 : 0;
            OperationProgressText = plan.IsSafe
                ? $"100% - {plan.Mutations.Count} target file(s) planned; no files changed"
                : "0% - No file transaction is allowed";
            AppendLog($"Optional patch dry-run: {plan.StatusMessage}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            OperationElapsed = FormatElapsed(stopwatch.Elapsed);
            OperationStatus = "Failed";
            OperationTitle = $"{OptionalPatchName} review failed";
            OperationSubtitle = ex.Message;
            OperationProgress = 0;
            OperationProgressText = "0% - Dry-run failed; no aircraft files were changed";
            AppendOperationLog($"[FAILED] {ex.Message}");
        }
        finally
        {
            IsOperationRunning = false;
            ActionsEnabled = true;
            RefreshOptionalPatchStatus();
        }
    }

    [RelayCommand]
    private async Task CheckContentPackageCatalog()
    {
        var productId = SelectedProduct?.IsDetected == true ? SelectedProduct.Family : null;
        if (IsContentPackageCatalogCheckRunning || string.IsNullOrWhiteSpace(productId))
        {
            return;
        }

        var onlinePackages = _contentPackageCatalog.ForProduct(productId)
            .Where(package => package.Distribution.Kind is ContentPackageDistributionKind.GitHubReleaseArchive)
            .ToArray();
        if (onlinePackages.Length == 0)
        {
            ContentPackageCatalogStatus = "This product has no optional GitHub release packages to check.";
            return;
        }

        IsContentPackageCatalogCheckRunning = true;
        CanCheckContentPackageCatalog = false;
        ContentPackageCatalogStatus = $"Checking {onlinePackages.Length} optional package release(s). No aircraft files are changed.";
        var succeeded = 0;
        foreach (var package in onlinePackages)
        {
            try
            {
                var release = await _contentPatchReleaseSource.GetLatestAsync(package);
                _contentPatchReleases[package.PackageId] = release;
                _contentPatchReleaseErrors.Remove(package.PackageId);
                succeeded++;
                AppendLog($"Content catalog: {package.DisplayName} latest stable release is {release.Tag}.");
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
            {
                _contentPatchReleaseErrors[package.PackageId] = ex.Message;
                AppendLog($"Content catalog check failed for {package.DisplayName}: {ex.Message}");
            }
        }

        IsContentPackageCatalogCheckRunning = false;
        ContentPackageCatalogStatus = succeeded == onlinePackages.Length
            ? $"Optional package releases checked: {succeeded}/{onlinePackages.Length}."
            : $"Optional package release checks succeeded: {succeeded}/{onlinePackages.Length}. See package status or Advanced log for details.";
        RefreshContentPackageOverview(preserveStatus: true);
    }

    public async Task ReviewCatalogPatchAsync(AvailableContentPackageStatus item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!await PrepareCatalogPatchPackageAsync(item))
        {
            return;
        }

        await ReviewOptionalPatch();
    }

    public async Task ApplyCatalogPatchAsync(AvailableContentPackageStatus item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!await PrepareCatalogPatchPackageAsync(item))
        {
            return;
        }

        var action = item.ActionLabel switch
        {
            "Install" => ContentPatchAction.Install.ToString(),
            "Repair" => ContentPatchAction.Repair.ToString(),
            _ => ContentPatchAction.Update.ToString()
        };
        await RunOptionalPatchAction(action);
    }

    public async Task RestoreCatalogPatchAsync(AvailableContentPackageStatus item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (IsOperationRunning || !item.CanRestore || SelectedViewVariant is not { } selectedVariant)
        {
            return;
        }

        var productId = SelectedProduct?.IsDetected == true ? SelectedProduct.Family : null;
        var catalogEntry = string.IsNullOrWhiteSpace(productId)
            ? null
            : _contentPackageCatalog.ForProduct(productId)
                .SingleOrDefault(package => package.PackageId.Equals(item.PackageId, StringComparison.Ordinal));
        if (catalogEntry is null || catalogEntry.Category is not ContentPackageCategory.OptionalPatch)
        {
            ContentPackageCatalogStatus = "Optional patch restore blocked: select the matching product installation.";
            return;
        }

        var confirmation = new ConfirmationRequest(
            $"Restore {catalogEntry.DisplayName}?",
            string.Join(
                Environment.NewLine,
                [
                    $"Aircraft: {AircraftProductIdentity.FromVariant(selectedVariant).DisplayName}",
                    $"Component: {catalogEntry.DisplayName}",
                    "",
                    "The Toolkit will restore the exact pre-installation files only when every current target still matches its recorded installed hash."
                ]),
            "Restore",
            "Cancel");
        if (!await _userInteractionService.ConfirmAsync(confirmation))
        {
            AppendLog($"Optional patch restore canceled for {catalogEntry.DisplayName}.");
            return;
        }

        OperationPanelVisible = true;
        OperationLog = "";
        OperationElapsed = "00:00s";
        OperationProgress = 0;
        OperationStatus = "Restore in progress";
        OperationTitle = $"Restoring {catalogEntry.DisplayName}";
        OperationSubtitle = "Validating recorded targets and exact pre-installation backups.";
        OperationProgressText = "0% - Validating restore state";
        IsOperationRunning = true;
        ActionsEnabled = false;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var descriptor = ContentPatchCatalog.OptionalPatch(catalogEntry);
            var result = await Task.Run(() =>
                _declarativeContentPatchOperation.Restore(descriptor, selectedVariant));
            foreach (var line in result.Log)
            {
                AppendOperationLog(line);
            }

            OperationElapsed = FormatElapsed(stopwatch.Elapsed);
            OperationStatus = result.Status;
            OperationTitle = result.Succeeded
                ? $"{catalogEntry.DisplayName} restore complete"
                : $"{catalogEntry.DisplayName} restore blocked";
            OperationSubtitle = result.Message;
            OperationProgress = result.Succeeded ? 100 : 0;
            OperationProgressText = result.Succeeded
                ? "100% - Exact pre-installation files restored"
                : "0% - Restore did not change aircraft files";
            AppendLog($"Optional patch restore: {result.Message}");
        }
        finally
        {
            IsOperationRunning = false;
            ActionsEnabled = true;
            RefreshOptionalPatchStatus();
            RefreshContentPackageOverview();
        }
    }

    public async Task RemoveCatalogPatchAsync(AvailableContentPackageStatus item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.CanRemove || !await PrepareCatalogPatchPackageAsync(item))
        {
            return;
        }

        await RunOptionalPatchAction(ContentPatchAction.Uninstall.ToString());
    }

    private async Task<bool> PrepareCatalogPatchPackageAsync(AvailableContentPackageStatus item)
    {
        if (IsOperationRunning || IsContentPackageCatalogCheckRunning || !item.IsOptional)
        {
            return false;
        }

        var productId = SelectedProduct?.IsDetected == true ? SelectedProduct.Family : null;
        var catalogEntry = string.IsNullOrWhiteSpace(productId)
            ? null
            : _contentPackageCatalog.ForProduct(productId)
                .SingleOrDefault(package => package.PackageId.Equals(item.PackageId, StringComparison.Ordinal));
        if (catalogEntry is null
            || catalogEntry.Distribution.Kind is not ContentPackageDistributionKind.GitHubReleaseArchive
            || SelectedViewVariant is null
            || !catalogEntry.SupportedProducts.Contains(SelectedViewVariant.Family, StringComparer.Ordinal))
        {
            ContentPackageCatalogStatus = "Optional package action blocked: select a compatible product and aircraft variant.";
            RefreshContentPackageOverview(preserveStatus: true);
            return false;
        }

        OperationPanelVisible = true;
        OperationLog = "";
        OperationElapsed = "00:00s";
        OperationProgress = 10;
        OperationStatus = "Package download in progress";
        OperationTitle = $"Preparing {catalogEntry.DisplayName}";
        OperationSubtitle = "Resolving the trusted GitHub release and validating its archive.";
        OperationProgressText = "10% - Reading release metadata";
        IsOperationRunning = true;
        IsContentPackageCatalogCheckRunning = true;
        ActionsEnabled = false;
        var stopwatch = Stopwatch.StartNew();
        var cancellationToken = BeginCancellableOperation();
        try
        {
            var source = _contentPatchReleaseSource;
            var release = _contentPatchReleases.GetValueOrDefault(catalogEntry.PackageId);
            var provisioned = await Task.Run(
                async () =>
                {
                    release ??= await source.GetLatestAsync(catalogEntry, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    return await source.ProvisionAsync(catalogEntry, release, cancellationToken);
                },
                cancellationToken);
            _contentPatchReleases[catalogEntry.PackageId] = provisioned.Release;
            _contentPatchReleaseErrors.Remove(catalogEntry.PackageId);
            OptionalPatchPackagePath = provisioned.PackageDirectory;
            RefreshOptionalPatchStatus();
            OperationElapsed = FormatElapsed(stopwatch.Elapsed);
            OperationProgress = 100;
            OperationStatus = "Package ready";
            OperationTitle = $"{catalogEntry.DisplayName} package ready";
            OperationSubtitle = $"Release {provisioned.Release.Tag} was verified and prepared for review.";
            OperationProgressText = "100% - Release asset, manifest and payload hashes validated";
            AppendOperationLog($"[RELEASE] {provisioned.Release.Tag} from {catalogEntry.RepositoryUrl}");
            AppendOperationLog($"[PACKAGE] {provisioned.Package.Manifest.PackageId} {provisioned.Package.Manifest.PackageVersion}");
            AppendOperationLog($"[CACHE] {provisioned.PackageDirectory}");
            AppendLog($"Prepared optional package {catalogEntry.DisplayName} {provisioned.Release.Tag} from its trusted GitHub release.");
            return true;
        }
        catch (OperationCanceledException)
        {
            OperationElapsed = FormatElapsed(stopwatch.Elapsed);
            OperationProgress = 0;
            OperationStatus = "Canceled";
            OperationTitle = "Optional package preparation canceled";
            OperationSubtitle = "No aircraft files were changed.";
            OperationProgressText = "0% - Download or validation canceled";
            ContentPackageCatalogStatus = "Optional package preparation canceled. No aircraft files were changed.";
            AppendLog("Optional package preparation canceled.");
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _contentPatchReleaseErrors[catalogEntry.PackageId] = ex.Message;
            OperationElapsed = FormatElapsed(stopwatch.Elapsed);
            OperationProgress = 0;
            OperationStatus = "Failed";
            OperationTitle = $"{catalogEntry.DisplayName} package rejected";
            OperationSubtitle = ex.Message;
            OperationProgressText = "0% - No aircraft files were changed";
            ContentPackageCatalogStatus = $"Optional package could not be prepared: {ex.Message}";
            AppendOperationLog($"[FAILED] {ex.Message}");
            AppendLog($"Optional package preparation failed for {catalogEntry.DisplayName}: {ex.Message}");
            return false;
        }
        finally
        {
            EndCancellableOperation();
            IsContentPackageCatalogCheckRunning = false;
            IsOperationRunning = false;
            ActionsEnabled = true;
            RefreshContentPackageOverview(preserveStatus: true);
        }
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

        var reuseImportedPlan = false;
        if (_lastUpstreamUpdateCheck is not null)
        {
            await RefreshUpstreamCacheEntriesAsync();
            reuseImportedPlan = AircraftUpdatePlanReusePolicy.CanReuseValidatedLocalPlan(
                _lastUpstreamUpdateCheck,
                UpstreamPackageCacheEntries);
        }

        if (reuseImportedPlan)
        {
            RefreshUpstreamActionAvailability("Using the imported and verified LevelUp update package.");
            AppendLog("Unified update: using the imported LevelUp package plan without replacing it with an online release check.");
        }
        else
        {
            await RefreshAircraftUpdateCheck();
        }

        MaintenanceOperationResult? aircraftResult = null;
        var updateCheck = _lastUpstreamUpdateCheck;
        if (updateCheck is null)
        {
            var vnavOnlyResult = await OfferVnavFollowUpAsync(aircraftUpdateCompleted: false);
            if (SelectedViewVariant is { } vnavOnlyVariant && vnavOnlyResult is not null)
            {
                await ShowUpdateResultAsync(vnavOnlyVariant, aircraftResult: null, vnavResult: vnavOnlyResult);
            }

            return;
        }

        if (updateCheck.IsCustomDistribution)
        {
            return;
        }

        if (updateCheck.RequiredPackages.Count > 0)
        {
            if (CanDownloadAircraftUpdatePackage)
            {
                await DownloadAircraftUpdatePackages();
            }

            if (!CanDryRunAircraftUpdatePackage)
            {
                AppendLog("Unified update stopped before VNAV follow-up because the required aircraft package is unavailable or invalid.");
                return;
            }

            await RunAircraftUpdateReviewAsync();
            if (!CanApplyAircraftUpdatePackage)
            {
                AppendLog("Unified update stopped before VNAV follow-up because the aircraft package review did not produce an applicable plan.");
                return;
            }

            aircraftResult = await ConfirmAndApplyAircraftUpdateAsync(
                offerVnavFollowUp: false,
                showResultDialog: false);
            if (aircraftResult is null || !aircraftResult.Succeeded)
            {
                if (aircraftResult is not null && SelectedViewVariant is { } blockedVariant)
                {
                    await ShowUpdateResultAsync(blockedVariant, aircraftResult, vnavResult: null);
                }

                return;
            }
        }
        else
        {
            aircraftResult = MaintenanceOperationResult.NoChange(
                "The aircraft package is already current.",
                ["[NO-CHANGE] No aircraft update package is required by the current plan."]);
        }

        var vnavResult = await OfferVnavFollowUpAsync(aircraftResult.Changed);
        if (SelectedViewVariant is { } completedVariant)
        {
            await ShowUpdateResultAsync(completedVariant, aircraftResult, vnavResult);
        }
    }

    [RelayCommand]
    private async Task RefreshAircraftUpdateCheck()
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
        _lastAircraftUpdateDryRun = null;
        var isLevelUp = string.Equals(
            selectedVariant?.Family,
            LevelUpAircraftUpdatePackageLoader.Family,
            StringComparison.OrdinalIgnoreCase);
        var productName = isLevelUp ? "LevelUp" : "Zibo";
        UpstreamUpdateStatus = $"Checking {productName} releases";
        UpstreamUpdateSummary = "Reading the public release index and planning full/cumulative package requirements.";
        UpstreamPlanAction = "Checking";
        UpstreamRequiredPackages.Clear();
        UpstreamPackageCacheEntries.Clear();
        UpstreamDryRunEntries.Clear();
        UpstreamDryRunSummary = "No aircraft package review has been calculated.";
        RefreshUpstreamActionAvailability("Checking aircraft package plan. Download and import are disabled while the feed is refreshed.");
        UpstreamFindings.ReplaceWith(["Read-only check in progress. No aircraft files will be changed."]);

        try
        {
            var result = isLevelUp
                ? await _levelUpUpdateChecker.CheckAsync(selectedVariant)
                : await _ziboUpdateChecker.CheckZiboAsync(selectedVariant);
            ApplyUpstreamUpdateCheck(result);
            AppendLog($"{productName} upstream check: {result.StateLabel} - {result.Summary}");
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException or TaskCanceledException or System.Xml.XmlException)
        {
            UpstreamUpdateStatus = isLevelUp ? "Online source unavailable" : "Feed check failed";
            UpstreamUpdateSummary = isLevelUp
                ? "No public LevelUp aircraft release is available yet. Import the supplied manifest or adjacent .7z package to continue offline."
                : ex.Message;
            UpstreamAvailableVersion = "-";
            UpstreamPlanAction = "Not checked";
            UpstreamUpdateMode = "-";
            UpstreamLastChecked = DateTimeOffset.Now.ToString("HH:mm:ss");
            UpstreamRequiredPackages.Clear();
            UpstreamPackageCacheEntries.Clear();
            UpstreamDryRunEntries.Clear();
            UpstreamDryRunSummary = "No aircraft package review has been calculated.";
            RefreshUpstreamActionAvailability(isLevelUp
                ? "Online LevelUp updates are not available yet. Use Import package for the supplied offline test package."
                : "Import unavailable. Upstream package check failed before a plan was available.");
            UpstreamFindings.ReplaceWith(isLevelUp
                ? [
                    "The public LevelUp release source is not available yet.",
                    "Use Import package and select either the supplied manifest or its adjacent .7z archive.",
                    $"Technical detail: {ex.Message}"
                ]
                : [
                    "Read-only check failed before a package plan could be built.",
                    ex.Message
                ]);
            AppendLog($"{productName} upstream check failed: {ex.Message}");
            RefreshUnifiedUpdateVisibility();
        }
        finally
        {
            IsUpstreamCheckRunning = false;
            ActionsEnabled = true;
        }
    }

    [RelayCommand]
    private async Task DryRunAircraftUpdate()
    {
        await RunAircraftUpdateReviewAsync();
    }

    private async Task<AircraftUpdateDryRunResult?> RunAircraftUpdateReviewAsync()
    {
        if (_lastUpstreamUpdateCheck is null)
        {
            RefreshUpstreamActionAvailability("Review blocked. Check for updates before reviewing aircraft package changes.");
            AppendLog("Aircraft package review blocked: check for updates first.");
            UpstreamFindings.ReplaceWith(["Check for updates before reviewing aircraft package changes."]);
            return null;
        }

        if (_lastUpstreamUpdateCheck.IsCustomDistribution)
        {
            RefreshUpstreamActionAvailability("Review blocked. Custom distributions use upstream package information as review-only.");
            AppendLog("Aircraft package review blocked: selected target is a custom distribution.");
            UpstreamFindings.ReplaceWith([
                "Custom distribution detected. Official upstream packages are review-only for this target.",
                "Use a normal upstream Zibo install for package import/review, or define a dedicated custom-port update source."
            ]);
            return null;
        }

        if (_lastUpstreamUpdateCheck.RequiredPackages.Count == 0)
        {
            AppendLog("Aircraft package review: no upstream packages are required by the current plan.");
            _lastAircraftUpdateDryRun = null;
            UpstreamDryRunEntries.Clear();
            UpstreamDryRunSummary = "No upstream package changes are required.";
            RefreshUpstreamActionAvailability("No upstream package review is required for this target.");
            return null;
        }

        var missing = UpstreamPackageCacheEntries
            .Where(entry => !entry.IsCached)
            .Select(entry => entry.Package.FileName)
            .ToArray();
        if (missing.Length > 0)
        {
            RefreshUpstreamActionAvailability($"Review blocked. Missing cached package(s): {string.Join(", ", missing)}.");
            AppendLog($"Aircraft package review blocked: missing cached package(s): {string.Join(", ", missing)}.");
            UpstreamFindings.ReplaceWith(missing.Select(name => $"Missing cached package: {name}"));
            return null;
        }

        var aircraftFolder = CurrentProductAircraftFolderPath();
        var cacheEntries = UpstreamPackageCacheEntries.ToArray();
        _lastAircraftUpdateDryRun = null;
        UpstreamDryRunEntries.Clear();
        OperationPanelVisible = true;
        OperationLog = "";
        OperationElapsed = "00:00s";
        OperationProgress = 15;
        OperationStatus = "Review in progress";
        OperationTitle = "Reviewing aircraft update";
        OperationSubtitle = "Validating package contents, hashes and target paths. No aircraft files are changed.";
        OperationProgressText = "15% - Reading and verifying cached package contents";
        IsOperationRunning = true;
        ActionsEnabled = false;
        var stopwatch = StartOperationElapsedTimer();
        var cancellationToken = BeginCancellableOperation();
        try
        {
            var result = await Task.Run(
                () => _aircraftUpdateDryRunAnalyzer.Analyze(aircraftFolder, cacheEntries, cancellationToken),
                cancellationToken);
            _lastAircraftUpdateDryRun = result;
            UpstreamDryRunSummary = result.Summary;
            UpstreamDryRunEntries.ReplaceWith(result.Entries);
            UpstreamFindings.ReplaceWith(result.Findings);
            OperationElapsed = FormatElapsed(stopwatch.Elapsed);
            OperationProgress = 100;
            OperationStatus = result.Succeeded ? "Review complete" : "Review blocked";
            OperationTitle = result.Succeeded ? "Aircraft update reviewed" : "Aircraft update review blocked";
            OperationSubtitle = result.Summary;
            OperationProgressText = result.Succeeded
                ? "100% - Review completed; no aircraft files were changed"
                : "100% - Review found blocking package entries";
            RefreshUpstreamActionAvailability(result.Succeeded
                ? "Review complete. Confirm the reviewed changes before applying."
                : "Review found blocking package entries. Apply remains disabled.");
            AppendLog($"Aircraft package review: {result.Summary}");
            return result;
        }
        catch (OperationCanceledException)
        {
            UpstreamDryRunSummary = "Aircraft update review was canceled. No aircraft files were changed.";
            UpstreamFindings.ReplaceWith(["Review canceled before any aircraft files were changed."]);
            OperationElapsed = FormatElapsed(stopwatch.Elapsed);
            OperationProgress = 0;
            OperationStatus = "Canceled";
            OperationTitle = "Aircraft update review canceled";
            OperationSubtitle = "No aircraft files were changed.";
            OperationProgressText = "0% - Review canceled before the write phase";
            RefreshUpstreamActionAvailability("Review canceled. No aircraft files were changed.");
            AppendLog("Aircraft package review canceled before any aircraft files were changed.");
            return null;
        }
        finally
        {
            StopOperationElapsedTimer();
            EndCancellableOperation();
            IsOperationRunning = false;
            ActionsEnabled = true;
        }
    }

    [RelayCommand]
    private async Task ApplyAircraftUpdate()
    {
        await ConfirmAndApplyAircraftUpdateAsync();
    }

    private async Task<MaintenanceOperationResult?> ConfirmAndApplyAircraftUpdateAsync(
        bool offerVnavFollowUp = true,
        bool showResultDialog = true)
    {
        if (IsOperationRunning)
        {
            return null;
        }

        var selectedVariant = SelectedViewVariant;
        if (selectedVariant is null)
        {
            AppendLog("Apply aircraft update: blocked because no view variant is selected.");
            return null;
        }

        if (_lastUpstreamUpdateCheck is null)
        {
            AppendLog("Apply aircraft update: blocked because no upstream package plan is available.");
            RefreshUpstreamActionAvailability("Apply blocked. Check for updates before applying cached packages.");
            return null;
        }

        var dryRun = _lastAircraftUpdateDryRun ?? await RunAircraftUpdateReviewAsync();
        if (dryRun is null || !dryRun.Succeeded)
        {
            return null;
        }

        if (dryRun.AddCount + dryRun.ReplaceCount + dryRun.DeleteCount == 0)
        {
            RefreshUpstreamActionAvailability("Review found no aircraft package changes to apply.");
            AppendLog("Apply aircraft update skipped: review found no file changes.");
            var noChangeResult = MaintenanceOperationResult.NoChange(
                "No aircraft package changes need to be applied.",
                ["[NO-CHANGE] The confirmed dry-run contained no aircraft file changes."]);
            if (showResultDialog)
            {
                await ShowUpdateResultAsync(selectedVariant, noChangeResult, vnavResult: null);
            }

            return noChangeResult;
        }

        var confirmation = new ConfirmationRequest(
            "Apply aircraft update?",
            string.Join(
                Environment.NewLine,
                [
                    AircraftProductIdentity.FromVariant(selectedVariant).DisplayName,
                    $"Version: {UpstreamLocalVersion} -> {UpstreamAvailableVersion}",
                    $"Target: {CurrentProductAircraftFolderPath()}",
                    $"Changes: {dryRun.AddCount} add, {dryRun.ReplaceCount} replace, {dryRun.DeleteCount} delete",
                    $"Protected local entries: {dryRun.ProtectedCount + dryRun.LocalLiveryPreservedCount}",
                    "",
                    "Backups are created before existing aircraft files are replaced or deleted. Once writing starts, the transaction cannot be canceled and will either complete or roll back."
                ]),
            "Apply update");
        if (!await _userInteractionService.ConfirmAsync(confirmation))
        {
            RefreshUpstreamActionAvailability("Update canceled after review. No aircraft files were changed.");
            AppendLog("Apply aircraft update canceled at the confirmation step. No aircraft files were changed.");
            return null;
        }

        var updateCheck = _lastUpstreamUpdateCheck;
        var cacheEntries = UpstreamPackageCacheEntries.ToArray();
        var result = await RunAircraftUpdateAction(
            "Apply aircraft update",
            "Preparing aircraft update transaction",
            "Aircraft update applied",
            "Aircraft update blocked",
            selectedVariant,
            (cancellationToken, writePhaseStarting) => _aircraftUpdateOperation.Apply(
                selectedVariant,
                updateCheck,
                cacheEntries,
                cancellationToken,
                writePhaseStarting),
            canCancelBeforeWrite: true,
            markLevelUpUpdateComplete: true);
        MaintenanceOperationResult? vnavResult = null;
        if (result?.Changed == true && offerVnavFollowUp)
        {
            vnavResult = await OfferVnavFollowUpAsync(aircraftUpdateCompleted: true);
        }

        if (result is not null && showResultDialog)
        {
            await ShowUpdateResultAsync(selectedVariant, result, vnavResult);
        }

        return result;
    }

    [RelayCommand]
    private async Task RestoreAircraftUpdate()
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

        var result = await RunAircraftUpdateAction(
            "Restore aircraft update",
            "Preparing aircraft update restore",
            "Aircraft update restored",
            "Aircraft update restore blocked",
            selectedVariant,
            (_, _) => _aircraftUpdateOperation.RestoreLatest(selectedVariant),
            canCancelBeforeWrite: false,
            markLevelUpUpdateComplete: false);
        if (result is not null)
        {
            await ShowUpdateResultAsync(selectedVariant, result, vnavResult: null);
        }
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
        IsOperationRunning = true;
        ActionsEnabled = false;
        var stopwatch = StartOperationElapsedTimer();
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
            StopOperationElapsedTimer();
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
            () => _adoptQuickViewBaselineOperation.Adopt(selectedVariant));
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
            () => _configBackupOperation.CreateBackup(selectedVariant));
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

    private MaintenanceOperationResult RunViewMaintenanceAction(
        string actionName,
        string preparingTitle,
        string successTitle,
        string blockedTitle,
        AircraftVariantViewAnalysis selectedVariant,
        Func<MaintenanceOperationResult> action,
        string? targetDisplayName = null)
    {
        OperationPanelVisible = true;
        OperationLog = "";
        OperationElapsed = "00:00s";
        OperationProgress = 0;
        OperationStatus = "Transaction in progress";
        OperationTitle = preparingTitle;
        OperationSubtitle = $"Preparing transaction for {targetDisplayName ?? selectedVariant.DisplayName}.";
        OperationProgressText = "0% - Validating target and X-Plane process state";
        IsOperationRunning = true;
        ActionsEnabled = false;
        var stopwatch = StartOperationElapsedTimer();
        MaintenanceOperationResult operationResult;
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
                ? result.Changed ? "100% - Transaction completed and backup state recorded" : "100% - No file change required"
                : "0% - Transaction did not start";
            foreach (var backupPath in result.BackupPaths)
            {
                AppendLog($"{actionName}: backup created at {backupPath}");
            }

            AppendLog($"{actionName}: {result.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or FileNotFoundException)
        {
            operationResult = new MaintenanceOperationResult(
                Succeeded: false,
                Changed: false,
                Status: "Failed",
                Message: ex.Message,
                BackupPaths: [],
                Log: [$"[FAILED] {ex.Message}"]);
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
            StopOperationElapsedTimer();
            IsOperationRunning = false;
            ActionsEnabled = true;
            var selectedPath = selectedVariant.AcfPath;
            var viewResult = _viewAnalyzer.Analyze(SelectedAircraftPath);
            ApplyViewAnalysis(viewResult, selectedPath);
            ApplyManifest(SelectManifest(viewResult));
            ApplyAnalysis(_analyzer.Analyze(CurrentProductAircraftFolderPath(), _manifest));
        }

        return operationResult;
    }

    private async Task<MaintenanceOperationResult?> RunAircraftUpdateAction(
        string actionName,
        string preparingTitle,
        string successTitle,
        string blockedTitle,
        AircraftVariantViewAnalysis selectedVariant,
        Func<CancellationToken, Action, MaintenanceOperationResult> action,
        bool canCancelBeforeWrite,
        bool markLevelUpUpdateComplete)
    {
        OperationPanelVisible = true;
        OperationLog = "";
        OperationElapsed = "00:00s";
        OperationProgress = 0;
        OperationStatus = "Transaction in progress";
        OperationTitle = preparingTitle;
        OperationSubtitle = $"Preparing aircraft package transaction for {AircraftProductIdentity.FromVariant(selectedVariant).DisplayName}.";
        OperationProgressText = "0% - Validating target, cache, review and X-Plane process state";
        IsOperationRunning = true;
        ActionsEnabled = false;
        var stopwatch = StartOperationElapsedTimer();
        var cancellationToken = canCancelBeforeWrite ? BeginCancellableOperation() : CancellationToken.None;
        MaintenanceOperationResult? operationResult = null;
        var completedVersion = _lastUpstreamUpdateCheck?.AvailableVersionDisplay;
        var isLevelUpUpdateApply = markLevelUpUpdateComplete
            && string.Equals(
                selectedVariant.Family,
                LevelUpAircraftUpdatePackageLoader.Family,
                StringComparison.OrdinalIgnoreCase);

        void WritePhaseStarting()
        {
            if (!canCancelBeforeWrite)
            {
                return;
            }

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                CanCancelOperation = false;
                OperationProgress = 55;
                OperationStatus = "Writing aircraft files";
                OperationTitle = "Applying aircraft update";
                OperationSubtitle = "The write transaction is running and will complete or roll back.";
                OperationProgressText = "55% - Validation complete; creating backups and applying reviewed changes";
            }).GetAwaiter().GetResult();
        }

        try
        {
            var result = await Task.Run(
                () => action(cancellationToken, WritePhaseStarting),
                cancellationToken);
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
            if (result.Succeeded
                && result.Changed
                && isLevelUpUpdateApply)
            {
                var version = string.IsNullOrWhiteSpace(completedVersion) ? "the selected release" : completedVersion;
                var folderName = Path.GetFileName(
                    CurrentProductAircraftFolderPath().TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar));
                OperationSubtitle = $"{result.Message} Installed version: {version}. The existing aircraft folder '{folderName}' was intentionally retained.";
            }
            OperationProgress = result.Succeeded ? 100 : 0;
            OperationProgressText = result.Succeeded
                ? result.Changed ? "100% - Aircraft update transaction completed and state recorded" : "100% - No file change required"
                : "0% - Transaction did not start or was rolled back";
            foreach (var backupPath in result.BackupPaths)
            {
                AppendLog($"{actionName}: backup created at {backupPath}");
            }

            AppendLog($"{actionName}: {result.Message}");
        }
        catch (OperationCanceledException)
        {
            OperationElapsed = FormatElapsed(stopwatch.Elapsed);
            OperationStatus = "Canceled";
            OperationTitle = $"{actionName} canceled";
            OperationSubtitle = "Validation stopped before the aircraft write phase. No aircraft files were changed.";
            OperationProgress = 0;
            OperationProgressText = "0% - Canceled before backup and aircraft file writes";
            AppendOperationLog("[CANCELED] Validation stopped before the write phase. No aircraft files were changed.");
            AppendLog($"{actionName} canceled before any aircraft files were changed.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or FileNotFoundException)
        {
            operationResult = new MaintenanceOperationResult(
                Succeeded: false,
                Changed: false,
                Status: "Failed",
                Message: ex.Message,
                BackupPaths: [],
                Log: [$"[FAILED] {ex.Message}"]);
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
            StopOperationElapsedTimer();
            if (canCancelBeforeWrite)
            {
                EndCancellableOperation();
            }

            IsOperationRunning = false;
            ActionsEnabled = true;
            var selectedPath = selectedVariant.AcfPath;
            var viewResult = _viewAnalyzer.Analyze(SelectedAircraftPath);
            ApplyViewAnalysis(viewResult, selectedPath);
            ApplyManifest(SelectManifest(viewResult));
            ApplyAnalysis(_analyzer.Analyze(CurrentProductAircraftFolderPath(), _manifest));
            if (operationResult is not null)
            {
                _lastAircraftUpdateDryRun = null;
                UpstreamDryRunEntries.Clear();
                UpstreamDryRunSummary = operationResult.Changed
                    ? "Aircraft files changed. Check for updates to re-check the installed version."
                    : "No aircraft update file changes were applied.";
                if (operationResult.Changed
                    && isLevelUpUpdateApply
                    && _lastUpstreamUpdateCheck is { } completedCheck)
                {
                    var version = string.IsNullOrWhiteSpace(completedVersion) ? "the selected release" : completedVersion;
                    UpstreamUpdateStatus = "Update complete";
                    UpstreamUpdateSummary = $"LevelUp {version} was installed successfully. The existing aircraft folder name was retained.";
                    UpstreamLocalVersion = version;
                    UpstreamAvailableVersion = version;
                    UpstreamPlanAction = "No action";
                    UpstreamUpdateMode = "-";
                    UpstreamLastChecked = DateTimeOffset.Now.ToString("HH:mm:ss");
                    var completionFindings = new[]
                    {
                        $"Installed LevelUp version: {version}.",
                        "The aircraft folder name is an installation path and was intentionally not renamed."
                    };
                    _lastUpstreamUpdateCheck = completedCheck with
                    {
                        StateLabel = "Up to date",
                        Summary = UpstreamUpdateSummary,
                        LocalVersionDisplay = version,
                        AvailableVersionDisplay = version,
                        Action = AircraftUpdatePlanAction.UpToDate,
                        ActionDisplay = "No action",
                        RequiredPackages = [],
                        Findings = completionFindings
                    };
                    _lastAircraftUpdateDryRun = null;
                    UpstreamRequiredPackages.Clear();
                    UpstreamPackageCacheEntries.Clear();
                    UpstreamDryRunEntries.Clear();
                    UpstreamFindings.ReplaceWith(completionFindings);
                    UpstreamDryRunSummary = "Aircraft update completed. The existing aircraft folder name was retained.";
                    RefreshUpstreamActionAvailability("Aircraft update completed. No additional aircraft package action is required.");
                }
                else
                {
                    RefreshUpstreamActionAvailability(operationResult.Changed
                        ? "Aircraft files changed. Check for updates to re-check the installed version."
                        : "No aircraft update file changes were applied.");
                }
            }
        }

        return operationResult;
    }

    private async Task<MaintenanceOperationResult?> OfferVnavFollowUpAsync(bool aircraftUpdateCompleted)
    {
        var descriptor = ContentPatchCatalog.Vnav(_manifest.PackageId, _manifest.RepositoryUrl);
        if (!ContentPatchCatalog.MayOfferAfterAircraftUpdate(descriptor))
        {
            AppendLog($"Automatic follow-up is disabled by lifecycle policy for {descriptor.ComponentId}.");
            return null;
        }

        var analysis = _lastAircraftAnalysis;
        var selectedVariant = SelectedViewVariant;
        if (analysis is null || selectedVariant is null)
        {
            return null;
        }

        var action = analysis.State switch
        {
            InstallState.NotInstalled => VnavContentAction.Install,
            InstallState.RepairRequired => VnavContentAction.Repair,
            InstallState.OutdatedMarkedInstallation or InstallState.KnownLegacyInstallation => VnavContentAction.Update,
            _ => (VnavContentAction?)null
        };
        if (action is null || !analysis.IsSafeToPatch)
        {
            AppendLog($"VNAV follow-up not required or unavailable after aircraft update: {analysis.StateLabel}.");
            return null;
        }

        var confirmation = new ConfirmationRequest(
            "Update VNAV descent tables?",
            string.Join(
                Environment.NewLine,
                [
                    aircraftUpdateCompleted
                        ? $"The aircraft update completed for {AircraftProductIdentity.FromVariant(selectedVariant).DisplayName}."
                        : $"VNAV maintenance is available for {AircraftProductIdentity.FromVariant(selectedVariant).DisplayName} independently of the aircraft package source.",
                    $"VNAV status after rescan: {analysis.StateLabel}",
                    $"Recommended action: {action}",
                    "",
                    "VNAV content is installed as a separate manifest-controlled transaction with its own validation and backup."
                ]),
            "Update VNAV tables",
            "Not now");
        if (!await _userInteractionService.ConfirmAsync(confirmation))
        {
            AppendLog("VNAV maintenance action skipped.");
            return null;
        }

        return await RunVnavContentAction(action.Value, selectedVariant);
    }

    private async Task<MaintenanceOperationResult> RunVnavContentAction(
        VnavContentAction action,
        AircraftVariantViewAnalysis selectedVariant)
    {
        OperationPanelVisible = true;
        OperationLog = "";
        OperationElapsed = "00:00s";
        OperationProgress = 0;
        OperationStatus = "Transaction in progress";
        OperationTitle = $"VNAV {action} - Preparing transaction";
        OperationSubtitle = $"Preparing manifest transaction for {AircraftProductIdentity.FromVariant(selectedVariant).DisplayName}.";
        OperationProgressText = "0% - Validating target, X-Plane process state, manifest and payload source";
        IsOperationRunning = true;
        ActionsEnabled = false;
        var stopwatch = StartOperationElapsedTimer();
        MaintenanceOperationResult operationResult;
        try
        {
            var manifest = await ResolveManifestForActionAsync(_manifest);
            ApplyManifest(manifest);
            var result = await _vnavContentOperation.RunAsync(action, selectedVariant, manifest);
            operationResult = result;
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
            foreach (var backupPath in result.BackupPaths)
            {
                AppendLog($"VNAV {action}: backup created at {backupPath}");
            }

            AppendLog($"VNAV {action}: {result.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException)
        {
            operationResult = new MaintenanceOperationResult(
                Succeeded: false,
                Changed: false,
                Status: "Failed",
                Message: ex.Message,
                BackupPaths: [],
                Log: [$"[FAILED] {ex.Message}"]);
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
            StopOperationElapsedTimer();
            IsOperationRunning = false;
            ActionsEnabled = true;
            var selectedPath = selectedVariant.AcfPath;
            var viewResult = _viewAnalyzer.Analyze(SelectedAircraftPath);
            ApplyViewAnalysis(viewResult, selectedPath);
            ApplyManifest(SelectManifest(viewResult));
            ApplyAnalysis(_analyzer.Analyze(CurrentProductAircraftFolderPath(), _manifest));
        }

        return operationResult;
    }

    private async Task ShowUpdateResultAsync(
        AircraftVariantViewAnalysis selectedVariant,
        MaintenanceOperationResult? aircraftResult,
        MaintenanceOperationResult? vnavResult)
    {
        var results = new[] { aircraftResult, vnavResult }
            .Where(result => result is not null)
            .Cast<MaintenanceOperationResult>()
            .ToArray();
        if (results.Length == 0)
        {
            return;
        }

        var anyChanged = results.Any(result => result.Changed);
        var anyUnsuccessful = results.Any(result => !result.Succeeded);
        var blockedByXPlane = results.Any(result =>
            result.Message.Contains("X-Plane is running", StringComparison.OrdinalIgnoreCase));
        var title = anyUnsuccessful
            ? anyChanged
                ? "Update partially completed"
                : blockedByXPlane
                    ? "Close X-Plane"
                    : "Update could not be completed"
            : anyChanged
                ? "Update complete"
                : "Already up to date";

        var message = new List<string>
        {
            AircraftProductIdentity.FromVariant(selectedVariant).DisplayName
        };
        if (aircraftResult is { Changed: true }
            && !string.IsNullOrWhiteSpace(UpstreamAvailableVersion)
            && UpstreamAvailableVersion != "-")
        {
            message.Add($"Version: {UpstreamAvailableVersion}");
        }

        message.Add("");
        if (aircraftResult is not null)
        {
            message.Add($"Aircraft package: {FormatUpdateStepResult(aircraftResult)}");
        }

        if (vnavResult is not null)
        {
            message.Add($"VNAV descent tables: {FormatUpdateStepResult(vnavResult)}");
        }

        var backupCount = results
            .SelectMany(result => result.BackupPaths)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        if (backupCount > 0)
        {
            message.Add($"Backups created: {backupCount}");
        }

        message.Add("");
        if (blockedByXPlane)
        {
            message.Add(anyChanged
                ? "Some changes completed. Close X-Plane completely before retrying the remaining step."
                : "Close X-Plane completely, then run Update again.");
        }
        else if (anyUnsuccessful)
        {
            message.Add("Review the Advanced tab and operation log for details before retrying.");
        }
        else if (anyChanged)
        {
            message.Add("You can now start X-Plane to load the changes.");
        }
        else
        {
            message.Add("No aircraft files needed to be changed.");
        }

        await _userInteractionService.ShowMessageAsync(
            new MessageRequest(title, string.Join(Environment.NewLine, message)));
    }

    private static string FormatUpdateStepResult(MaintenanceOperationResult result)
    {
        if (!result.Succeeded)
        {
            return $"{result.Status} - {result.Message}";
        }

        return result.Changed
            ? $"{result.Status} - {result.Message}"
            : $"No change - {result.Message}";
    }

    private void ApplyAnalysis(AircraftAnalysisResult result)
    {
        _lastAircraftAnalysis = result;
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
        RefreshUnifiedUpdateVisibility();
        RefreshContentPackageOverview();
        RefreshToolPackageOverview();
        RefreshResourcePackageOverview();
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
        RefreshToolPackageOverview();
        RefreshResourcePackageOverview();
    }

    partial void OnSelectedViewVariantChanged(AircraftVariantViewAnalysis? value)
    {
        SelectProductForVariant(value);
        ApplySelectedVariantReadiness(value);
        RefreshProductScopedPackageAnalysis();
        RefreshOptionalPatchStatus();
        RefreshContentPackageOverview();
        RefreshToolPackageOverview();
        RefreshResourcePackageOverview();
    }

    private void RefreshOptionalPatchStatus()
    {
        CanRunOptionalPatch = false;
        if (string.IsNullOrWhiteSpace(OptionalPatchPackagePath))
        {
            OptionalPatchName = "No optional patch package selected";
            OptionalPatchStatus = "Select a declarative package folder containing package-manifest.json.";
            return;
        }

        try
        {
            var package = DeclarativePatchPackageLoader.LoadDirectory(OptionalPatchPackagePath);
            OptionalPatchName = package.Manifest.PackageId;
            var selectedVariant = SelectedViewVariant;
            if (selectedVariant is null)
            {
                OptionalPatchStatus = $"Package {package.Manifest.PackageVersion} is valid. Select a compatible aircraft variant.";
                return;
            }

            var supportedProducts = DeclarativePatchProductCompatibility.ResolveSupportedProducts(package.Manifest);
            if (!supportedProducts.Contains(selectedVariant.Family))
            {
                OptionalPatchStatus = $"Package supports [{string.Join(", ", supportedProducts)}]; selected product is {selectedVariant.Family}.";
                return;
            }

            var aircraftRoot = Path.GetDirectoryName(selectedVariant.AcfPath) ?? "";
            var state = _stateStore.TryGetContentInstallation(aircraftRoot)?.ContentComponents?
                .GetValueOrDefault(package.Manifest.PackageId);
            OptionalPatchStatus = state is null
                ? $"Package {package.Manifest.PackageVersion} is validated and ready for explicit installation ({package.Manifest.Targets.Count} files)."
                : $"Installed {state.PackageVersion}; selected package {package.Manifest.PackageVersion}.";
            CanRunOptionalPatch = ActionsEnabled && !IsOperationRunning;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            OptionalPatchName = "Invalid optional patch package";
            OptionalPatchStatus = ex.Message;
        }
    }

    private void RefreshContentPackageOverview(bool preserveStatus = false)
    {
        AvailableContentPackages.Clear();
        var product = SelectedProduct;
        if (product?.IsDetected != true)
        {
            ContentPackageOverviewVisible = false;
            CanCheckContentPackageCatalog = false;
            if (!preserveStatus)
            {
                ContentPackageCatalogStatus = "Select a supported product to view its managed content and optional patches.";
            }

            return;
        }

        var packages = _contentPackageCatalog.ForProduct(product.Family)
            .Where(package => package.Category is ContentPackageCategory.OptionalPatch)
            .ToArray();
        ContentPackageOverviewVisible = packages.Length > 0;
        var aircraftRoot = CurrentProductAircraftFolderPath();
        var installationState = string.IsNullOrWhiteSpace(aircraftRoot)
            ? null
            : _stateStore.TryGetContentInstallation(aircraftRoot);
        foreach (var package in packages)
        {
            var state = installationState?.ContentComponents.GetValueOrDefault(package.PackageId);
            var release = _contentPatchReleases.GetValueOrDefault(package.PackageId);
            var releaseError = _contentPatchReleaseErrors.GetValueOrDefault(package.PackageId);
            var isManaged = package.Category is ContentPackageCategory.ManagedContent;
            string installedVersion;
            string availableVersion;
            string status;
            string actionLabel;

            if (package.Distribution.Kind is ContentPackageDistributionKind.ExistingVnav)
            {
                var isActiveManifest = _manifest.PackageId.Equals(package.PackageId, StringComparison.Ordinal);
                installedVersion = isActiveManifest ? LocalPackageVersion : state?.PackageVersion ?? "-";
                availableVersion = isActiveManifest ? AvailablePackageVersion : "-";
                status = isActiveManifest ? AircraftStatus : "Managed by the product VNAV update workflow";
                actionLabel = "Managed in Updates";
            }
            else
            {
                installedVersion = string.IsNullOrWhiteSpace(state?.PackageVersion) ? "-" : state.PackageVersion;
                availableVersion = release?.Tag ?? "Not checked";
                if (!string.IsNullOrWhiteSpace(releaseError))
                {
                    status = $"Release check failed: {releaseError}";
                }
                else if (state is null)
                {
                    status = release is null ? "Optional; release not checked" : "Optional; not installed";
                }
                else if (release is null)
                {
                    status = "Installed; check the latest release before updating";
                }
                else
                {
                    status = ContentVersionsEqual(state.PackageVersion, release.Tag)
                        ? "Installed release is current; repair remains available"
                        : "A different release is available";
                }

                actionLabel = state is null
                    ? "Install"
                    : release is null ? "Install/update"
                    : ContentVersionsEqual(state.PackageVersion, release.Tag) ? "Repair" : "Update";
            }

            var canAct = !isManaged
                && ActionsEnabled
                && !IsOperationRunning
                && !IsContentPackageCatalogCheckRunning
                && SelectedViewVariant is not null
                && package.SupportedProducts.Contains(SelectedViewVariant.Family, StringComparer.Ordinal);
            var canRestore = canAct && state is not null;
            AvailableContentPackages.Add(new AvailableContentPackageStatus(
                package.PackageId,
                package.DisplayName,
                package.Description,
                isManaged ? "Managed content" : "Optional patch",
                installedVersion,
                availableVersion,
                status,
                package.RepositoryUrl,
                IsOptional: !isManaged,
                CanAct: canAct,
                actionLabel,
                CanRestore: canRestore,
                CanRemove: canRestore));
        }

        CanCheckContentPackageCatalog = ActionsEnabled
            && !IsOperationRunning
            && !IsContentPackageCatalogCheckRunning
            && packages.Any(package => package.Distribution.Kind is ContentPackageDistributionKind.GitHubReleaseArchive);
        if (!preserveStatus)
        {
            var optionalCount = packages.Count(package => package.Category is ContentPackageCategory.OptionalPatch);
            ContentPackageCatalogStatus = $"{packages.Length} package(s) available for {product.Name}: {optionalCount} optional.";
        }
    }

    private static bool ContentVersionsEqual(string left, string right) =>
        left.Trim().TrimStart('v', 'V').Equals(
            right.Trim().TrimStart('v', 'V'),
            StringComparison.OrdinalIgnoreCase);

    partial void OnSelectedToolPackageChanged(ContentPackageCatalogEntry? value)
    {
        if (_synchronizingToolSelection)
        {
            return;
        }

        var channel = value is null
            ? "stable"
            : NormalizeToolReleaseChannel(
                _settings.ToolReleaseChannels.GetValueOrDefault(value.PackageId, "stable"));
        _synchronizingToolSelection = true;
        try
        {
            SelectedToolReleaseChannel = channel;
        }
        finally
        {
            _synchronizingToolSelection = false;
        }

        RefreshToolPackageOverview();
    }

    partial void OnSelectedToolReleaseChannelChanged(string value)
    {
        if (_synchronizingToolSelection)
        {
            return;
        }

        var normalized = NormalizeToolReleaseChannel(value);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            SelectedToolReleaseChannel = normalized;
            return;
        }

        var entry = SelectedToolCatalogEntry();
        if (entry is null)
        {
            return;
        }

        _settings.ToolReleaseChannels[entry.PackageId] = normalized;
        _settingsStore.Save(_settings);
        RefreshToolPackageOverview();
    }

    [RelayCommand]
    private async Task CheckToolRelease()
    {
        var entry = SelectedToolCatalogEntry();
        if (entry is null || IsToolPackageOperationRunning || IsOperationRunning)
        {
            return;
        }

        IsToolPackageOperationRunning = true;
        ActionsEnabled = false;
        ToolPackageStatus = $"Checking the {SelectedToolReleaseChannel} {entry.DisplayName} release. No X-Plane files are changed.";
        var channel = ParseToolReleaseChannel();
        try
        {
            var release = await _toolPackageReleaseSource.GetLatestAsync(entry, channel);
            var key = ToolReleaseKey(entry.PackageId, channel);
            if (release is null)
            {
                _toolPackageReleases.Remove(key);
                ToolPackageStatus = $"No {SelectedToolReleaseChannel} release is currently available.";
                AppendLog($"Tool release check: no {SelectedToolReleaseChannel} release is available for {entry.DisplayName}.");
            }
            else
            {
                _toolPackageReleases[key] = release;
                ToolPackageStatus = $"{SelectedToolReleaseChannel} release {release.Manifest.PackageVersion} is available.";
                AppendLog($"Tool release check: {entry.DisplayName} {release.Manifest.PackageVersion} ({SelectedToolReleaseChannel}).");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or InvalidOperationException)
        {
            ToolPackageStatus = $"Tool release check failed: {ex.Message}";
            AppendLog($"Tool release check failed for {entry.DisplayName}: {ex.Message}");
        }
        finally
        {
            IsToolPackageOperationRunning = false;
            ActionsEnabled = true;
            RefreshToolPackageOverview(preserveStatus: true);
        }
    }

    [RelayCommand]
    private async Task RunToolPackage()
    {
        var entry = SelectedToolCatalogEntry();
        var xPlaneRoot = ResolveCurrentXPlaneRoot();
        var release = entry is null
            ? null
            : _toolPackageReleases.GetValueOrDefault(ToolReleaseKey(entry.PackageId, ParseToolReleaseChannel()));
        if (entry is null
            || release is null
            || string.IsNullOrWhiteSpace(xPlaneRoot)
            || IsToolPackageOperationRunning
            || IsOperationRunning)
        {
            return;
        }

        var inspection = _toolPackageManager.Inspect(entry, xPlaneRoot, release);
        var action = inspection.State switch
        {
            ToolPackageInstallState.NotInstalled => ToolPackageAction.Install,
            ToolPackageInstallState.UpdateAvailable or ToolPackageInstallState.InstalledVersionUnknown => ToolPackageAction.Update,
            ToolPackageInstallState.SelectedReleaseOlder => ToolPackageAction.SwitchChannel,
            ToolPackageInstallState.RepairRequired or ToolPackageInstallState.Current => ToolPackageAction.Repair,
            _ => (ToolPackageAction?)null
        };
        if (action is null)
        {
            ToolPackageStatus = $"Tool action is not available: {inspection.Status}.";
            return;
        }

        var verb = action.Value is ToolPackageAction.SwitchChannel
            ? "Switch channel"
            : action.Value.ToString();
        var confirmation = new ConfirmationRequest(
            $"{verb} {entry.DisplayName}?",
            $"{entry.DisplayName} {release.Manifest.PackageVersion} ({release.Manifest.Channel}) will be installed under:\n{xPlaneRoot}\n\nExisting tool files will be backed up. Manifest-protected and unowned local files will be preserved. X-Plane must be closed and restarted afterward.",
            verb);
        if (!await _userInteractionService.ConfirmAsync(confirmation))
        {
            ToolPackageStatus = $"{verb} canceled. No X-Plane files were changed.";
            AppendLog($"Tool package {verb.ToLowerInvariant()} canceled before download and file changes.");
            return;
        }

        IsToolPackageOperationRunning = true;
        ActionsEnabled = false;
        IsOperationRunning = true;
        OperationPanelVisible = true;
        OperationTitle = $"{verb} {entry.DisplayName}";
        OperationSubtitle = "Downloading and validating the official release package.";
        OperationProgress = 15;
        OperationProgressText = "15% - Verifying release metadata and package archive";
        OperationStatus = "Tool package in progress";
        OperationLog = "";
        var cancellationToken = BeginCancellableOperation();
        try
        {
            var source = _toolPackageReleaseSource;
            var provisioned = await Task.Run(
                async () => await source.ProvisionAsync(entry, release, cancellationToken),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            OperationProgress = 55;
            OperationSubtitle = $"Creating a backup and staging the verified {entry.DisplayName} installation.";
            OperationProgressText = $"55% - Backing up and staging {entry.DisplayName}";
            CanCancelOperation = false;
            var result = await Task.Run(
                () => _toolPackageManager.Apply(entry, provisioned, xPlaneRoot, action.Value));
            foreach (var line in result.Log)
            {
                AppendOperationLog(line);
            }

            OperationProgress = result.Succeeded ? 100 : 0;
            OperationStatus = result.Status;
            OperationTitle = result.Succeeded ? $"{entry.DisplayName} {result.Status.ToLowerInvariant()}" : $"{entry.DisplayName} blocked";
            OperationSubtitle = result.Message;
            OperationProgressText = result.Succeeded ? "100% - Tool package transaction completed" : "0% - No tool files were changed";
            ToolPackageStatus = result.Message;
            AppendLog($"Tool package {action}: {result.Message}");
        }
        catch (OperationCanceledException)
        {
            OperationProgress = 0;
            OperationStatus = "Canceled";
            OperationTitle = "Tool package operation canceled";
            OperationSubtitle = "No X-Plane plugin files were changed.";
            OperationProgressText = "0% - Canceled before the file transaction";
            ToolPackageStatus = "Operation canceled. No X-Plane plugin files were changed.";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            OperationProgress = 0;
            OperationStatus = "Failed";
            OperationTitle = $"{entry.DisplayName} operation failed";
            OperationSubtitle = ex.Message;
            OperationProgressText = "0% - The transaction failed and was rolled back";
            ToolPackageStatus = $"Tool operation failed: {ex.Message}";
            AppendLog($"Tool package operation failed: {ex.Message}");
        }
        finally
        {
            EndCancellableOperation();
            IsOperationRunning = false;
            IsToolPackageOperationRunning = false;
            ActionsEnabled = true;
            RefreshToolPackageOverview(preserveStatus: true);
        }
    }

    [RelayCommand]
    private async Task RestoreToolPackage()
    {
        var entry = SelectedToolCatalogEntry();
        var xPlaneRoot = ResolveCurrentXPlaneRoot();
        if (entry is null
            || string.IsNullOrWhiteSpace(xPlaneRoot)
            || !CanRestoreToolPackage
            || IsToolPackageOperationRunning
            || IsOperationRunning)
        {
            return;
        }

        var confirmation = new ConfirmationRequest(
            $"Restore {entry.DisplayName}?",
            $"The latest valid {entry.DisplayName} backup for this X-Plane installation will be restored. Restore is blocked if package-owned files changed afterward.\n\n{xPlaneRoot}",
            "Restore");
        if (!await _userInteractionService.ConfirmAsync(confirmation))
        {
            ToolPackageStatus = "Restore canceled. No X-Plane files were changed.";
            return;
        }

        IsToolPackageOperationRunning = true;
        IsOperationRunning = true;
        ActionsEnabled = false;
        OperationPanelVisible = true;
        OperationTitle = $"Restoring {entry.DisplayName}";
        OperationSubtitle = "Validating current files against the recorded installation state.";
        OperationProgress = 40;
        OperationProgressText = "40% - Validating restore guard and backup generation";
        OperationStatus = "Restore in progress";
        OperationLog = "";
        try
        {
            var result = await Task.Run(() => _toolPackageManager.Restore(entry, xPlaneRoot));
            foreach (var line in result.Log)
            {
                AppendOperationLog(line);
            }

            OperationProgress = result.Succeeded ? 100 : 0;
            OperationStatus = result.Status;
            OperationTitle = result.Succeeded ? $"{entry.DisplayName} restored" : $"{entry.DisplayName} restore blocked";
            OperationSubtitle = result.Message;
            OperationProgressText = result.Succeeded ? "100% - Restore transaction completed" : "0% - No tool files were changed";
            ToolPackageStatus = result.Message;
            AppendLog($"Tool package restore: {result.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            OperationProgress = 0;
            OperationStatus = "Failed";
            OperationTitle = $"{entry.DisplayName} restore failed";
            OperationSubtitle = ex.Message;
            OperationProgressText = "0% - Restore failed and the previous state was retained";
            ToolPackageStatus = $"Restore failed: {ex.Message}";
            AppendLog($"Tool package restore failed: {ex.Message}");
        }
        finally
        {
            IsOperationRunning = false;
            IsToolPackageOperationRunning = false;
            ActionsEnabled = true;
            RefreshToolPackageOverview(preserveStatus: true);
        }
    }

    private void RefreshToolPackageOverview(bool preserveStatus = false)
    {
        SynchronizeAvailableToolPackages();
        var entry = SelectedToolCatalogEntry();
        ToolPackageVisible = entry is not null;
        if (entry is null)
        {
            ToolInstalledVersion = "-";
            ToolAvailableVersion = "Not checked";
            ToolXPlaneRoot = "-";
            ToolTargetPath = "-";
            ToolPackageStatus = "Tools are available only for detected Zibo or LevelUp products.";
            CanCheckToolRelease = false;
            CanRunToolPackage = false;
            CanRestoreToolPackage = false;
            return;
        }

        ToolPackageName = entry.DisplayName;
        ToolPackageDescription = entry.Description;
        var channel = ParseToolReleaseChannel();
        var release = _toolPackageReleases.GetValueOrDefault(ToolReleaseKey(entry.PackageId, channel));
        var xPlaneRoot = ResolveCurrentXPlaneRoot();
        var inspection = _toolPackageManager.Inspect(entry, xPlaneRoot, release);
        ToolInstalledVersion = inspection.InstalledVersion;
        ToolAvailableVersion = inspection.AvailableVersion;
        ToolXPlaneRoot = string.IsNullOrWhiteSpace(inspection.XPlaneRoot) ? "Not resolved" : inspection.XPlaneRoot;
        ToolTargetPath = string.IsNullOrWhiteSpace(inspection.TargetPath) ? "Not resolved" : inspection.TargetPath;
        ToolActionLabel = inspection.State switch
        {
            ToolPackageInstallState.NotInstalled => "Install",
            ToolPackageInstallState.UpdateAvailable or ToolPackageInstallState.InstalledVersionUnknown => "Update",
            ToolPackageInstallState.SelectedReleaseOlder => $"Switch to {SelectedToolReleaseChannel}",
            _ => "Repair"
        };
        if (!preserveStatus)
        {
            ToolPackageStatus = inspection.Status;
        }

        CanCheckToolRelease = ActionsEnabled
            && !IsOperationRunning
            && !IsToolPackageOperationRunning
            && inspection.State is not ToolPackageInstallState.TargetUnavailable;
        CanRunToolPackage = CanCheckToolRelease
            && release is not null
            && inspection.State is ToolPackageInstallState.NotInstalled
                or ToolPackageInstallState.InstalledVersionUnknown
                or ToolPackageInstallState.UpdateAvailable
                or ToolPackageInstallState.SelectedReleaseOlder
                or ToolPackageInstallState.RepairRequired
                or ToolPackageInstallState.Current;
        var state = string.IsNullOrWhiteSpace(xPlaneRoot)
            ? null
            : _stateStore.TryGetToolInstallation(xPlaneRoot, entry.PackageId);
        CanRestoreToolPackage = CanCheckToolRelease
            && state?.Backups.Any(backup => !backup.SourceExisted || Directory.Exists(backup.BackupPath)) == true;
    }

    private ContentPackageCatalogEntry? SelectedToolCatalogEntry()
    {
        var product = SelectedProduct;
        var entry = SelectedToolPackage;
        return product?.IsDetected == true
            && entry?.Category is ContentPackageCategory.Tool
            && entry.SupportedProducts.Contains(product.Family, StringComparer.Ordinal)
                ? entry
                : null;
    }

    private void SynchronizeAvailableToolPackages()
    {
        var product = SelectedProduct;
        var tools = product?.IsDetected == true
            ? _contentPackageCatalog.ForProduct(product.Family)
                .Where(package => package.Category is ContentPackageCategory.Tool)
                .ToArray()
            : [];
        var selectedPackageId = SelectedToolPackage?.PackageId;
        var selected = tools.FirstOrDefault(package =>
                package.PackageId.Equals(selectedPackageId, StringComparison.Ordinal))
            ?? tools.FirstOrDefault();

        _synchronizingToolSelection = true;
        try
        {
            var toolListChanged = AvailableToolPackages.Count != tools.Length
                || AvailableToolPackages.Zip(tools).Any(pair =>
                    !pair.First.PackageId.Equals(pair.Second.PackageId, StringComparison.Ordinal));
            if (toolListChanged)
            {
                AvailableToolPackages.ReplaceWith(tools);
            }

            SelectedToolPackage = selected;
            var channel = selected is null
                ? "stable"
                : NormalizeToolReleaseChannel(
                    _settings.ToolReleaseChannels.GetValueOrDefault(selected.PackageId, "stable"));
            SelectedToolReleaseChannel = channel;
        }
        finally
        {
            _synchronizingToolSelection = false;
        }
    }

    partial void OnSelectedResourcePackageChanged(ContentPackageCatalogEntry? value)
    {
        if (_synchronizingResourceSelection)
        {
            return;
        }

        var channel = NormalizeResourceReleaseChannel(
            value,
            value is null
                ? "stable"
                : _settings.ToolReleaseChannels.GetValueOrDefault(value.PackageId, "stable"));
        _synchronizingResourceSelection = true;
        try
        {
            ResourceReleaseChannelOptions = ResourceReleaseChannels(value);
            SelectedResourceReleaseChannel = channel;
            ResourceDestinationPath = value is null
                ? ""
                : _stateStore.TryGetResourceInstallation(value.PackageId)?.DestinationDirectory ?? "";
        }
        finally
        {
            _synchronizingResourceSelection = false;
        }

        RefreshResourcePackageOverview();
    }

    partial void OnSelectedResourceReleaseChannelChanged(string value)
    {
        if (_synchronizingResourceSelection)
        {
            return;
        }

        var entry = SelectedResourceCatalogEntry();
        var normalized = NormalizeResourceReleaseChannel(entry, value);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            SelectedResourceReleaseChannel = normalized;
            return;
        }

        if (entry is null)
        {
            return;
        }

        _settings.ToolReleaseChannels[entry.PackageId] = normalized;
        _settingsStore.Save(_settings);
        RefreshResourcePackageOverview();
    }

    public void SetResourceDestinationPathFromBrowse(string path)
    {
        ResourceDestinationPath = Path.GetFullPath(path);
        ResourcePackageStatus = "Extraction location selected. Check the release before installing.";
        RefreshResourcePackageOverview(preserveStatus: true);
    }

    [RelayCommand]
    private async Task CheckResourceRelease()
    {
        var entry = SelectedResourceCatalogEntry();
        if (entry is null || IsResourcePackageOperationRunning || IsOperationRunning)
        {
            return;
        }

        IsResourcePackageOperationRunning = true;
        ActionsEnabled = false;
        ResourcePackageStatus = $"Checking the {SelectedResourceReleaseChannel} {entry.DisplayName} release.";
        var channel = ParseResourceReleaseChannel();
        try
        {
            var release = await _resourcePackageReleaseSource.GetLatestAsync(entry, channel);
            var key = ResourceReleaseKey(entry.PackageId, channel);
            if (release is null)
            {
                _resourcePackageReleases.Remove(key);
                ResourcePackageStatus = $"No {SelectedResourceReleaseChannel} resource release is currently available.";
                AppendLog($"Resource release check: no {SelectedResourceReleaseChannel} release is available for {entry.DisplayName}.");
            }
            else
            {
                _resourcePackageReleases[key] = release;
                ResourcePackageStatus = $"{SelectedResourceReleaseChannel} release {release.Manifest.PackageVersion} is available.";
                AppendLog($"Resource release check: {entry.DisplayName} {release.Manifest.PackageVersion} ({SelectedResourceReleaseChannel}).");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or InvalidOperationException)
        {
            ResourcePackageStatus = $"Resource release check failed: {ex.Message}";
            AppendLog($"Resource release check failed for {entry.DisplayName}: {ex.Message}");
        }
        finally
        {
            IsResourcePackageOperationRunning = false;
            ActionsEnabled = true;
            RefreshResourcePackageOverview(preserveStatus: true);
        }
    }

    [RelayCommand]
    private async Task DownloadResourcePackage()
    {
        var entry = SelectedResourceCatalogEntry();
        var channel = ParseResourceReleaseChannel();
        var release = entry is null
            ? null
            : _resourcePackageReleases.GetValueOrDefault(ResourceReleaseKey(entry.PackageId, channel));
        if (entry is null
            || release is null
            || string.IsNullOrWhiteSpace(ResourceDestinationPath)
            || IsResourcePackageOperationRunning
            || IsOperationRunning)
        {
            return;
        }

        try
        {
            await Task.Run(
                () => _resourcePackageManager.ValidateDestination(entry, release, ResourceDestinationPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            ResourcePackageStatus = $"Resource destination is not available: {ex.Message}";
            AppendLog($"Resource destination validation failed: {ex.Message}");
            return;
        }

        var confirmation = new ConfirmationRequest(
            $"Install {entry.DisplayName}?",
            $"{entry.DisplayName} {release.Manifest.PackageVersion} will be downloaded, verified and extracted to:\n{Path.Combine(ResourceDestinationPath, release.Manifest.TargetDirectory)}\n\nNo X-Plane or aircraft files will be changed.",
            "Install");
        if (!await _userInteractionService.ConfirmAsync(confirmation))
        {
            ResourcePackageStatus = "Resource installation canceled. No files were changed.";
            return;
        }

        IsResourcePackageOperationRunning = true;
        ActionsEnabled = false;
        IsOperationRunning = true;
        OperationPanelVisible = true;
        OperationTitle = $"Installing {entry.DisplayName}";
        OperationSubtitle = "Downloading and validating the official resource archive.";
        OperationProgress = 15;
        OperationProgressText = "15% - Verifying release metadata and resource archive";
        OperationStatus = "Resource installation in progress";
        OperationLog = "";
        var cancellationToken = BeginCancellableOperation();
        try
        {
            var source = _resourcePackageReleaseSource;
            var provisioned = await Task.Run(
                async () => await source.DownloadAsync(
                    entry,
                    release,
                    ResourceDestinationPath,
                    cancellationToken),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            OperationProgress = 70;
            OperationSubtitle = "Securely extracting and verifying every resource file.";
            OperationProgressText = "70% - Extracting verified resource into staging";
            var result = await Task.Run(
                () => _resourcePackageManager.InstallToDirectory(
                    entry,
                    provisioned,
                    ResourceDestinationPath,
                    cancellationToken),
                cancellationToken);
            OperationProgress = 100;
            OperationStatus = "Completed";
            OperationTitle = $"{entry.DisplayName} installed";
            OperationSubtitle = result.Message;
            OperationProgressText = "100% - Resource installation completed and verified";
            ResourcePackageStatus = result.Message;
            ResourceFilePath = result.InstalledPath;
            AppendLog($"Resource installation: {result.Message} Path: {result.InstalledPath}");
        }
        catch (OperationCanceledException)
        {
            OperationProgress = 0;
            OperationStatus = "Canceled";
            OperationTitle = "Resource installation canceled";
            OperationSubtitle = "No installed resource directory was changed.";
            OperationProgressText = "0% - Installation canceled before final placement";
            ResourcePackageStatus = "Resource installation canceled. No installed resource directory was changed.";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            OperationProgress = 0;
            OperationStatus = "Failed";
            OperationTitle = $"{entry.DisplayName} installation failed";
            OperationSubtitle = ex.Message;
            OperationProgressText = "0% - Resource installation failed";
            ResourcePackageStatus = $"Resource installation failed: {ex.Message}";
            AppendLog($"Resource installation failed: {ex.Message}");
        }
        finally
        {
            EndCancellableOperation();
            IsOperationRunning = false;
            IsResourcePackageOperationRunning = false;
            ActionsEnabled = true;
            RefreshResourcePackageOverview(preserveStatus: true);
        }
    }

    [RelayCommand]
    private async Task VerifyResourcePackage()
    {
        var entry = SelectedResourceCatalogEntry();
        if (entry is null || IsResourcePackageOperationRunning || IsOperationRunning)
        {
            return;
        }

        IsResourcePackageOperationRunning = true;
        ActionsEnabled = false;
        ResourcePackageStatus = $"Verifying the recorded {entry.DisplayName} installation.";
        try
        {
            var release = _resourcePackageReleases.GetValueOrDefault(
                ResourceReleaseKey(entry.PackageId, ParseResourceReleaseChannel()));
            var inspection = await Task.Run(() => _resourcePackageManager.Inspect(entry, release, verifyHash: true));
            ResourcePackageStatus = inspection.Status;
            AppendLog($"Resource verification: {entry.DisplayName}: {inspection.Status}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            ResourcePackageStatus = $"Resource verification failed: {ex.Message}";
            AppendLog($"Resource verification failed: {ex.Message}");
        }
        finally
        {
            IsResourcePackageOperationRunning = false;
            ActionsEnabled = true;
            RefreshResourcePackageOverview(preserveStatus: true);
        }
    }

    [RelayCommand]
    private void OpenResourceFolder()
    {
        var state = SelectedResourceCatalogEntry() is { } entry
            ? _stateStore.TryGetResourceInstallation(entry.PackageId)
            : null;
        if (state is null || !Directory.Exists(state.TargetPath))
        {
            ResourcePackageStatus = "The recorded resource installation folder is not available.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = state.TargetPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ResourcePackageStatus = $"Resource folder could not be opened: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RemoveResourcePackage()
    {
        var entry = SelectedResourceCatalogEntry();
        if (entry is null || IsResourcePackageOperationRunning || IsOperationRunning)
        {
            return;
        }

        var confirmation = new ConfirmationRequest(
            $"Remove installed {entry.DisplayName}?",
            "Only the exact resource directory previously installed and still fully verified by the Toolkit will be removed. Changed or additional files block removal. No X-Plane files will be changed.",
            "Remove");
        if (!await _userInteractionService.ConfirmAsync(confirmation))
        {
            ResourcePackageStatus = "Resource removal canceled.";
            return;
        }

        IsResourcePackageOperationRunning = true;
        ActionsEnabled = false;
        try
        {
            var result = await Task.Run(() => _resourcePackageManager.Remove(entry));
            ResourcePackageStatus = result.Message;
            AppendLog($"Resource removal: {result.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            ResourcePackageStatus = $"Resource removal failed: {ex.Message}";
            AppendLog($"Resource removal failed: {ex.Message}");
        }
        finally
        {
            IsResourcePackageOperationRunning = false;
            ActionsEnabled = true;
            RefreshResourcePackageOverview(preserveStatus: true);
        }
    }

    private void RefreshResourcePackageOverview(bool preserveStatus = false)
    {
        SynchronizeAvailableResourcePackages();
        var entry = SelectedResourceCatalogEntry();
        ResourcePackageVisible = entry is not null;
        if (entry is null)
        {
            ResourceDownloadedVersion = "-";
            ResourceAvailableVersion = "Not checked";
            ResourceFilePath = "-";
            ResourcePackageStatus = "Resources are available only for compatible detected products.";
            CanCheckResourceRelease = false;
            CanDownloadResourcePackage = false;
            CanVerifyResourcePackage = false;
            CanOpenResourceFolder = false;
            CanRemoveResourcePackage = false;
            return;
        }

        ResourcePackageName = entry.DisplayName;
        ResourcePackageDescription = entry.Description;
        var channel = ParseResourceReleaseChannel();
        var release = _resourcePackageReleases.GetValueOrDefault(ResourceReleaseKey(entry.PackageId, channel));
        var inspection = _resourcePackageManager.Inspect(entry, release);
        ResourceDownloadedVersion = inspection.InstalledVersion;
        ResourceAvailableVersion = inspection.AvailableVersion;
        ResourceFilePath = string.IsNullOrWhiteSpace(inspection.InstalledPath) ? "-" : inspection.InstalledPath;
        if (string.IsNullOrWhiteSpace(ResourceDestinationPath)
            && !string.IsNullOrWhiteSpace(inspection.DestinationDirectory))
        {
            ResourceDestinationPath = inspection.DestinationDirectory;
        }

        ResourceActionLabel = inspection.State is ResourcePackageState.UpdateAvailable
            ? "Update"
            : "Install";
        if (!preserveStatus)
        {
            ResourcePackageStatus = inspection.Status;
        }

        var available = ActionsEnabled && !IsOperationRunning && !IsResourcePackageOperationRunning;
        CanCheckResourceRelease = available;
        CanDownloadResourcePackage = available
            && release is not null
            && !string.IsNullOrWhiteSpace(ResourceDestinationPath)
            && inspection.CanInstall;
        CanVerifyResourcePackage = available && !string.IsNullOrWhiteSpace(inspection.InstalledPath);
        CanOpenResourceFolder = available && Directory.Exists(inspection.InstalledPath);
        CanRemoveResourcePackage = available && _stateStore.TryGetResourceInstallation(entry.PackageId) is not null;
    }

    private ContentPackageCatalogEntry? SelectedResourceCatalogEntry()
    {
        var product = SelectedProduct;
        var entry = SelectedResourcePackage;
        return product?.IsDetected == true
            && entry?.Category is ContentPackageCategory.Resource
            && entry.SupportedProducts.Contains(product.Family, StringComparer.Ordinal)
                ? entry
                : null;
    }

    private void SynchronizeAvailableResourcePackages()
    {
        var product = SelectedProduct;
        var resources = product?.IsDetected == true
            ? _contentPackageCatalog.ForProduct(product.Family)
                .Where(package => package.Category is ContentPackageCategory.Resource)
                .ToArray()
            : [];
        var selectedPackageId = SelectedResourcePackage?.PackageId;
        var selected = resources.FirstOrDefault(package =>
                package.PackageId.Equals(selectedPackageId, StringComparison.Ordinal))
            ?? resources.FirstOrDefault();

        _synchronizingResourceSelection = true;
        try
        {
            var listChanged = AvailableResourcePackages.Count != resources.Length
                || AvailableResourcePackages.Zip(resources).Any(pair =>
                    !pair.First.PackageId.Equals(pair.Second.PackageId, StringComparison.Ordinal));
            if (listChanged)
            {
                AvailableResourcePackages.ReplaceWith(resources);
            }

            if (!ReferenceEquals(SelectedResourcePackage, selected))
            {
                SelectedResourcePackage = selected;
                ResourceDestinationPath = selected is null
                    ? ""
                    : _stateStore.TryGetResourceInstallation(selected.PackageId)?.DestinationDirectory ?? "";
            }

            ResourceReleaseChannelOptions = ResourceReleaseChannels(selected);
            SelectedResourceReleaseChannel = NormalizeResourceReleaseChannel(
                selected,
                selected is null
                    ? "stable"
                    : _settings.ToolReleaseChannels.GetValueOrDefault(selected.PackageId, "stable"));
        }
        finally
        {
            _synchronizingResourceSelection = false;
        }
    }

    private string? ResolveCurrentXPlaneRoot() =>
        XPlaneInstallationLocator.Resolve(
            SelectedAircraftPath,
            SelectedProduct?.AircraftFolderPath,
            SelectedViewVariant?.AcfPath);

    private ToolReleaseChannel ParseToolReleaseChannel() =>
        SelectedToolReleaseChannel.Equals("beta", StringComparison.OrdinalIgnoreCase)
            ? ToolReleaseChannel.Beta
            : ToolReleaseChannel.Stable;

    private ResourceReleaseChannel ParseResourceReleaseChannel() =>
        SelectedResourceReleaseChannel.Equals("beta", StringComparison.OrdinalIgnoreCase)
            ? ResourceReleaseChannel.Beta
            : ResourceReleaseChannel.Stable;

    private static string NormalizeToolReleaseChannel(string value) =>
        value.Trim().Equals("beta", StringComparison.OrdinalIgnoreCase) ? "beta" : "stable";

    private static IReadOnlyList<string> ResourceReleaseChannels(ContentPackageCatalogEntry? entry)
    {
        var channels = entry?.SupportedChannels
            .Select(NormalizeToolReleaseChannel)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return channels is { Length: > 0 } ? channels : ["stable"];
    }

    private static string NormalizeResourceReleaseChannel(
        ContentPackageCatalogEntry? entry,
        string value)
    {
        var normalized = NormalizeToolReleaseChannel(value);
        var channels = ResourceReleaseChannels(entry);
        return channels.Contains(normalized, StringComparer.Ordinal)
            ? normalized
            : channels[0];
    }

    private static string ToolReleaseKey(string packageId, ToolReleaseChannel channel) =>
        $"{packageId}:{channel.ToString().ToLowerInvariant()}";

    private static string ResourceReleaseKey(string packageId, ResourceReleaseChannel channel) =>
        $"{packageId}:{channel.ToString().ToLowerInvariant()}";

    private void RefreshProductTargets(string? preferredAcfPath = null)
    {
        var products = new[]
        {
            BuildProductTarget(AircraftProductIds.Zibo737Ng, "Zibo", preferredAcfPath),
            BuildProductTarget(AircraftProductIds.LevelUp737Ng, "LevelUp", preferredAcfPath)
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
        AircraftProductUpdateEnabled = ProductActionsEnabled && IsAircraftUpdateFamily(product?.Family);
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
        AircraftProductUpdateEnabled = ProductActionsEnabled && IsAircraftUpdateFamily(product.Family);
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
        _lastAircraftUpdateDryRun = null;
        UpstreamRequiredPackages.Clear();
        UpstreamPackageCacheEntries.Clear();
        UpstreamDryRunEntries.Clear();
        UpstreamDryRunSummary = "No aircraft package review has been calculated.";
        RefreshUpstreamActionAvailability("Check for updates to enable package download or import.");

        if (variant is null)
        {
            UpstreamUpdateStatus = "No aircraft selected";
            UpstreamUpdateSummary = "Select a Zibo or LevelUp aircraft folder to check aircraft packages.";
            UpstreamLocalVersion = "-";
            RefreshUpstreamActionAvailability("Select a Zibo or LevelUp aircraft folder before importing packages.");
            UpstreamFindings.ReplaceWith(["The upstream aircraft package check is read-only."]);
            return;
        }

        UpstreamLocalVersion = variant.LocalVersion ?? "-";

        if (string.Equals(variant.Family, LevelUpAircraftUpdatePackageLoader.Family, StringComparison.OrdinalIgnoreCase))
        {
            UpstreamSource = LevelUpGitHubReleaseIndexSource.DefaultIndexUrl;
            UpstreamUpdateStatus = "Ready to check";
            UpstreamUpdateSummary = "Check the public LevelUp release index for an exact full package or a matching cumulative patch.";
            RefreshUpstreamActionAvailability("Check for updates, or import a LevelUp manifest/archive as an offline fallback.");
            UpstreamFindings.ReplaceWith([
                "The check reads release metadata only; no aircraft files are changed.",
                "LevelUp archives and payload files are verified against authoritative SHA-256 values before review.",
                "Embedded Zibomod updates remain a separate layer and are not inferred from normal Zibo packages."
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
        _lastAircraftUpdateDryRun = null;
        UpstreamUpdateStatus = result.StateLabel;
        UpstreamUpdateSummary = result.Summary;
        UpstreamLocalVersion = result.LocalVersionDisplay;
        UpstreamAvailableVersion = result.AvailableVersionDisplay;
        UpstreamPlanAction = result.ActionDisplay;
        UpstreamUpdateMode = FormatAircraftUpdateMode(result.UpdateMode);
        UpstreamSource = string.IsNullOrWhiteSpace(result.SourceUrl)
            ? string.Equals(result.Family, LevelUpAircraftUpdatePackageLoader.Family, StringComparison.OrdinalIgnoreCase)
                ? LevelUpGitHubReleaseIndexSource.DefaultIndexUrl
                : ZiboUpstreamFeedParser.DefaultFeedUrl
            : result.SourceUrl;
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

    private async Task RefreshUpstreamCacheEntriesAsync()
    {
        var packages = _lastUpstreamUpdateCheck?.RequiredPackages.ToArray() ?? [];
        var cache = _aircraftUpdatePackageCache;
        var entries = await Task.Run(() => packages.Select(cache.Inspect).ToArray());
        UpstreamPackageCacheEntries.ReplaceWith(entries);
        RefreshUpstreamActionAvailability();
    }

    partial void OnActionsEnabledChanged(bool value)
    {
        RefreshUpstreamActionAvailability();
        ApplyQuickViewBaselineAssessment(SelectedViewVariant);
        RefreshSelectedProductSummary(SelectedProduct);
        RefreshOptionalPatchStatus();
        RefreshContentPackageOverview();
        RefreshToolPackageOverview();
        RefreshResourcePackageOverview();
    }

    [RelayCommand]
    private void CancelOperation()
    {
        if (!CanCancelOperation || _operationCancellationSource is null)
        {
            return;
        }

        CanCancelOperation = false;
        OperationStatus = "Canceling";
        OperationProgressText = "Cancel requested - waiting for the current validation step to stop";
        _operationCancellationSource.Cancel();
    }

    private CancellationToken BeginCancellableOperation()
    {
        _operationCancellationSource?.Dispose();
        _operationCancellationSource = new CancellationTokenSource();
        CanCancelOperation = true;
        return _operationCancellationSource.Token;
    }

    private void EndCancellableOperation()
    {
        CanCancelOperation = false;
        _operationCancellationSource?.Dispose();
        _operationCancellationSource = null;
    }

    private void RefreshUnifiedUpdateVisibility()
    {
        var hasSelectedProduct = SelectedViewVariant is not null
            && IsAircraftUpdateFamily(SelectedViewVariant.Family);
        var canCheckAircraftSource = string.Equals(
            UpstreamUpdateStatus,
            "Ready to check",
            StringComparison.Ordinal);
        var hasAircraftPackageAction = _lastUpstreamUpdateCheck is
        {
            IsCustomDistribution: false,
            HasUpdate: true
        };
        var hasVnavAction = _lastAircraftAnalysis?.IsSafeToPatch == true
            && _lastAircraftAnalysis.State is InstallState.NotInstalled
                or InstallState.RepairRequired
                or InstallState.OutdatedMarkedInstallation
                or InstallState.KnownLegacyInstallation;

        UnifiedUpdateVisible = hasSelectedProduct
            && (canCheckAircraftSource || hasAircraftPackageAction || hasVnavAction);
    }

    private void RefreshUpstreamActionAvailability(string? statusOverride = null)
    {
        RefreshUnifiedUpdateVisibility();
        var selectedVariant = SelectedViewVariant;
        var aircraftUpdateSupported = selectedVariant is not null && IsAircraftUpdateFamily(selectedVariant.Family);
        var isLevelUp = string.Equals(selectedVariant?.Family, LevelUpAircraftUpdatePackageLoader.Family, StringComparison.OrdinalIgnoreCase);
        var requiredPackages = _lastUpstreamUpdateCheck?.RequiredPackages ?? [];
        var hasRequiredPackages = requiredPackages.Count > 0;
        var isCustomDistribution = _lastUpstreamUpdateCheck?.IsCustomDistribution == true;
        var allRequiredPackagesCached = hasRequiredPackages
            && UpstreamPackageCacheEntries.Count == requiredPackages.Count
            && UpstreamPackageCacheEntries.All(entry => entry.IsCached);
        var dryRunHasBlockingEntries = UpstreamDryRunEntries.Any(entry => entry.Action is AircraftUpdateDryRunEntryAction.BlockedUnsafePath
            or AircraftUpdateDryRunEntryAction.BlockedInvalidPackage);

        CanImportAircraftUpdatePackage = ActionsEnabled && aircraftUpdateSupported && !isCustomDistribution && (isLevelUp || hasRequiredPackages);
        CanDownloadAircraftUpdatePackage = ActionsEnabled && aircraftUpdateSupported && hasRequiredPackages && !isCustomDistribution && !allRequiredPackagesCached;
        CanDryRunAircraftUpdatePackage = ActionsEnabled && aircraftUpdateSupported && hasRequiredPackages && !isCustomDistribution && allRequiredPackagesCached;
        CanApplyAircraftUpdatePackage = ActionsEnabled
            && aircraftUpdateSupported
            && hasRequiredPackages
            && !isCustomDistribution
            && allRequiredPackagesCached
            && _lastAircraftUpdateDryRun?.Succeeded == true
            && !dryRunHasBlockingEntries;
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
            UpstreamActionStatus = isLevelUp
                ? "Check the public LevelUp release index, or import a manifest/archive as an offline fallback."
                : "Check for updates before importing packages.";
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
        AircraftProductUpdateEnabled = ActionsEnabled && aircraftUpdateSupported;
    }

    private static bool IsAircraftUpdateFamily(string? family) =>
        string.Equals(family, "zibo-737ng", StringComparison.OrdinalIgnoreCase)
        || string.Equals(family, LevelUpAircraftUpdatePackageLoader.Family, StringComparison.OrdinalIgnoreCase);

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
            "Downloaded package cache folder",
            AircraftUpdateCacheRootPath,
            fullPath => _settings.AircraftUpdateCacheRootPath = fullPath,
            fullPath =>
            {
                _aircraftUpdatePackageCache = new AircraftUpdatePackageCache(fullPath);
                _aircraftUpdatePackageCache.EnsureRoot();
                _contentPatchReleaseSource = new GitHubContentPatchReleaseSource(
                    _aircraftUpdateHttpClient,
                    _aircraftUpdatePackageCache.RootPath);
                _toolPackageReleaseSource = new GitHubToolPackageReleaseSource(
                    _aircraftUpdateHttpClient,
                    _aircraftUpdatePackageCache.RootPath);
                AircraftUpdateCacheRootPath = _aircraftUpdatePackageCache.RootPath;
                UpstreamCacheRoot = _aircraftUpdatePackageCache.RootPath;
                RefreshUpstreamCacheEntries();
                _lastAircraftUpdateDryRun = null;
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
            _lastAircraftUpdateDryRun = null;
            UpstreamDryRunEntries.Clear();
            UpstreamDryRunSummary = "Downloaded package cache was cleared. Import required packages again before review.";
            RefreshUpstreamActionAvailability($"Downloaded package cache cleared. Removed {removed} top-level item(s).");
            SettingsStatus = $"Downloaded package cache cleared. Removed {removed} top-level item(s).";
            AppendLog($"Settings: downloaded package cache cleared at {_aircraftUpdatePackageCache.RootPath}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SettingsStatus = $"Downloaded package cache was not cleared: {ex.Message}";
            AppendLog($"Settings: downloaded package cache clear failed: {ex.Message}");
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

    private Stopwatch StartOperationElapsedTimer()
    {
        StopOperationElapsedTimer();
        OperationElapsed = "00:00s";
        _operationElapsedStopwatch = Stopwatch.StartNew();
        _operationElapsedTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _operationElapsedTimer.Tick += UpdateOperationElapsed;
        _operationElapsedTimer.Start();
        return _operationElapsedStopwatch;
    }

    private void StopOperationElapsedTimer()
    {
        if (_operationElapsedTimer is not null)
        {
            _operationElapsedTimer.Stop();
            _operationElapsedTimer.Tick -= UpdateOperationElapsed;
            _operationElapsedTimer = null;
        }

        if (_operationElapsedStopwatch is not null)
        {
            _operationElapsedStopwatch.Stop();
            OperationElapsed = FormatElapsed(_operationElapsedStopwatch.Elapsed);
            _operationElapsedStopwatch = null;
        }
    }

    private void UpdateOperationElapsed(object? sender, EventArgs eventArgs)
    {
        if (_operationElapsedStopwatch is not null)
        {
            OperationElapsed = FormatElapsed(_operationElapsedStopwatch.Elapsed);
        }
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
        if (string.Equals(family, AircraftProductIds.Zibo737Ng, StringComparison.OrdinalIgnoreCase))
        {
            if (_manifest.PackageId.Contains("zibo", StringComparison.OrdinalIgnoreCase))
            {
                return _manifest;
            }

            return _manifests.FirstOrDefault(manifest => manifest.PackageId.Contains("zibo", StringComparison.OrdinalIgnoreCase))
                ?? _manifest;
        }

        if (string.Equals(family, AircraftProductIds.LevelUp737Ng, StringComparison.OrdinalIgnoreCase))
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

    private static ContentPackageCatalog LoadContentPackageCatalog()
    {
        var contentDir = Path.Combine(AppContext.BaseDirectory, "Content");
        if (!Directory.Exists(contentDir))
        {
            contentDir = Path.Combine(Environment.CurrentDirectory, "src", "LevelUp.NavTableUpdater.App", "Content");
        }

        var catalogPath = Path.Combine(contentDir, "content-package-catalog.json");
        if (!File.Exists(catalogPath))
        {
            throw new FileNotFoundException("Bundled content package catalog is missing.", catalogPath);
        }

        return ContentPackageCatalog.Parse(File.ReadAllText(catalogPath));
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

public sealed record AvailableContentPackageStatus(
    string PackageId,
    string DisplayName,
    string Description,
    string CategoryLabel,
    string InstalledVersion,
    string AvailableVersion,
    string Status,
    string RepositoryUrl,
    bool IsOptional,
    bool CanAct,
    string ActionLabel,
    bool CanRestore,
    bool CanRemove)
{
    public bool IsManaged => !IsOptional;
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
