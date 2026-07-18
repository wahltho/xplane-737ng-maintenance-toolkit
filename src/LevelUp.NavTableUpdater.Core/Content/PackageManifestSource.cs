using System.Net;
using LevelUp.NavTableUpdater.Core.Manifest;

namespace LevelUp.NavTableUpdater.Core.Content;

public interface IPackageManifestSource
{
    Task<PackageManifest> RefreshAsync(PackageManifest seedManifest, CancellationToken cancellationToken = default);
}

public sealed class GitHubReleasePackageManifestSource : IPackageManifestSource
{
    private readonly HttpClient _httpClient;

    public GitHubReleasePackageManifestSource(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("XPlane737NGMaintenanceToolkit/0.2");
        }
    }

    public async Task<PackageManifest> RefreshAsync(
        PackageManifest seedManifest,
        CancellationToken cancellationToken = default)
    {
        var url = $"{BuildReleaseAssetBaseUrl(seedManifest)}/package-manifest.txt";
        using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new HttpRequestException($"GitHub Release manifest was not found: {url}");
        }

        response.EnsureSuccessStatusCode();
        var manifestText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var refreshed = ManifestParser.ParsePipeManifest(manifestText);
        if (!string.Equals(refreshed.PackageId, seedManifest.PackageId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Release manifest package ID mismatch: expected {seedManifest.PackageId}, got {refreshed.PackageId}.");
        }

        if (!string.Equals(refreshed.RepositoryUrl, seedManifest.RepositoryUrl, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Release manifest repository mismatch: expected {seedManifest.RepositoryUrl}, got {refreshed.RepositoryUrl}.");
        }

        return refreshed;
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
