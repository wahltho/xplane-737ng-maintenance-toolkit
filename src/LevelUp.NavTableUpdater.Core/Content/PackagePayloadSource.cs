using System.Net;
using System.Security.Cryptography;
using LevelUp.NavTableUpdater.Core.Manifest;

namespace LevelUp.NavTableUpdater.Core.Content;

public interface IPackagePayloadSource
{
    Task<IReadOnlyDictionary<string, PackagePayload>> GetPayloadsAsync(
        PackageManifest manifest,
        CancellationToken cancellationToken = default);
}

public sealed record PackagePayload(string FileName, byte[] Bytes, string Source)
{
    public string Text => new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true).GetString(Bytes);
}

public sealed class CompositePackagePayloadSource : IPackagePayloadSource
{
    private readonly IReadOnlyList<IPackagePayloadSource> _sources;

    public CompositePackagePayloadSource(params IPackagePayloadSource[] sources)
    {
        _sources = sources;
    }

    public async Task<IReadOnlyDictionary<string, PackagePayload>> GetPayloadsAsync(
        PackageManifest manifest,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        foreach (var source in _sources)
        {
            try
            {
                return await source.GetPayloadsAsync(manifest, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or InvalidOperationException)
            {
                errors.Add($"{source.GetType().Name}: {ex.Message}");
            }
        }

        throw new InvalidOperationException($"No package payload source could provide {manifest.PackageId}. {string.Join(" | ", errors)}");
    }
}

public sealed class LocalDirectoryPackagePayloadSource : IPackagePayloadSource
{
    private readonly IReadOnlyList<string> _directories;

    public LocalDirectoryPackagePayloadSource(IEnumerable<string> directories)
    {
        _directories = directories
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select(Path.GetFullPath)
            .Distinct(StringComparerForCurrentPlatform())
            .ToArray();
    }

    public Task<IReadOnlyDictionary<string, PackagePayload>> GetPayloadsAsync(
        PackageManifest manifest,
        CancellationToken cancellationToken = default)
    {
        foreach (var directory in _directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(directory))
            {
                continue;
            }

            var payloads = TryLoadFromDirectory(directory, manifest);
            if (payloads is not null)
            {
                return Task.FromResult<IReadOnlyDictionary<string, PackagePayload>>(payloads);
            }
        }

        throw new InvalidOperationException("No local package directory contains all manifest payload files with matching size and SHA-256.");
    }

    private static IReadOnlyDictionary<string, PackagePayload>? TryLoadFromDirectory(string directory, PackageManifest manifest)
    {
        var result = new Dictionary<string, PackagePayload>(StringComparer.OrdinalIgnoreCase);
        foreach (var payload in manifest.Payloads)
        {
            var path = Path.Combine(directory, payload.FileName);
            if (!File.Exists(path))
            {
                return null;
            }

            var bytes = File.ReadAllBytes(path);
            PackagePayloadValidator.ValidatePayload(payload, bytes, path);
            result[payload.FileName] = new PackagePayload(payload.FileName, bytes, path);
        }

        return result;
    }

    private static StringComparer StringComparerForCurrentPlatform() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}

public sealed class GitHubReleasePackagePayloadSource : IPackagePayloadSource
{
    private readonly HttpClient _httpClient;

    public GitHubReleasePackagePayloadSource(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("XPlane737NGMaintenanceToolkit/0.2");
        }
    }

    public async Task<IReadOnlyDictionary<string, PackagePayload>> GetPayloadsAsync(
        PackageManifest manifest,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = BuildReleaseAssetBaseUrl(manifest);
        var result = new Dictionary<string, PackagePayload>(StringComparer.OrdinalIgnoreCase);
        foreach (var payload in manifest.Payloads)
        {
            var url = $"{baseUrl}/{Uri.EscapeDataString(payload.FileName)}";
            var bytes = await DownloadAsync(url, cancellationToken).ConfigureAwait(false);
            PackagePayloadValidator.ValidatePayload(payload, bytes, url);
            result[payload.FileName] = new PackagePayload(payload.FileName, bytes, url);
        }

        return result;
    }

    private async Task<byte[]> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new HttpRequestException($"GitHub Release asset was not found: {url}");
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string BuildReleaseAssetBaseUrl(PackageManifest manifest)
    {
        if (!Uri.TryCreate(manifest.RepositoryUrl, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported package repository URL: {manifest.RepositoryUrl}");
        }

        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            throw new InvalidOperationException($"GitHub repository URL must include owner and repo: {manifest.RepositoryUrl}");
        }

        if (string.IsNullOrWhiteSpace(manifest.ReleaseTag))
        {
            throw new InvalidOperationException("Manifest release tag is missing.");
        }

        return $"https://github.com/{parts[0]}/{parts[1]}/releases/download/{Uri.EscapeDataString(manifest.ReleaseTag)}";
    }
}

public static class PackagePayloadValidator
{
    public static void ValidatePayload(PayloadFile payload, byte[] bytes, string source)
    {
        if (bytes.LongLength != payload.Size)
        {
            throw new InvalidOperationException($"{payload.FileName}: expected {payload.Size} bytes, got {bytes.LongLength} bytes from {source}.");
        }

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!hash.Equals(payload.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{payload.FileName}: expected SHA-256 {payload.Sha256}, got {hash} from {source}.");
        }
    }
}
