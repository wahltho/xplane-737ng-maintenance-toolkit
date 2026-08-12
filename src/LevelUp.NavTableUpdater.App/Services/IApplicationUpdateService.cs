namespace LevelUp.NavTableUpdater.App.Services;

public sealed record ApplicationUpdateCheckResult(
    bool IsManagedInstallation,
    string CurrentVersion,
    string? AvailableVersion,
    string? ReleaseNotes,
    string ReleaseUrl)
{
    public bool HasUpdate => !string.IsNullOrWhiteSpace(AvailableVersion);
}

public interface IApplicationUpdateService
{
    Task<ApplicationUpdateCheckResult> CheckForUpdatesAsync();

    Task DownloadUpdateAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default);

    void ApplyUpdateAndRestart();
}
