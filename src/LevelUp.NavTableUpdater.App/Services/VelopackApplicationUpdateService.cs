using Velopack;
using Velopack.Sources;

namespace LevelUp.NavTableUpdater.App.Services;

public sealed class VelopackApplicationUpdateService : IApplicationUpdateService
{
    public const string RepositoryUrl = "https://github.com/wahltho/xplane-737ng-maintenance-toolkit";
    public const string ReleasesUrl = $"{RepositoryUrl}/releases";

    private readonly UpdateManager _updateManager;
    private UpdateInfo? _pendingUpdate;
    private bool _isDownloaded;

    public VelopackApplicationUpdateService()
        : this(new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false)))
    {
    }

    internal VelopackApplicationUpdateService(UpdateManager updateManager)
    {
        _updateManager = updateManager ?? throw new ArgumentNullException(nameof(updateManager));
    }

    public async Task<ApplicationUpdateCheckResult> CheckForUpdatesAsync()
    {
        var currentVersion = _updateManager.CurrentVersion?.ToString()
            ?? typeof(VelopackApplicationUpdateService).Assembly.GetName().Version?.ToString(3)
            ?? "unknown";
        if (!_updateManager.IsInstalled)
        {
            return new ApplicationUpdateCheckResult(
                IsManagedInstallation: false,
                currentVersion,
                AvailableVersion: null,
                ReleaseNotes: null,
                ReleasesUrl);
        }

        _pendingUpdate = await _updateManager.CheckForUpdatesAsync().ConfigureAwait(false);
        _isDownloaded = false;
        var availableVersion = _pendingUpdate?.TargetFullRelease.Version.ToString();
        return new ApplicationUpdateCheckResult(
            IsManagedInstallation: true,
            currentVersion,
            availableVersion,
            _pendingUpdate?.TargetFullRelease.NotesMarkdown,
            availableVersion is null ? ReleasesUrl : $"{ReleasesUrl}/tag/v{availableVersion}");
    }

    public async Task DownloadUpdateAsync(
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_pendingUpdate is null)
        {
            throw new InvalidOperationException("No application update has been selected.");
        }

        await _updateManager.DownloadUpdatesAsync(
            _pendingUpdate,
            value => progress?.Report(value),
            cancellationToken).ConfigureAwait(false);
        _isDownloaded = true;
    }

    public void ApplyUpdateAndRestart()
    {
        if (_pendingUpdate is null || !_isDownloaded)
        {
            throw new InvalidOperationException("The application update has not been downloaded.");
        }

        _updateManager.ApplyUpdatesAndRestart(_pendingUpdate.TargetFullRelease, []);
    }
}
