using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using LevelUp.NavTableUpdater.Core.Content;

namespace LevelUp.NavTableUpdater.Core.Tools;

public sealed class GitHubToolPackageReleaseSource
{
    private const int MaximumMetadataBytes = 1024 * 1024;
    private const int MaximumManifestBytes = 2 * 1024 * 1024;
    private const long MaximumArchiveBytes = 256L * 1024 * 1024;
    private const long MaximumExpandedBytes = 512L * 1024 * 1024;
    private const int MaximumArchiveEntries = 4096;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly string _cacheRoot;

    public GitHubToolPackageReleaseSource(HttpClient httpClient, string cacheRoot)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);
        _httpClient = httpClient;
        _cacheRoot = Path.GetFullPath(cacheRoot);
        Directory.CreateDirectory(_cacheRoot);
        RejectLink(_cacheRoot, "Tool package cache root");
    }

    public async Task<ToolPackageRelease?> GetLatestAsync(
        ContentPackageCatalogEntry catalogEntry,
        ToolReleaseChannel channel,
        CancellationToken cancellationToken = default)
    {
        ValidateCatalogEntry(catalogEntry, channel);
        var (owner, repository) = ParseRepository(catalogEntry.RepositoryUrl);
        var apiUrl = channel is ToolReleaseChannel.Stable
            ? $"https://api.github.com/repos/{owner}/{repository}/releases/latest"
            : $"https://api.github.com/repos/{owner}/{repository}/releases?per_page=30";
        var metadata = await DownloadMetadataAsync(apiUrl, cancellationToken).ConfigureAwait(false);
        var releases = ParseReleases(metadata, channel);
        var release = releases.FirstOrDefault(candidate =>
            !candidate.Draft
            && candidate.Prerelease == (channel is ToolReleaseChannel.Beta));
        if (release is null)
        {
            return null;
        }

        if (!IsSafeSegment(release.TagName) || release.Assets is null)
        {
            throw new InvalidDataException($"Latest {ChannelName(channel)} release metadata is incomplete or unsafe.");
        }

        var manifestAssets = release.Assets
            .Where(asset => AssetNameMatches(catalogEntry.Distribution.ManifestAssetNamePattern, asset.Name))
            .ToArray();
        if (manifestAssets.Length != 1)
        {
            throw new InvalidDataException(
                $"Expected exactly one manifest asset matching '{catalogEntry.Distribution.ManifestAssetNamePattern}', found {manifestAssets.Length}.");
        }

        var manifestAsset = manifestAssets[0];
        ValidateAsset(owner, repository, manifestAsset, MaximumManifestBytes, ".json");
        var manifestBytes = await DownloadVerifiedAssetAsync(manifestAsset, MaximumManifestBytes, cancellationToken).ConfigureAwait(false);
        var manifest = ToolPackageManifestParser.Parse(manifestBytes);

        var archiveAssets = release.Assets
            .Where(asset => string.Equals(asset.Name, manifest.Archive.FileName, StringComparison.Ordinal))
            .ToArray();
        if (archiveAssets.Length != 1)
        {
            throw new InvalidDataException($"Expected exactly one archive asset named '{manifest.Archive.FileName}', found {archiveAssets.Length}.");
        }

        var archiveAsset = archiveAssets[0];
        ValidateAsset(owner, repository, archiveAsset, MaximumArchiveBytes, ".zip");
        var archiveDigest = ParseSha256Digest(archiveAsset.Digest, "archive asset");
        if (archiveAsset.Size != manifest.Archive.Size
            || !archiveDigest.Equals(manifest.Archive.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Tool archive metadata does not match the signed GitHub release asset metadata.");
        }

        ValidateManifestIdentity(catalogEntry, channel, release, manifest);
        return new ToolPackageRelease(
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

    public async Task<ToolPackageProvisionResult> ProvisionAsync(
        ContentPackageCatalogEntry catalogEntry,
        ToolPackageRelease release,
        CancellationToken cancellationToken = default)
    {
        ValidateCatalogEntry(catalogEntry, release.Channel);
        ValidateProvisionedRelease(catalogEntry, release);
        var releaseRoot = Path.Combine(
            _cacheRoot,
            "tool-packages",
            SanitizeSegment(catalogEntry.PackageId),
            ChannelName(release.Channel),
            SanitizeSegment(release.Tag));
        CreateSafeCacheDirectory(releaseRoot);

        var archivePath = Path.Combine(releaseRoot, release.Manifest.Archive.FileName);
        RejectLink(archivePath, "Tool package cache archive");
        var downloaded = false;
        if (!File.Exists(archivePath) || !FileMatches(archivePath, release.Manifest.Archive.Size, release.Manifest.Archive.Sha256))
        {
            await DownloadArchiveAsync(release, archivePath, cancellationToken).ConfigureAwait(false);
            downloaded = true;
        }

        var packageDirectory = Path.Combine(releaseRoot, "package");
        RejectLink(packageDirectory, "Tool package cache directory");
        if (Directory.Exists(packageDirectory))
        {
            try
            {
                ValidateExtractedPackage(packageDirectory, release.Manifest);
                return new ToolPackageProvisionResult(release, packageDirectory, downloaded);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Directory.Delete(packageDirectory, recursive: true);
            }
        }

        var tempDirectory = packageDirectory + $".tmp-{Guid.NewGuid():N}";
        try
        {
            Directory.CreateDirectory(tempDirectory);
            ExtractArchive(archivePath, tempDirectory, release.Manifest, cancellationToken);
            ValidateExtractedPackage(tempDirectory, release.Manifest);
            Directory.Move(tempDirectory, packageDirectory);
            return new ToolPackageProvisionResult(release, packageDirectory, downloaded);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
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
            throw new InvalidDataException($"Downloaded release asset failed size/SHA-256 verification: {asset.Name}.");
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
        ToolPackageRelease release,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var tempPath = destinationPath + $".download-{Guid.NewGuid():N}";
        try
        {
            using var response = await _httpClient.GetAsync(
                release.ArchiveAssetUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > 0
                && response.Content.Headers.ContentLength != release.Manifest.Archive.Size)
            {
                throw new InvalidDataException("Tool archive HTTP size differs from the release manifest.");
            }

            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long written = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    written += read;
                    if (written > MaximumArchiveBytes || written > release.Manifest.Archive.Size)
                    {
                        throw new InvalidDataException("Downloaded tool archive exceeds its declared size.");
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }

            if (!FileMatches(tempPath, release.Manifest.Archive.Size, release.Manifest.Archive.Sha256))
            {
                throw new InvalidDataException("Downloaded tool archive failed size/SHA-256 verification.");
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

    private static void ExtractArchive(
        string archivePath,
        string destinationRoot,
        ToolPackageManifest manifest,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > MaximumArchiveEntries)
        {
            throw new InvalidDataException("Tool archive contains too many entries.");
        }

        var expected = manifest.Files.ToDictionary(file => file.Path, PathComparer);
        var seen = new HashSet<string>(PathComparer);
        long expandedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            if (IsSymbolicLink(entry))
            {
                throw new InvalidDataException($"Tool archive contains a symbolic link: {entry.FullName}.");
            }

            var archivePathNormalized = NormalizeArchivePath(entry.FullName);
            var prefix = manifest.Archive.RootPath + "/";
            if (!archivePathNormalized.StartsWith(prefix, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Tool archive entry is outside the declared root: {entry.FullName}.");
            }

            var relativePath = archivePathNormalized[prefix.Length..];
            if (!expected.TryGetValue(relativePath, out var declared) || !seen.Add(relativePath))
            {
                throw new InvalidDataException($"Tool archive contains an undeclared or duplicate file: {relativePath}.");
            }

            expandedBytes += entry.Length;
            if (entry.Length != declared.Size || expandedBytes > MaximumExpandedBytes)
            {
                throw new InvalidDataException($"Tool archive entry has an invalid size: {relativePath}.");
            }

            var destination = ResolvePath(destinationRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var input = entry.Open();
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                output.Write(buffer, 0, read);
                sha256.AppendData(buffer, 0, read);
            }

            var digest = Convert.ToHexString(sha256.GetHashAndReset()).ToLowerInvariant();
            if (!digest.Equals(declared.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Tool archive entry failed SHA-256 verification: {relativePath}.");
            }

            ApplyUnixMode(entry, destination);
        }

        var missing = expected.Keys.Where(path => !seen.Contains(path)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidDataException($"Tool archive is missing {missing.Length} manifest file(s), including {missing[0]}.");
        }
    }

    internal static void ValidateExtractedPackage(string packageDirectory, ToolPackageManifest manifest)
    {
        var expected = manifest.Files.ToDictionary(file => file.Path, PathComparer);
        var actual = new HashSet<string>(PathComparer);
        foreach (var path in EnumerateFilesWithoutLinks(packageDirectory))
        {
            var relativePath = ToolPackageManifestParser.NormalizeRelativePath(
                Path.GetRelativePath(packageDirectory, path));
            if (!expected.ContainsKey(relativePath) || !actual.Add(relativePath))
            {
                throw new InvalidDataException($"Extracted tool package contains an undeclared or duplicate file: {relativePath}.");
            }
        }

        if (actual.Count != expected.Count)
        {
            var missing = expected.Keys.First(path => !actual.Contains(path));
            throw new InvalidDataException($"Extracted tool package is missing manifest file: {missing}.");
        }

        foreach (var file in manifest.Files)
        {
            var path = ResolvePath(packageDirectory, file.Path);
            if (!FileMatches(path, file.Size, file.Sha256))
            {
                throw new InvalidDataException($"Extracted tool package file failed size/SHA-256 verification: {file.Path}.");
            }
        }
    }

    private static IEnumerable<string> EnumerateFilesWithoutLinks(string root)
    {
        RejectLink(root, "Tool package directory");
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var item in Directory.EnumerateFileSystemEntries(directory))
            {
                RejectLink(item, "Tool package path");
                if (Directory.Exists(item))
                {
                    pending.Push(item);
                }
                else
                {
                    yield return item;
                }
            }
        }
    }

    private static IReadOnlyList<GitHubReleaseDocument> ParseReleases(byte[] json, ToolReleaseChannel channel)
    {
        try
        {
            if (channel is ToolReleaseChannel.Stable)
            {
                var release = JsonSerializer.Deserialize<GitHubReleaseDocument>(json, JsonOptions)
                    ?? throw new InvalidDataException("GitHub stable release metadata is empty.");
                return [release];
            }

            return JsonSerializer.Deserialize<List<GitHubReleaseDocument>>(json, JsonOptions)
                ?? throw new InvalidDataException("GitHub beta release metadata is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"GitHub tool release metadata is invalid JSON: {ex.Message}", ex);
        }
    }

    private static void ValidateManifestIdentity(
        ContentPackageCatalogEntry catalogEntry,
        ToolReleaseChannel channel,
        GitHubReleaseDocument release,
        ToolPackageManifest manifest)
    {
        var expectedProducts = catalogEntry.SupportedProducts.ToHashSet(StringComparer.Ordinal);
        var actualProducts = manifest.SupportedProducts.ToHashSet(StringComparer.Ordinal);
        if (!manifest.PackageId.Equals(catalogEntry.PackageId, StringComparison.Ordinal)
            || !manifest.ReleaseTag.Equals(release.TagName, StringComparison.Ordinal)
            || !NormalizeVersion(manifest.PackageVersion).Equals(NormalizeVersion(release.TagName), StringComparison.OrdinalIgnoreCase)
            || !manifest.Repository.TrimEnd('/').Equals(catalogEntry.RepositoryUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
            || !manifest.InstallScope.Equals(catalogEntry.InstallScope, StringComparison.Ordinal)
            || !manifest.TargetPath.Equals(catalogEntry.TargetPath, StringComparison.Ordinal)
            || manifest.SchemaVersion != catalogEntry.Distribution.ManifestSchemaVersion
            || manifest.RestartRequired != catalogEntry.RestartRequired
            || !actualProducts.SetEquals(expectedProducts)
            || !manifest.Channel.Equals(ChannelName(channel), StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Tool manifest identity does not match the trusted catalog entry {catalogEntry.PackageId}.");
        }
    }

    private static void ValidateProvisionedRelease(ContentPackageCatalogEntry catalogEntry, ToolPackageRelease release)
    {
        if (!IsSafeSegment(release.Tag)
            || !catalogEntry.SupportedChannels.Contains(ChannelName(release.Channel), StringComparer.Ordinal)
            || release.ManifestAssetSize is <= 0 or > MaximumManifestBytes
            || !IsSha256(release.ManifestAssetSha256)
            || !IsSafeGitHubReleaseAssetUrl(catalogEntry.RepositoryUrl, release.ManifestAssetUrl)
            || !IsSafeGitHubReleaseAssetUrl(catalogEntry.RepositoryUrl, release.ArchiveAssetUrl))
        {
            throw new InvalidDataException($"Tool release does not match the trusted catalog entry {catalogEntry.PackageId}.");
        }

        ValidateManifestIdentity(
            catalogEntry,
            release.Channel,
            new GitHubReleaseDocument
            {
                TagName = release.Tag,
                Prerelease = release.Channel is ToolReleaseChannel.Beta
            },
            release.Manifest);
    }

    private static void ValidateCatalogEntry(ContentPackageCatalogEntry entry, ToolReleaseChannel channel)
    {
        var supportedCategory = entry.Category is ContentPackageCategory.Tool
            or ContentPackageCategory.AircraftComponent;
        if (!supportedCategory
            || entry.Distribution.Kind is not ContentPackageDistributionKind.GitHubToolRelease
            || !entry.SupportedChannels.Contains(ChannelName(channel), StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Catalog entry {entry.PackageId} is not configured for the requested tool release channel.");
        }
    }

    private static void ValidateAsset(
        string owner,
        string repository,
        GitHubReleaseAsset asset,
        long maximumSize,
        string suffix)
    {
        if (!IsSafeFileName(asset.Name, suffix)
            || asset.Size is <= 0 || asset.Size > maximumSize
            || !IsSha256(ParseSha256Digest(asset.Digest, "release asset"))
            || !IsSafeGitHubReleaseAssetUrl(owner, repository, asset.BrowserDownloadUrl))
        {
            throw new InvalidDataException($"GitHub release asset metadata is incomplete or unsafe: {asset.Name}.");
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
        if (!IsSha256(hash))
        {
            throw new InvalidDataException($"GitHub {label} has no valid SHA-256 digest.");
        }

        return hash;
    }

    private static bool FileMatches(string path, long size, string sha256)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != size)
        {
            return false;
        }

        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return actual.Equals(sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolvePath(string root, string relativePath)
    {
        var normalized = ToolPackageManifestParser.NormalizeRelativePath(relativePath);
        var rootPath = Path.GetFullPath(root);
        var path = Path.GetFullPath(Path.Combine(rootPath, Path.Combine(normalized.Split('/'))));
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(rootPath) + Path.DirectorySeparatorChar, comparison))
        {
            throw new InvalidDataException($"Tool package path escapes its root: {relativePath}.");
        }

        RejectNestedLinks(rootPath, path);
        return path;
    }

    private static string NormalizeArchivePath(string value)
    {
        var normalized = value.Replace('\\', '/').TrimEnd('/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith('/') || normalized.Contains(':'))
        {
            throw new InvalidDataException($"Unsafe tool archive path: {value}.");
        }

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(part => part is "." or ".."))
        {
            throw new InvalidDataException($"Unsafe tool archive path: {value}.");
        }

        return string.Join('/', parts);
    }

    private void CreateSafeCacheDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!fullPath.StartsWith(Path.TrimEndingDirectorySeparator(_cacheRoot) + Path.DirectorySeparatorChar, comparison))
        {
            throw new InvalidDataException("Tool package cache path escapes its configured root.");
        }

        var current = _cacheRoot;
        foreach (var part in Path.GetRelativePath(_cacheRoot, fullPath).Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            RejectLink(current, "Tool package cache path");
            Directory.CreateDirectory(current);
            RejectLink(current, "Tool package cache path");
        }
    }

    private static void RejectNestedLinks(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        var current = root;
        foreach (var part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part is "" or ".")
            {
                continue;
            }

            current = Path.Combine(current, part);
            RejectLink(current, "Tool package path");
        }
    }

    private static void RejectLink(string path, string label)
    {
        if (new FileInfo(path).LinkTarget is not null
            || new DirectoryInfo(path).LinkTarget is not null)
        {
            throw new InvalidDataException($"{label} is a symbolic link: {path}.");
        }
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
    {
        var unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
        return unixMode == 0xA000
            || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0;
    }

    private static void ApplyUnixMode(ZipArchiveEntry entry, string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var mode = (entry.ExternalAttributes >> 16) & 0x1FF;
        if (mode != 0)
        {
            File.SetUnixFileMode(path, (UnixFileMode)mode);
        }
    }

    private static bool AssetNameMatches(string pattern, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var wildcard = pattern.IndexOf('*');
        if (wildcard < 0)
        {
            return false;
        }

        var prefix = pattern[..wildcard];
        var suffix = pattern[(wildcard + 1)..];
        return name.Length >= prefix.Length + suffix.Length
            && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSafeFileName(string? value, string suffix) =>
        !string.IsNullOrWhiteSpace(value)
        && Path.GetFileName(value) == value
        && !value.Contains('/')
        && !value.Contains('\\')
        && value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);

    private static bool IsSafeSegment(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value is not "." and not ".."
        && !value.Contains('/')
        && !value.Contains('\\');

    private static string SanitizeSegment(string value)
    {
        if (!IsSafeSegment(value) || value.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '_')))
        {
            throw new InvalidDataException($"Unsafe tool cache segment: {value}.");
        }

        return value;
    }

    private static bool IsSha256(string? value) =>
        value?.Length == 64 && value.All(Uri.IsHexDigit);

    private static string NormalizeVersion(string value)
    {
        var normalized = value.Trim();
        return normalized.Length > 1
            && normalized[0] is 'v' or 'V' or 'r' or 'R'
            && char.IsDigit(normalized[1])
                ? normalized[1..]
                : normalized;
    }

    private static string ChannelName(ToolReleaseChannel channel) =>
        channel is ToolReleaseChannel.Stable ? "stable" : "beta";

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

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
