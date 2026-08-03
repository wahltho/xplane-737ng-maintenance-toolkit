using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using LevelUp.NavTableUpdater.Core.Manifest;

namespace LevelUp.NavTableUpdater.Core.Content;

public sealed record ContentPatchRelease(
    string Tag,
    string ReleasePageUrl,
    string AssetName,
    string AssetUrl,
    long AssetSize,
    string AssetSha256);

public sealed record ContentPatchProvisionResult(
    DeclarativePatchPackage Package,
    string PackageDirectory,
    ContentPatchRelease Release,
    bool Downloaded);

public sealed class GitHubContentPatchReleaseSource
{
    private const int MaximumMetadataBytes = 1024 * 1024;
    private const int MaximumManifestBytes = 1024 * 1024;
    private const long MaximumArchiveBytes = 64L * 1024 * 1024;
    private const long MaximumExpandedBytes = 128L * 1024 * 1024;
    private const int MaximumArchiveEntries = 512;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly string _cacheRoot;

    public GitHubContentPatchReleaseSource(HttpClient httpClient, string cacheRoot)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);
        _httpClient = httpClient;
        _cacheRoot = Path.GetFullPath(cacheRoot);
    }

    public async Task<ContentPatchRelease> GetLatestAsync(
        ContentPackageCatalogEntry catalogEntry,
        CancellationToken cancellationToken = default)
    {
        ValidateGitHubDistribution(catalogEntry);
        var (owner, repository) = ParseRepository(catalogEntry.RepositoryUrl);
        var apiUrl = $"https://api.github.com/repos/{owner}/{repository}/releases/latest";
        using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("XPlane737NGMaintenanceToolkit", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        var bytes = await DownloadBytesAsync(request, MaximumMetadataBytes, cancellationToken).ConfigureAwait(false);

        GitHubReleaseDocument release;
        try
        {
            release = JsonSerializer.Deserialize<GitHubReleaseDocument>(bytes, JsonOptions)
                ?? throw new InvalidDataException("GitHub release metadata is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"GitHub release metadata is invalid JSON: {ex.Message}", ex);
        }

        if (release.Draft || release.Prerelease || !IsSafeSegment(release.TagName))
        {
            throw new InvalidDataException($"Latest release for {catalogEntry.PackageId} is not a stable, safe release.");
        }

        if (release.Assets is null)
        {
            throw new InvalidDataException($"Latest release for {catalogEntry.PackageId} has no asset list.");
        }

        var matches = release.Assets
            .Where(asset => AssetNameMatches(catalogEntry.Distribution.AssetNamePattern, asset.Name))
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidDataException(
                $"Expected exactly one release asset matching '{catalogEntry.Distribution.AssetNamePattern}', found {matches.Length}.");
        }

        var selected = matches[0];
        var expectedAssetPrefix = $"/{owner}/{repository}/releases/download/";
        if (!IsSafeAssetName(selected.Name)
            || selected.Size is <= 0 or > MaximumArchiveBytes
            || !TryParseSha256Digest(selected.Digest, out var sha256)
            || !Uri.TryCreate(selected.BrowserDownloadUrl, UriKind.Absolute, out var assetUri)
            || assetUri.Scheme != Uri.UriSchemeHttps
            || !assetUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || !assetUri.AbsolutePath.StartsWith(expectedAssetPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Release asset metadata is incomplete or unsafe for {catalogEntry.PackageId}.");
        }

        var releasePageUrl = string.IsNullOrWhiteSpace(release.HtmlUrl)
            ? $"{catalogEntry.RepositoryUrl.TrimEnd('/')}/releases/tag/{Uri.EscapeDataString(release.TagName)}"
            : release.HtmlUrl;
        return new ContentPatchRelease(
            release.TagName,
            releasePageUrl,
            selected.Name,
            selected.BrowserDownloadUrl,
            selected.Size,
            sha256);
    }

    public async Task<ContentPatchProvisionResult> ProvisionAsync(
        ContentPackageCatalogEntry catalogEntry,
        ContentPatchRelease release,
        CancellationToken cancellationToken = default)
    {
        ValidateGitHubDistribution(catalogEntry);
        ArgumentNullException.ThrowIfNull(release);
        ValidateReleaseForCatalog(catalogEntry, release);
        var releaseRoot = Path.Combine(
            _cacheRoot,
            "content-patches",
            SanitizeSegment(catalogEntry.PackageId),
            SanitizeSegment(release.Tag));
        CreateSafeCacheDirectory(releaseRoot);
        var archivePath = Path.Combine(releaseRoot, release.AssetName);
        RejectLink(archivePath, "Content patch cache archive");
        var downloaded = false;
        if (!File.Exists(archivePath) || !ArchiveMatchesRelease(archivePath, release))
        {
            await DownloadArchiveAsync(release, archivePath, cancellationToken).ConfigureAwait(false);
            downloaded = true;
        }

        var packageDirectory = Path.Combine(releaseRoot, "package");
        RejectLink(packageDirectory, "Content patch cache package directory");
        if (Directory.Exists(packageDirectory))
        {
            try
            {
                var cachedPackage = DeclarativePatchPackageLoader.LoadDirectory(packageDirectory);
                ValidateResolvedPackage(catalogEntry, release, cachedPackage);
                return new ContentPatchProvisionResult(cachedPackage, packageDirectory, release, downloaded);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Directory.Delete(packageDirectory, recursive: true);
            }
        }

        var tempDirectory = packageDirectory + $".tmp-{Guid.NewGuid():N}";
        try
        {
            Directory.CreateDirectory(tempDirectory);
            ExtractRequiredPackageFiles(archivePath, tempDirectory, cancellationToken);
            var package = DeclarativePatchPackageLoader.LoadDirectory(tempDirectory);
            ValidateResolvedPackage(catalogEntry, release, package);
            Directory.Move(tempDirectory, packageDirectory);
            return new ContentPatchProvisionResult(package, packageDirectory, release, downloaded);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    private async Task<byte[]> DownloadBytesAsync(
        HttpRequestMessage request,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 0
            && response.Content.Headers.ContentLength > maximumBytes)
        {
            throw new InvalidDataException("GitHub release metadata exceeds the size limit.");
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > maximumBytes)
            {
                throw new InvalidDataException("GitHub release metadata exceeds the size limit.");
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private async Task DownloadArchiveAsync(
        ContentPatchRelease release,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var tempPath = destinationPath + $".download-{Guid.NewGuid():N}";
        try
        {
            using var response = await _httpClient.GetAsync(
                release.AssetUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > 0
                && response.Content.Headers.ContentLength != release.AssetSize)
            {
                throw new InvalidDataException($"Release asset size differs from GitHub metadata: {release.AssetName}.");
            }

            {
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long length = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    length += read;
                    if (length > MaximumArchiveBytes || length > release.AssetSize)
                    {
                        throw new InvalidDataException($"Release asset exceeds its declared size: {release.AssetName}.");
                    }

                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }

                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                var actualSha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (length != release.AssetSize
                    || !actualSha256.Equals(release.AssetSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Release asset failed size/SHA-256 validation: {release.AssetName}.");
                }
            }

            File.Move(tempPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static void ExtractRequiredPackageFiles(
        string archivePath,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > MaximumArchiveEntries)
        {
            throw new InvalidDataException("Content patch archive contains too many entries.");
        }

        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        var caseInsensitiveNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expandedSize = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedName = NormalizeArchivePath(entry.FullName);
            if (!caseInsensitiveNames.Add(normalizedName))
            {
                throw new InvalidDataException($"Content patch archive contains duplicate or case-colliding paths: {normalizedName}.");
            }

            if (IsSymbolicLink(entry))
            {
                throw new InvalidDataException($"Content patch archive contains a symbolic link: {normalizedName}.");
            }

            expandedSize += entry.Length;
            if (entry.Length > MaximumArchiveBytes || expandedSize > MaximumExpandedBytes)
            {
                throw new InvalidDataException("Content patch archive exceeds the expanded size limit.");
            }

            if (!string.IsNullOrEmpty(entry.Name))
            {
                entries.Add(normalizedName, entry);
            }
        }

        var manifests = entries
            .Where(pair => pair.Key.EndsWith("package-manifest.json", StringComparison.Ordinal)
                && Path.GetFileName(pair.Key).Equals("package-manifest.json", StringComparison.Ordinal))
            .ToArray();
        if (manifests.Length != 1)
        {
            throw new InvalidDataException($"Content patch archive must contain exactly one package-manifest.json; found {manifests.Length}.");
        }

        var manifestParts = manifests[0].Key.Split('/');
        if (manifestParts.Length is < 1 or > 2)
        {
            throw new InvalidDataException("package-manifest.json must be at archive root or inside one top-level folder.");
        }

        var manifestBytes = ReadEntry(manifests[0].Value, MaximumManifestBytes, cancellationToken);
        var manifest = DeclarativePatchManifestParser.Parse(System.Text.Encoding.UTF8.GetString(manifestBytes));
        var prefix = manifestParts.Length == 2 ? manifestParts[0] + "/" : "";
        var required = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [manifests[0].Key] = "package-manifest.json"
        };
        foreach (var payload in manifest.Payloads)
        {
            required.Add(prefix + payload.Path.Replace('\\', '/'), payload.Path);
        }

        foreach (var requiredEntry in required)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entries.TryGetValue(requiredEntry.Key, out var archiveEntry))
            {
                throw new InvalidDataException($"Content patch archive is missing required payload: {requiredEntry.Value}.");
            }

            var destinationPath = ContentPatchPathSafety.ResolveTarget(
                destinationRoot,
                requiredEntry.Value,
                "Extracted content patch path");
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            using var input = archiveEntry.Open();
            using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
        }
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry, int maximumBytes, CancellationToken cancellationToken)
    {
        if (entry.Length > maximumBytes)
        {
            throw new InvalidDataException($"Archive entry exceeds the size limit: {entry.FullName}.");
        }

        using var input = entry.Open();
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (output.Length + read > maximumBytes)
            {
                throw new InvalidDataException($"Archive entry exceeds the size limit: {entry.FullName}.");
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static string NormalizeArchivePath(string value)
    {
        var normalized = value.Replace('\\', '/').TrimEnd('/');
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.StartsWith('/')
            || normalized.Contains(':'))
        {
            throw new InvalidDataException($"Unsafe content patch archive path: {value}.");
        }

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(part => part is "." or ".."))
        {
            throw new InvalidDataException($"Unsafe content patch archive path: {value}.");
        }

        return string.Join('/', parts);
    }

    private static void ValidateResolvedPackage(
        ContentPackageCatalogEntry catalogEntry,
        ContentPatchRelease release,
        DeclarativePatchPackage package)
    {
        var supportedProducts = DeclarativePatchProductCompatibility.ResolveSupportedProducts(package.Manifest);
        if (!package.Manifest.PackageId.Equals(catalogEntry.PackageId, StringComparison.Ordinal)
            || !package.Manifest.RepositoryUrl.TrimEnd('/').Equals(catalogEntry.RepositoryUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
            || package.Manifest.SchemaVersion != catalogEntry.Distribution.ManifestSchemaVersion
            || !supportedProducts.SetEquals(catalogEntry.SupportedProducts)
            || package.Manifest.RestartRequired != catalogEntry.RestartRequired
            || !NormalizeVersion(package.Manifest.PackageVersion).Equals(NormalizeVersion(release.Tag), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Downloaded package identity does not match the trusted catalog entry {catalogEntry.PackageId}.");
        }
    }

    private static bool ArchiveMatchesRelease(string archivePath, ContentPatchRelease release)
    {
        var info = new FileInfo(archivePath);
        if (info.Length != release.AssetSize)
        {
            return false;
        }

        using var stream = File.OpenRead(archivePath);
        var sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return sha256.Equals(release.AssetSha256, StringComparison.OrdinalIgnoreCase);
    }

    private static bool AssetNameMatches(string pattern, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var wildcard = pattern.IndexOf('*');
        var prefix = pattern[..wildcard];
        var suffix = pattern[(wildcard + 1)..];
        return name.Length >= prefix.Length + suffix.Length
            && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseSha256Digest(string? value, out string sha256)
    {
        const string prefix = "sha256:";
        sha256 = value?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true ? value[prefix.Length..] : "";
        return sha256.Length == 64 && sha256.All(Uri.IsHexDigit);
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
    {
        var unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
        return unixMode == 0xA000
            || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0;
    }

    private static bool IsSafeAssetName(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && Path.GetFileName(value) == value
        && !value.Contains('/')
        && !value.Contains('\\')
        && value.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    private static bool IsSafeSegment(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value is not "." and not ".."
        && !value.Contains('/')
        && !value.Contains('\\');

    private static string SanitizeSegment(string value)
    {
        if (!IsSafeSegment(value) || value.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '_')))
        {
            throw new InvalidDataException($"Unsafe content patch cache segment: {value}.");
        }

        return value;
    }

    private static string NormalizeVersion(string value) => value.Trim().TrimStart('v', 'V');

    private static void ValidateGitHubDistribution(ContentPackageCatalogEntry catalogEntry)
    {
        ArgumentNullException.ThrowIfNull(catalogEntry);
        if (catalogEntry.Distribution.Kind is not ContentPackageDistributionKind.GitHubReleaseArchive)
        {
            throw new InvalidOperationException($"Content package {catalogEntry.PackageId} does not use a GitHub release archive.");
        }

        var pattern = catalogEntry.Distribution.AssetNamePattern;
        if (string.IsNullOrWhiteSpace(pattern)
            || !pattern.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            || pattern.Contains('/')
            || pattern.Contains('\\')
            || pattern.Count(ch => ch == '*') != 1)
        {
            throw new InvalidOperationException($"Content package {catalogEntry.PackageId} has an unsafe release asset pattern.");
        }

        if (!Uri.TryCreate(catalogEntry.RepositoryUrl, UriKind.Absolute, out var repositoryUri)
            || repositoryUri.Scheme != Uri.UriSchemeHttps
            || !repositoryUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || repositoryUri.AbsolutePath.Trim('/').Split('/').Length != 2)
        {
            throw new InvalidOperationException($"Content package {catalogEntry.PackageId} has an unsafe GitHub repository URL.");
        }
    }

    private static void ValidateReleaseForCatalog(
        ContentPackageCatalogEntry catalogEntry,
        ContentPatchRelease release)
    {
        var (owner, repository) = ParseRepository(catalogEntry.RepositoryUrl);
        var expectedAssetPrefix = $"/{owner}/{repository}/releases/download/";
        if (!IsSafeSegment(release.Tag)
            || !IsSafeAssetName(release.AssetName)
            || !AssetNameMatches(catalogEntry.Distribution.AssetNamePattern, release.AssetName)
            || release.AssetSize is <= 0 or > MaximumArchiveBytes
            || release.AssetSha256.Length != 64
            || !release.AssetSha256.All(Uri.IsHexDigit)
            || !Uri.TryCreate(release.AssetUrl, UriKind.Absolute, out var assetUri)
            || assetUri.Scheme != Uri.UriSchemeHttps
            || !assetUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || !assetUri.AbsolutePath.StartsWith(expectedAssetPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Content patch release does not match the trusted catalog entry {catalogEntry.PackageId}.");
        }
    }

    private void CreateSafeCacheDirectory(string path)
    {
        Directory.CreateDirectory(_cacheRoot);
        var root = Path.TrimEndingDirectorySeparator(_cacheRoot);
        var fullPath = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, comparison))
        {
            throw new InvalidOperationException("Content patch cache path escapes the configured cache root.");
        }

        var relative = Path.GetRelativePath(root, fullPath);
        var current = root;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            RejectLink(current, "Content patch cache directory");
            if (File.Exists(current))
            {
                throw new InvalidOperationException($"Content patch cache directory is occupied by a file: {current}.");
            }

            Directory.CreateDirectory(current);
            RejectLink(current, "Content patch cache directory");
        }
    }

    private static void RejectLink(string path, string label)
    {
        FileSystemInfo? item = Directory.Exists(path)
            ? new DirectoryInfo(path)
            : File.Exists(path) ? new FileInfo(path) : null;
        if (item?.LinkTarget is not null)
        {
            throw new InvalidOperationException($"{label} must not be a symbolic link: {path}.");
        }
    }

    private static (string Owner, string Repository) ParseRepository(string repositoryUrl)
    {
        var uri = new Uri(repositoryUrl);
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        return (parts[0], parts[1]);
    }

    private sealed class GitHubReleaseDocument
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = "";
        public bool Draft { get; set; }
        public bool Prerelease { get; set; }
        public List<GitHubReleaseAssetDocument> Assets { get; set; } = [];
    }

    private sealed class GitHubReleaseAssetDocument
    {
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";
        public long Size { get; set; }
        public string Digest { get; set; } = "";
    }
}
