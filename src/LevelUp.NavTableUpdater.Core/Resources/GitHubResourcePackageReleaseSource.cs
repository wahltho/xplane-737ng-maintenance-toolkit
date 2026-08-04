using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using LevelUp.NavTableUpdater.Core.Content;

namespace LevelUp.NavTableUpdater.Core.Resources;

public sealed class GitHubResourcePackageReleaseSource
{
    private const int MaximumMetadataBytes = 1024 * 1024;
    private const int MaximumManifestBytes = 2 * 1024 * 1024;
    private const long MaximumArchiveBytes = 2L * 1024 * 1024 * 1024 - 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public GitHubResourcePackageReleaseSource(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
    }

    public async Task<ResourcePackageRelease?> GetLatestAsync(
        ContentPackageCatalogEntry catalogEntry,
        ResourceReleaseChannel channel,
        CancellationToken cancellationToken = default)
    {
        ValidateCatalogEntry(catalogEntry, channel);
        var (owner, repository) = ParseRepository(catalogEntry.RepositoryUrl);
        var apiUrl = $"https://api.github.com/repos/{owner}/{repository}/releases?per_page=100";
        var metadata = await DownloadMetadataAsync(apiUrl, cancellationToken).ConfigureAwait(false);
        var releases = JsonSerializer.Deserialize<List<GitHubReleaseDocument>>(metadata, JsonOptions)
            ?? throw new InvalidDataException("GitHub resource release metadata is empty.");
        var release = releases.FirstOrDefault(candidate =>
            !candidate.Draft
            && candidate.Prerelease == (channel is ResourceReleaseChannel.Beta)
            && candidate.Assets?.Any(asset =>
                AssetNameMatches(catalogEntry.Distribution.ManifestAssetNamePattern, asset.Name)) == true);
        if (release is null)
        {
            return null;
        }

        if (!ResourcePackageManifestParser.IsSafeSegment(release.TagName) || release.Assets is null)
        {
            throw new InvalidDataException($"Latest {ChannelName(channel)} resource release metadata is incomplete or unsafe.");
        }

        var manifestAssets = release.Assets
            .Where(asset => AssetNameMatches(catalogEntry.Distribution.ManifestAssetNamePattern, asset.Name))
            .ToArray();
        if (manifestAssets.Length != 1)
        {
            throw new InvalidDataException(
                $"Expected exactly one resource manifest asset matching '{catalogEntry.Distribution.ManifestAssetNamePattern}', found {manifestAssets.Length}.");
        }

        var manifestAsset = manifestAssets[0];
        ValidateAsset(owner, repository, manifestAsset, MaximumManifestBytes, ".json");
        var manifestBytes = await DownloadVerifiedAssetAsync(
            manifestAsset,
            MaximumManifestBytes,
            cancellationToken).ConfigureAwait(false);
        var manifest = ResourcePackageManifestParser.Parse(manifestBytes);

        var archiveAssets = release.Assets
            .Where(asset => string.Equals(asset.Name, manifest.Archive.FileName, StringComparison.Ordinal))
            .ToArray();
        if (archiveAssets.Length != 1)
        {
            throw new InvalidDataException(
                $"Expected exactly one resource archive asset named '{manifest.Archive.FileName}', found {archiveAssets.Length}.");
        }

        if (!AssetNameMatches(catalogEntry.Distribution.AssetNamePattern, manifest.Archive.FileName))
        {
            throw new InvalidDataException("Resource archive name does not match the trusted catalog pattern.");
        }

        var archiveAsset = archiveAssets[0];
        ValidateAsset(owner, repository, archiveAsset, MaximumArchiveBytes, ".7z");
        var archiveDigest = ParseSha256Digest(archiveAsset.Digest, "archive asset");
        if (archiveAsset.Size != manifest.Archive.Size
            || !archiveDigest.Equals(manifest.Archive.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Resource archive metadata does not match the GitHub release asset metadata.");
        }

        ValidateManifestIdentity(catalogEntry, channel, release, manifest);
        return new ResourcePackageRelease(
            channel,
            release.TagName,
            release.HtmlUrl,
            manifestAsset.Name,
            manifestAsset.BrowserDownloadUrl,
            manifestAsset.Size,
            ParseSha256Digest(manifestAsset.Digest, "manifest asset"),
            archiveAsset.BrowserDownloadUrl,
            manifest);
    }

    public async Task<ResourcePackageProvisionResult> DownloadAsync(
        ContentPackageCatalogEntry catalogEntry,
        ResourcePackageRelease release,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        ValidateCatalogEntry(catalogEntry, release.Channel);
        ValidateProvisionedRelease(catalogEntry, release);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        var directory = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(directory);
        var archivePath = Path.Combine(
            directory,
            $".{release.Manifest.Archive.FileName}.{Guid.NewGuid():N}.download");
        RejectLink(archivePath, "Temporary resource download");
        try
        {
            await DownloadArchiveAsync(release, archivePath, cancellationToken).ConfigureAwait(false);
            return new ResourcePackageProvisionResult(
                release,
                archivePath,
                Downloaded: true,
                Temporary: true);
        }
        catch
        {
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }

            throw;
        }
    }

    private async Task<byte[]> DownloadMetadataAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("XPlane737NGMaintenanceToolkit", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return await DownloadBytesAsync(request, MaximumMetadataBytes, cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]> DownloadVerifiedAssetAsync(
        GitHubReleaseAsset asset,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, asset.BrowserDownloadUrl);
        var bytes = await DownloadBytesAsync(request, maximumBytes, cancellationToken).ConfigureAwait(false);
        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (bytes.LongLength != asset.Size
            || !digest.Equals(ParseSha256Digest(asset.Digest, "release asset"), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Downloaded resource release asset failed size/SHA-256 verification: {asset.Name}.");
        }

        return bytes;
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
        if (response.Content.Headers.ContentLength is long contentLength && contentLength > maximumBytes)
        {
            throw new InvalidDataException("GitHub response exceeds the configured size limit.");
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > maximumBytes)
            {
                throw new InvalidDataException("GitHub response exceeds the configured size limit.");
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private async Task DownloadArchiveAsync(
        ResourcePackageRelease release,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, release.ArchiveAssetUrl);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long contentLength
            && contentLength != release.Manifest.Archive.Size)
        {
            throw new InvalidDataException("Resource archive response size does not match its manifest.");
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > MaximumArchiveBytes || total > release.Manifest.Archive.Size)
            {
                throw new InvalidDataException("Downloaded resource archive exceeds its declared size.");
            }

            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (total != release.Manifest.Archive.Size
            || !actualHash.Equals(release.Manifest.Archive.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Downloaded resource archive failed size/SHA-256 verification.");
        }
    }

    private static void ValidateManifestIdentity(
        ContentPackageCatalogEntry catalogEntry,
        ResourceReleaseChannel channel,
        GitHubReleaseDocument release,
        ResourcePackageManifest manifest)
    {
        if (!manifest.PackageId.Equals(catalogEntry.PackageId, StringComparison.Ordinal)
            || !manifest.ReleaseTag.Equals(release.TagName, StringComparison.Ordinal)
            || !manifest.Repository.TrimEnd('/').Equals(catalogEntry.RepositoryUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
            || manifest.SchemaVersion != catalogEntry.Distribution.ManifestSchemaVersion
            || !manifest.Channel.Equals(ChannelName(channel), StringComparison.Ordinal)
            || !manifest.SupportedProducts.ToHashSet(StringComparer.Ordinal)
                .SetEquals(catalogEntry.SupportedProducts))
        {
            throw new InvalidDataException($"Resource manifest identity does not match the trusted catalog entry {catalogEntry.PackageId}.");
        }
    }

    private static void ValidateProvisionedRelease(
        ContentPackageCatalogEntry catalogEntry,
        ResourcePackageRelease release)
    {
        if (!ResourcePackageManifestParser.IsSafeSegment(release.Tag)
            || !catalogEntry.SupportedChannels.Contains(ChannelName(release.Channel), StringComparer.Ordinal)
            || release.ManifestAssetSize is <= 0 or > MaximumManifestBytes
            || !ResourcePackageManifestParser.IsSha256(release.ManifestAssetSha256)
            || !IsSafeGitHubReleaseAssetUrl(catalogEntry.RepositoryUrl, release.ManifestAssetUrl)
            || !IsSafeGitHubReleaseAssetUrl(catalogEntry.RepositoryUrl, release.ArchiveAssetUrl))
        {
            throw new InvalidDataException($"Resource release does not match the trusted catalog entry {catalogEntry.PackageId}.");
        }

        ValidateManifestIdentity(
            catalogEntry,
            release.Channel,
            new GitHubReleaseDocument { TagName = release.Tag, Prerelease = release.Channel is ResourceReleaseChannel.Beta },
            release.Manifest);
    }

    private static void ValidateCatalogEntry(ContentPackageCatalogEntry entry, ResourceReleaseChannel channel)
    {
        if (entry.Category is not ContentPackageCategory.Resource
            || entry.Distribution.Kind is not ContentPackageDistributionKind.GitHubResourceRelease
            || !entry.SupportedChannels.Contains(ChannelName(channel), StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Catalog entry {entry.PackageId} is not configured for the requested resource release channel.");
        }
    }

    private static void ValidateAsset(
        string owner,
        string repository,
        GitHubReleaseAsset asset,
        long maximumSize,
        string suffix)
    {
        if (!ResourcePackageManifestParser.IsSafeFileName(asset.Name, suffix)
            || asset.Size is <= 0 || asset.Size > maximumSize
            || !ResourcePackageManifestParser.IsSha256(ParseSha256Digest(asset.Digest, "release asset"))
            || !IsSafeGitHubReleaseAssetUrl(owner, repository, asset.BrowserDownloadUrl))
        {
            throw new InvalidDataException($"GitHub resource release asset metadata is incomplete or unsafe: {asset.Name}.");
        }
    }

    private static bool IsSafeGitHubReleaseAssetUrl(string repositoryUrl, string assetUrl)
    {
        var (owner, repository) = ParseRepository(repositoryUrl);
        return IsSafeGitHubReleaseAssetUrl(owner, repository, assetUrl);
    }

    private static bool IsSafeGitHubReleaseAssetUrl(string owner, string repository, string assetUrl) =>
        Uri.TryCreate(assetUrl, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.StartsWith($"/{owner}/{repository}/releases/download/", StringComparison.OrdinalIgnoreCase);

    private static (string Owner, string Repository) ParseRepository(string repositoryUrl)
    {
        var uri = new Uri(repositoryUrl, UriKind.Absolute);
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        return (parts[0], parts[1]);
    }

    private static string ParseSha256Digest(string? value, string label)
    {
        const string prefix = "sha256:";
        var hash = value?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true
            ? value[prefix.Length..].ToLowerInvariant()
            : "";
        if (!ResourcePackageManifestParser.IsSha256(hash))
        {
            throw new InvalidDataException($"GitHub {label} has no valid SHA-256 digest.");
        }

        return hash;
    }

    private static bool AssetNameMatches(string pattern, string name)
    {
        var separator = pattern.IndexOf('*');
        return separator >= 0
            && name.StartsWith(pattern[..separator], StringComparison.Ordinal)
            && name.EndsWith(pattern[(separator + 1)..], StringComparison.Ordinal);
    }

    private static void RejectLink(string path, string label)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return;
        }

        var info = Directory.Exists(path) ? new DirectoryInfo(path) as FileSystemInfo : new FileInfo(path);
        if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidDataException($"{label} must not be a symbolic link or reparse point: {path}.");
        }
    }

    private static string ChannelName(ResourceReleaseChannel channel) =>
        channel is ResourceReleaseChannel.Beta ? "beta" : "stable";

    private sealed class GitHubReleaseDocument
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = "";

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubReleaseAsset>? Assets { get; set; }
    }

    private sealed class GitHubReleaseAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("digest")]
        public string? Digest { get; set; }
    }
}
