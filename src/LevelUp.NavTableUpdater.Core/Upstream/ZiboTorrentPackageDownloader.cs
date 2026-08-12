using MonoTorrent;
using MonoTorrent.BEncoding;
using MonoTorrent.Client;

namespace LevelUp.NavTableUpdater.Core.Upstream;

public sealed class ZiboTorrentPackageDownloader : IAircraftTorrentPackageDownloader
{
    private const int MaximumTorrentMetadataBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan NoPeerTimeout = TimeSpan.FromMinutes(5);

    private static readonly string[] FallbackTrackers =
    [
        "udp://tracker.opentrackr.org:1337/announce",
        "udp://open.stealth.si:80/announce",
        "udp://tracker.torrent.eu.org:451/announce",
        "udp://exodus.desync.com:6969/announce"
    ];

    public async Task<string> DownloadAsync(
        string torrentUrl,
        string expectedFileName,
        string workingDirectory,
        HttpClient httpClient,
        IProgress<AircraftPackageDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(torrentUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(httpClient);

        var safeFileName = Path.GetFileName(expectedFileName);
        if (!string.Equals(safeFileName, expectedFileName, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Expected torrent package filename is unsafe: {expectedFileName}");
        }

        progress?.Report(new AircraftPackageDownloadProgress("BitTorrent", "Downloading torrent metadata", 0));
        var torrent = await LoadTorrentAsync(torrentUrl, expectedFileName, httpClient, cancellationToken).ConfigureAwait(false);

        var sessionRoot = Path.Combine(workingDirectory, $"torrent-{Guid.NewGuid():N}");
        var downloadRoot = Path.Combine(sessionRoot, "download");
        Directory.CreateDirectory(downloadRoot);

        var settings = new EngineSettingsBuilder
        {
            AllowPortForwarding = false,
            AutoSaveLoadDhtCache = true,
            AutoSaveLoadFastResume = false,
            CacheDirectory = Path.Combine(workingDirectory, "engine-cache")
        }.ToSettings();
        var torrentSettings = new TorrentSettingsBuilder
        {
            AllowDht = true,
            AllowPeerExchange = true,
            CreateContainingDirectory = false,
            MaximumConnections = 60
        }.ToSettings();

        try
        {
            string completedPath;
            using (var engine = new ClientEngine(settings))
            {
                var manager = await engine.AddAsync(torrent, downloadRoot, torrentSettings).ConfigureAwait(false);
                await AddFallbackTrackersAsync(manager).ConfigureAwait(false);

                var activityStopwatch = System.Diagnostics.Stopwatch.StartNew();
                var previousProgress = 0d;
                await manager.StartAsync().ConfigureAwait(false);
                try
                {
                    while (manager.Progress < 100)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (manager.State == TorrentState.Error)
                        {
                            throw new IOException($"BitTorrent download entered an error state: {manager.Error?.Exception?.Message ?? "unknown error"}");
                        }

                        if (manager.Progress > previousProgress || manager.Monitor.DownloadRate > 0)
                        {
                            activityStopwatch.Restart();
                            previousProgress = manager.Progress;
                        }
                        else if (activityStopwatch.Elapsed >= NoPeerTimeout)
                        {
                            throw new TimeoutException("No active BitTorrent peers were found for the official Zibo package within five minutes.");
                        }

                        progress?.Report(new AircraftPackageDownloadProgress(
                            "BitTorrent",
                            manager.OpenConnections == 0 ? "Finding peers" : "Downloading from peers",
                            manager.Progress,
                            manager.Monitor.DownloadRate,
                            manager.OpenConnections));
                        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
                    }

                    progress?.Report(new AircraftPackageDownloadProgress(
                        "BitTorrent",
                        "Torrent piece verification complete",
                        100,
                        manager.Monitor.DownloadRate,
                        manager.OpenConnections));
                }
                finally
                {
                    await manager.StopAsync().ConfigureAwait(false);
                }

                var downloadedPath = Path.Combine(downloadRoot, expectedFileName);
                if (!File.Exists(downloadedPath))
                {
                    throw new InvalidDataException($"BitTorrent completed without producing the expected package: {expectedFileName}");
                }

                completedPath = Path.Combine(workingDirectory, $"{Guid.NewGuid():N}-{expectedFileName}");
                File.Move(downloadedPath, completedPath);
            }

            DeleteSessionDirectory(sessionRoot);
            return completedPath;
        }
        catch
        {
            DeleteSessionDirectory(sessionRoot);
            throw;
        }
    }

    private static async Task<Torrent> LoadTorrentAsync(
        string torrentUrl,
        string expectedFileName,
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(torrentUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumTorrentMetadataBytes)
        {
            throw new InvalidDataException("Torrent metadata exceeds the accepted size limit.");
        }

        var metadata = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (metadata.Length == 0 || metadata.Length > MaximumTorrentMetadataBytes)
        {
            throw new InvalidDataException("Torrent metadata is empty or exceeds the accepted size limit.");
        }

        Torrent torrent;
        try
        {
            torrent = await Torrent.LoadAsync(metadata).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is BEncodingException or InvalidOperationException or InvalidDataException)
        {
            throw new InvalidDataException("The official package source did not return valid torrent metadata.", ex);
        }

        if (!string.Equals(torrent.Name, expectedFileName, StringComparison.OrdinalIgnoreCase)
            || torrent.Files.Count != 1
            || !string.Equals(torrent.Files[0].Path, expectedFileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Torrent metadata does not describe exactly the expected package '{expectedFileName}'.");
        }

        return torrent;
    }

    private static async Task AddFallbackTrackersAsync(TorrentManager manager)
    {
        foreach (var tracker in FallbackTrackers)
        {
            try
            {
                await manager.TrackerManager.AddTrackerAsync(new Uri(tracker)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
            {
                // DHT, PEX and the trackers embedded in the official metadata remain available.
            }
        }
    }

    internal static void DeleteSessionDirectory(string sessionRoot)
    {
        if (Directory.Exists(sessionRoot))
        {
            Directory.Delete(sessionRoot, recursive: true);
        }
    }
}
