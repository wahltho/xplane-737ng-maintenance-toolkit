using System.Security.Cryptography;
using LevelUp.NavTableUpdater.Core.Platform;

namespace LevelUp.NavTableUpdater.Core.Upstream;

public sealed class AircraftUpdatePackageCache
{
    private const string MarkerFileName = ".xplane-737ng-aircraft-update-cache";
    private readonly IAircraftTorrentPackageDownloader _torrentDownloader;

    public AircraftUpdatePackageCache(
        string rootPath,
        IAircraftTorrentPackageDownloader? torrentDownloader = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        RootPath = Path.GetFullPath(rootPath);
        _torrentDownloader = torrentDownloader ?? new ZiboTorrentPackageDownloader();
    }

    public string RootPath { get; }

    public static string DefaultRootPath => ToolkitPaths.DefaultAircraftUpdateCacheRootPath;

    public void EnsureRoot()
    {
        Directory.CreateDirectory(RootPath);
        File.WriteAllText(Path.Combine(RootPath, MarkerFileName), "X-Plane 737NG Maintenance Toolkit aircraft update cache\n");
    }

    public int Clear()
    {
        if (!Directory.Exists(RootPath))
        {
            EnsureRoot();
            return 0;
        }

        if (!IsSafeToClear())
        {
            throw new InvalidOperationException("Cache folder is not marked as a toolkit aircraft update cache. Save the cache folder setting first before clearing it.");
        }

        var removed = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(RootPath))
        {
            if (string.Equals(Path.GetFileName(entry), MarkerFileName, StringComparison.Ordinal))
            {
                continue;
            }

            if (Directory.Exists(entry))
            {
                Directory.Delete(entry, recursive: true);
            }
            else
            {
                File.Delete(entry);
            }

            removed++;
        }

        EnsureRoot();
        return removed;
    }

    public AircraftUpdatePackageCacheEntry Inspect(AircraftUpdatePackage package)
    {
        var cachePath = GetPackagePath(package);
        if (!File.Exists(cachePath))
        {
            return new AircraftUpdatePackageCacheEntry(
                package,
                cachePath,
                AircraftUpdatePackageCacheState.Missing,
                SizeBytes: null,
                Sha256: null);
        }

        var info = new FileInfo(cachePath);
        var sha256 = ComputeSha256(cachePath);
        var validationError = GetExpectedIntegrityError(package, info.Length, sha256);
        return new AircraftUpdatePackageCacheEntry(
            package,
            cachePath,
            validationError is null ? AircraftUpdatePackageCacheState.Cached : AircraftUpdatePackageCacheState.Invalid,
            info.Length,
            sha256,
            validationError);
    }

    public IReadOnlyList<AircraftUpdatePackageCacheEntry> InspectRequiredPackages(AircraftUpdatePlan plan) =>
        plan.RequiredPackages.Select(Inspect).ToArray();

    public AircraftUpdatePackageCacheEntry ImportPackage(
        string packagePath,
        AircraftUpdatePackage expectedPackage,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentNullException.ThrowIfNull(expectedPackage);
        cancellationToken.ThrowIfCancellationRequested();

        var sourcePath = Path.GetFullPath(packagePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Aircraft update package was not found.", sourcePath);
        }

        var sourceFileName = Path.GetFileName(sourcePath);
        if (!string.Equals(sourceFileName, expectedPackage.FileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Selected package '{sourceFileName}' does not match expected package '{expectedPackage.FileName}'.");
        }

        ValidateReadableArchive(sourcePath, cancellationToken);
        var sourceInfo = new FileInfo(sourcePath);
        var sourceSha256 = ComputeSha256(sourcePath, cancellationToken);
        var integrityError = GetExpectedIntegrityError(expectedPackage, sourceInfo.Length, sourceSha256);
        if (integrityError is not null)
        {
            throw new InvalidDataException(integrityError);
        }

        EnsureRoot();
        var destinationPath = GetPackagePath(expectedPackage);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        var tempPath = destinationPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            CopyFile(sourcePath, tempPath, cancellationToken);
            var copiedSha256 = ComputeSha256(tempPath, cancellationToken);
            if (!string.Equals(sourceSha256, copiedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Copied package SHA-256 does not match the selected source package: {expectedPackage.FileName}.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempPath, destinationPath, overwrite: true);
            var info = new FileInfo(destinationPath);
            return new AircraftUpdatePackageCacheEntry(
                expectedPackage,
                destinationPath,
                AircraftUpdatePackageCacheState.Imported,
                info.Length,
                copiedSha256);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

    }

    public AircraftUpdatePackageCacheEntry ImportZip(
        string zipPath,
        AircraftUpdatePackage expectedPackage,
        CancellationToken cancellationToken = default) =>
        ImportPackage(zipPath, expectedPackage, cancellationToken);

    public async Task<AircraftUpdatePackageCacheEntry> DownloadAsync(
        AircraftUpdatePackage package,
        HttpClient httpClient,
        CancellationToken cancellationToken = default,
        IProgress<AircraftPackageDownloadProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(httpClient);

        var candidates = BuildDownloadCandidates(package).ToArray();
        if (candidates.Length == 0)
        {
            throw new InvalidOperationException($"Package '{package.FileName}' has no download URL.");
        }

        EnsureRoot();
        var destinationPath = GetPackagePath(package);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var failures = new List<string>();

        foreach (var candidate in candidates)
        {
            var tempPath = destinationPath + $".{Guid.NewGuid():N}.download";
            try
            {
                progress?.Report(new AircraftPackageDownloadProgress("HTTPS", "Downloading direct archive", 0));
                using var response = await httpClient.GetAsync(candidate, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                ValidateResponseContentType(response, candidate);
                await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (var output = File.Create(tempPath))
                {
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                }

                ValidateReadableArchive(tempPath, cancellationToken);
                var downloadedInfo = new FileInfo(tempPath);
                var downloadedSha = ComputeSha256(tempPath, cancellationToken);
                var integrityError = GetExpectedIntegrityError(package, downloadedInfo.Length, downloadedSha);
                if (integrityError is not null)
                {
                    throw new InvalidDataException(integrityError);
                }

                File.Move(tempPath, destinationPath, overwrite: true);
                var info = new FileInfo(destinationPath);
                return new AircraftUpdatePackageCacheEntry(
                    package,
                    destinationPath,
                    AircraftUpdatePackageCacheState.Imported,
                    info.Length,
                    downloadedSha);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException)
            {
                failures.Add($"{candidate}: {ex.Message}");
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        if (IsTorrentSource(package.SourceUrl))
        {
            var torrentSessionRoot = Path.Combine(Path.GetDirectoryName(destinationPath)!, ".torrent-work");
            string? downloadedPath = null;
            try
            {
                downloadedPath = await _torrentDownloader.DownloadAsync(
                    package.SourceUrl!,
                    package.FileName,
                    torrentSessionRoot,
                    httpClient,
                    progress,
                    cancellationToken).ConfigureAwait(false);
                ValidateReadableArchive(downloadedPath, cancellationToken);
                var downloadedInfo = new FileInfo(downloadedPath);
                var downloadedSha = ComputeSha256(downloadedPath, cancellationToken);
                var integrityError = GetExpectedIntegrityError(package, downloadedInfo.Length, downloadedSha);
                if (integrityError is not null)
                {
                    throw new InvalidDataException(integrityError);
                }

                File.Move(downloadedPath, destinationPath, overwrite: true);
                downloadedPath = null;
                var info = new FileInfo(destinationPath);
                return new AircraftUpdatePackageCacheEntry(
                    package,
                    destinationPath,
                    AircraftUpdatePackageCacheState.Imported,
                    info.Length,
                    downloadedSha);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TimeoutException)
            {
                failures.Add($"{package.SourceUrl}: {ex.Message}");
            }
            finally
            {
                if (downloadedPath is not null && File.Exists(downloadedPath))
                {
                    File.Delete(downloadedPath);
                }
            }
        }

        throw new InvalidOperationException($"Package '{package.FileName}' could not be downloaded and validated. {string.Join(" | ", failures)}");
    }

    public string GetPackagePath(AircraftUpdatePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var fileName = Path.GetFileName(package.FileName);
        if (!string.Equals(fileName, package.FileName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Package filename is not safe: {package.FileName}");
        }

        var family = SanitizePathSegment(package.Family);
        var version = SanitizePathSegment(package.VersionDisplay);
        return Path.Combine(RootPath, family, version, fileName);
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) || ch is '/' or '\\' ? '_' : ch).ToArray();
        var sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    private static string ComputeSha256(string path, CancellationToken cancellationToken = default)
    {
        using var stream = File.OpenRead(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, read);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static IEnumerable<string> BuildDownloadCandidates(AircraftUpdatePackage package)
    {
        if (string.IsNullOrWhiteSpace(package.SourceUrl))
        {
            yield break;
        }

        var sourceUrl = package.SourceUrl.Trim();
        if (sourceUrl.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
        {
            yield return sourceUrl[..^".torrent".Length];
            yield break;
        }

        yield return sourceUrl;
    }

    private static bool IsTorrentSource(string? sourceUrl) =>
        !string.IsNullOrWhiteSpace(sourceUrl)
        && sourceUrl.Trim().EndsWith(".torrent", StringComparison.OrdinalIgnoreCase);

    private static void ValidateResponseContentType(HttpResponseMessage response, string sourceUrl)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (string.Equals(mediaType, "application/x-bittorrent", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mediaType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Download did not return an aircraft archive ({mediaType ?? "unknown content type"}): {sourceUrl}");
        }
    }

    private static void ValidateReadableArchive(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var archive = AircraftPackageArchive.Open(path);
        _ = archive.Entries.Count;
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void CopyFile(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        using var source = File.OpenRead(sourcePath);
        using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            destination.Write(buffer, 0, read);
        }

        cancellationToken.ThrowIfCancellationRequested();
        destination.Flush(flushToDisk: true);
    }

    private static string? GetExpectedIntegrityError(AircraftUpdatePackage package, long sizeBytes, string sha256)
    {
        if (package.ExpectedSizeBytes is not null && package.ExpectedSizeBytes.Value != sizeBytes)
        {
            return $"Package size does not match the manifest for {package.FileName}: expected {package.ExpectedSizeBytes.Value}, got {sizeBytes}.";
        }

        if (!string.IsNullOrWhiteSpace(package.ExpectedSha256)
            && !string.Equals(package.ExpectedSha256, sha256, StringComparison.OrdinalIgnoreCase))
        {
            return $"Package SHA-256 does not match the manifest for {package.FileName}.";
        }

        return null;
    }

    private bool IsSafeToClear()
    {
        if (File.Exists(Path.Combine(RootPath, MarkerFileName)))
        {
            return true;
        }

        return string.Equals(RootPath, Path.GetFullPath(DefaultRootPath), StringComparison.OrdinalIgnoreCase);
    }
}
