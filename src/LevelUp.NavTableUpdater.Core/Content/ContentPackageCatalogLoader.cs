using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LevelUp.NavTableUpdater.Core.Content;

public enum ContentPackageCatalogOrigin
{
    RemoteRelease,
    LastKnownGoodCache,
    BundledFallback
}

public sealed record ContentPackageCatalogLoadResult(
    ContentPackageCatalog Catalog,
    ContentPackageCatalogOrigin Origin,
    string Detail,
    string ReleaseTag = "");

public sealed class ContentPackageCatalogLoader
{
    private static readonly TimeSpan RemoteRequestTimeout = TimeSpan.FromSeconds(15);

    public const string DefaultRepositoryUrl =
        "https://github.com/wahltho/xplane-737ng-maintenance-toolkit";

    public const string CatalogAssetName = "content-package-catalog.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly string _repositoryUrl;
    private readonly string _releaseApiUrl;
    private readonly string _cachePath;
    private readonly Version _toolkitVersion;

    public ContentPackageCatalogLoader(
        HttpClient httpClient,
        string cacheRoot,
        Version toolkitVersion,
        string repositoryUrl = DefaultRepositoryUrl,
        string? releaseApiUrl = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _toolkitVersion = toolkitVersion ?? throw new ArgumentNullException(nameof(toolkitVersion));
        _repositoryUrl = NormalizeRepositoryUrl(repositoryUrl);
        _releaseApiUrl = releaseApiUrl ?? BuildReleaseApiUrl(_repositoryUrl);
        _cachePath = Path.Combine(Path.GetFullPath(cacheRoot), CatalogAssetName);
    }

    public string CachePath => _cachePath;

    public async Task<ContentPackageCatalogLoadResult> LoadAsync(
        string bundledCatalogJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundledCatalogJson);
        var bundled = ContentPackageCatalog.Parse(bundledCatalogJson, _toolkitVersion);
        string remoteFailure;

        try
        {
            using var remoteCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            remoteCancellation.CancelAfter(RemoteRequestTimeout);
            var remote = await DownloadLatestAsync(remoteCancellation.Token).ConfigureAwait(false);
            WriteCache(remote.Json);
            return new ContentPackageCatalogLoadResult(
                remote.Catalog,
                ContentPackageCatalogOrigin.RemoteRelease,
                $"Loaded catalog {remote.Catalog.CatalogVersion} from {remote.Tag}.",
                remote.Tag);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsRecoverable(ex))
        {
            remoteFailure = ex.Message;
        }

        try
        {
            if (File.Exists(_cachePath))
            {
                var cachedJson = File.ReadAllText(_cachePath, Encoding.UTF8);
                var cached = ContentPackageCatalog.Parse(cachedJson, _toolkitVersion);
                ValidatePublishedCatalog(cached, "cached catalog");
                return new ContentPackageCatalogLoadResult(
                    cached,
                    ContentPackageCatalogOrigin.LastKnownGoodCache,
                    $"Remote catalog unavailable ({remoteFailure}); using cached catalog {cached.CatalogVersion}.");
            }
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            return new ContentPackageCatalogLoadResult(
                bundled,
                ContentPackageCatalogOrigin.BundledFallback,
                $"Remote catalog unavailable ({remoteFailure}); cached catalog rejected ({ex.Message}); "
                + $"using bundled catalog {bundled.CatalogVersion}.");
        }

        return new ContentPackageCatalogLoadResult(
            bundled,
            ContentPackageCatalogOrigin.BundledFallback,
            $"Remote catalog unavailable ({remoteFailure}); using bundled catalog {bundled.CatalogVersion}.");
    }

    private async Task<RemoteCatalog> DownloadLatestAsync(CancellationToken cancellationToken)
    {
        using var releaseRequest = CreateRequest(_releaseApiUrl);
        using var releaseResponse = await _httpClient.SendAsync(releaseRequest, cancellationToken).ConfigureAwait(false);
        releaseResponse.EnsureSuccessStatusCode();
        var releaseJson = await releaseResponse.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var releases = JsonSerializer.Deserialize<List<GitHubReleaseDocument>>(releaseJson, JsonOptions)
            ?? throw new InvalidDataException("GitHub catalog release response is empty.");
        var release = releases
            .Where(candidate => !candidate.Draft && !candidate.Prerelease)
            .Select(candidate => new { Release = candidate, Version = ParseCatalogTag(candidate.TagName) })
            .Where(candidate => candidate.Version is not null)
            .OrderByDescending(candidate => candidate.Version)
            .Select(candidate => candidate.Release)
            .FirstOrDefault()
            ?? throw new InvalidDataException("No stable catalog-v* GitHub Release was found.");
        var tagVersion = ParseCatalogTag(release.TagName)
            ?? throw new InvalidDataException("Selected catalog release has an invalid tag.");
        var asset = release.Assets.SingleOrDefault(candidate =>
                candidate.Name.Equals(CatalogAssetName, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"Catalog release {release.TagName} has no {CatalogAssetName} asset.");
        ValidateAssetUrl(release.TagName, asset.BrowserDownloadUrl);

        using var assetRequest = CreateRequest(asset.BrowserDownloadUrl);
        using var assetResponse = await _httpClient.SendAsync(assetRequest, cancellationToken).ConfigureAwait(false);
        assetResponse.EnsureSuccessStatusCode();
        var bytes = await assetResponse.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (asset.Size < 0 || bytes.LongLength != asset.Size)
        {
            throw new InvalidDataException($"Catalog asset size does not match GitHub metadata for {release.TagName}.");
        }

        ValidateDigest(asset.Digest, bytes, release.TagName);
        var json = new UTF8Encoding(false, true).GetString(bytes);
        var catalog = ContentPackageCatalog.Parse(json, _toolkitVersion);
        ValidatePublishedCatalog(catalog, $"catalog release {release.TagName}");
        if (!Version.TryParse(catalog.CatalogVersion, out var catalogVersion)
            || catalogVersion != tagVersion)
        {
            throw new InvalidDataException(
                $"Catalog version {catalog.CatalogVersion} does not match release tag {release.TagName}.");
        }

        return new RemoteCatalog(catalog, json, release.TagName);
    }

    private void WriteCache(string json)
    {
        var directory = Path.GetDirectoryName(_cachePath)
            ?? throw new InvalidOperationException("Content catalog cache path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = _cachePath + $".tmp-{Guid.NewGuid():N}";
        try
        {
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            File.Move(temporaryPath, _cachePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private HttpRequestMessage CreateRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("XPlane737NGMaintenanceToolkit", _toolkitVersion.ToString(3)));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return request;
    }

    private void ValidateAssetUrl(string tag, string assetUrl)
    {
        if (!Uri.TryCreate(assetUrl, UriKind.Absolute, out var assetUri)
            || !Uri.TryCreate(_repositoryUrl, UriKind.Absolute, out var repositoryUri)
            || !assetUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !assetUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || !assetUri.Query.Equals(string.Empty, StringComparison.Ordinal)
            || !assetUri.Fragment.Equals(string.Empty, StringComparison.Ordinal)
            || !assetUri.AbsolutePath.Equals(
                $"{repositoryUri.AbsolutePath}/releases/download/{tag}/{CatalogAssetName}",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Catalog release {tag} uses an untrusted asset URL.");
        }
    }

    private static void ValidateDigest(string digest, byte[] bytes, string tag)
    {
        if (string.IsNullOrWhiteSpace(digest))
        {
            return;
        }

        const string prefix = "sha256:";
        if (!digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || digest.Length != prefix.Length + 64)
        {
            throw new InvalidDataException($"Catalog release {tag} has an unsupported asset digest.");
        }

        var actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!actual.Equals(digest[prefix.Length..], StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Catalog asset SHA-256 does not match GitHub metadata for {tag}.");
        }
    }

    private static Version? ParseCatalogTag(string tag)
    {
        const string prefix = "catalog-v";
        return tag.StartsWith(prefix, StringComparison.Ordinal)
            && Version.TryParse(tag[prefix.Length..], out var version)
                ? version
                : null;
    }

    private static void ValidatePublishedCatalog(ContentPackageCatalog catalog, string source)
    {
        if (string.IsNullOrWhiteSpace(catalog.MinimumToolkitVersion))
        {
            throw new InvalidDataException($"The {source} has no minimumToolkitVersion.");
        }
    }

    private static string NormalizeRepositoryUrl(string repositoryUrl)
    {
        if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0
            || uri.AbsolutePath.Trim('/').Split('/').Length != 2)
        {
            throw new ArgumentException("Content catalog repository must be a canonical HTTPS GitHub repository URL.", nameof(repositoryUrl));
        }

        return $"https://github.com/{uri.AbsolutePath.Trim('/')}";
    }

    private static string BuildReleaseApiUrl(string repositoryUrl)
    {
        var repositoryPath = new Uri(repositoryUrl).AbsolutePath.Trim('/');
        return $"https://api.github.com/repos/{repositoryPath}/releases?per_page=100";
    }

    private static bool IsRecoverable(Exception ex) =>
        ex is HttpRequestException
            or IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or JsonException
            or DecoderFallbackException
            or InvalidOperationException
            or ArgumentException
            or FormatException
            or CryptographicException
            or OperationCanceledException
            or TimeoutException;

    private sealed record RemoteCatalog(ContentPackageCatalog Catalog, string Json, string Tag);

    private sealed class GitHubReleaseDocument
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubReleaseAssetDocument> Assets { get; set; } = [];
    }

    private sealed class GitHubReleaseAssetDocument
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("digest")]
        public string Digest { get; set; } = "";
    }
}
