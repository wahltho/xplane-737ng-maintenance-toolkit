namespace LevelUp.NavTableUpdater.Core.Upstream;

public interface IAircraftTorrentPackageDownloader
{
    Task<string> DownloadAsync(
        string torrentUrl,
        string expectedFileName,
        string workingDirectory,
        HttpClient httpClient,
        IProgress<AircraftPackageDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
