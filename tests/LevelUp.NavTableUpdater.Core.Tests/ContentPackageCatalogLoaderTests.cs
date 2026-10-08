using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Content;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class ContentPackageCatalogLoaderTests
{
    private const string RepositoryUrl = "https://github.com/example/toolkit";
    private const string ApiUrl = "https://api.github.com/repos/example/toolkit/releases?per_page=100";

    [Fact]
    public async Task LoadAsync_UsesHighestStableCatalogReleaseAndWritesValidatedCache()
    {
        using var directory = new TemporaryDirectory();
        var remoteJson = BuildCatalog("1.4.0", "0.12.0", "remote.package");
        using var client = CreateClient(BuildReleaseResponse(
            Release("v99.0.0", remoteJson),
            Release("catalog-v1.5.0", BuildCatalog("1.5.0", "0.12.0", "beta.package"), prerelease: true),
            Release("catalog-v1.4.0", remoteJson),
            Release("catalog-v1.3.0", BuildCatalog("1.3.0", "0.11.0", "old.package"))));
        var loader = new ContentPackageCatalogLoader(
            client,
            directory.Path,
            new Version(0, 12, 0),
            RepositoryUrl,
            ApiUrl);

        var result = await loader.LoadAsync();

        Assert.Equal(ContentPackageCatalogOrigin.RemoteRelease, result.Origin);
        Assert.Equal("1.4.0", result.Catalog.CatalogVersion);
        Assert.Equal("catalog-v1.4.0", result.ReleaseTag);
        Assert.Equal("remote.package", Assert.Single(result.Catalog.Packages).PackageId);
        Assert.Equal(remoteJson, File.ReadAllText(loader.CachePath));
    }

    [Fact]
    public async Task LoadAsync_WhenRemoteFails_UsesLastKnownGoodCache()
    {
        using var directory = new TemporaryDirectory();
        var cachedJson = BuildCatalog("1.3.1", "0.11.0", "cached.package");
        File.WriteAllText(Path.Combine(directory.Path, ContentPackageCatalogLoader.CatalogAssetName), cachedJson);
        using var client = new HttpClient(new StubHandler(new Dictionary<string, HttpResponseMessage>()));
        var loader = new ContentPackageCatalogLoader(
            client,
            directory.Path,
            new Version(0, 12, 0),
            RepositoryUrl,
            ApiUrl);

        var result = await loader.LoadAsync();

        Assert.Equal(ContentPackageCatalogOrigin.LastKnownGoodCache, result.Origin);
        Assert.Equal("cached.package", Assert.Single(result.Catalog.Packages).PackageId);
        Assert.Contains("Remote catalog unavailable", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_WhenRemoteRequiresNewerToolkit_DoesNotReplaceValidCache()
    {
        using var directory = new TemporaryDirectory();
        var cachedJson = BuildCatalog("1.3.1", "0.11.0", "cached.package");
        var cachePath = Path.Combine(directory.Path, ContentPackageCatalogLoader.CatalogAssetName);
        File.WriteAllText(cachePath, cachedJson);
        var remoteJson = BuildCatalog("2.0.0", "1.0.0", "future.package");
        using var client = CreateClient(BuildReleaseResponse(Release("catalog-v2.0.0", remoteJson)));
        var loader = new ContentPackageCatalogLoader(
            client,
            directory.Path,
            new Version(0, 12, 0),
            RepositoryUrl,
            ApiUrl);

        var result = await loader.LoadAsync();

        Assert.Equal(ContentPackageCatalogOrigin.LastKnownGoodCache, result.Origin);
        Assert.Equal("cached.package", Assert.Single(result.Catalog.Packages).PackageId);
        Assert.Equal(cachedJson, File.ReadAllText(cachePath));
    }

    [Fact]
    public async Task LoadAsync_WhenRemoteAndCacheAreInvalid_ReturnsUnavailableCatalog()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(directory.Path, ContentPackageCatalogLoader.CatalogAssetName),
            "{not-json");
        using var client = new HttpClient(new StubHandler(new Dictionary<string, HttpResponseMessage>()));
        var loader = new ContentPackageCatalogLoader(
            client,
            directory.Path,
            new Version(0, 12, 0),
            RepositoryUrl,
            ApiUrl);

        var result = await loader.LoadAsync();

        Assert.Equal(ContentPackageCatalogOrigin.Unavailable, result.Origin);
        Assert.False(result.Catalog.IsAvailable);
        Assert.Empty(result.Catalog.Packages);
        Assert.Contains("cached catalog rejected", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_WhenRemoteOmitsMinimumToolkitVersion_ReturnsUnavailableCatalog()
    {
        using var directory = new TemporaryDirectory();
        var remoteJson = BuildCatalog("1.4.0", "", "remote.package");
        using var client = CreateClient(BuildReleaseResponse(Release("catalog-v1.4.0", remoteJson)));
        var loader = new ContentPackageCatalogLoader(
            client,
            directory.Path,
            new Version(0, 12, 0),
            RepositoryUrl,
            ApiUrl);

        var result = await loader.LoadAsync();

        Assert.Equal(ContentPackageCatalogOrigin.Unavailable, result.Origin);
        Assert.False(result.Catalog.IsAvailable);
        Assert.Empty(result.Catalog.Packages);
        Assert.Contains("has no minimumToolkitVersion", result.Detail, StringComparison.Ordinal);
        Assert.False(File.Exists(loader.CachePath));
    }

    [Fact]
    public void Parse_WhenMinimumToolkitVersionIsNotMet_RejectsCatalog()
    {
        var error = Assert.Throws<InvalidDataException>(() =>
            ContentPackageCatalog.Parse(
                BuildCatalog("2.0.0", "2.0.0", "future.package"),
                new Version(1, 9, 9)));

        Assert.Contains("requires toolkit 2.0.0", error.Message, StringComparison.Ordinal);
    }

    private static HttpClient CreateClient(IReadOnlyDictionary<string, HttpResponseMessage> releaseResponses)
    {
        var responses = new Dictionary<string, HttpResponseMessage>(releaseResponses, StringComparer.Ordinal);
        return new HttpClient(new StubHandler(responses));
    }

    private static IReadOnlyDictionary<string, HttpResponseMessage> BuildReleaseResponse(params ReleaseFixture[] releases)
    {
        var metadata = releases.Select(release => release.Metadata).ToArray();
        var responses = new Dictionary<string, HttpResponseMessage>(StringComparer.Ordinal)
        {
            [ApiUrl] = JsonResponse(metadata)
        };
        foreach (var release in releases.Where(release => release.Tag.StartsWith("catalog-v", StringComparison.Ordinal)))
        {
            responses[release.AssetUrl] = BytesResponse(release.Bytes);
        }

        return responses;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnershipCatalog_LoadAndOfflineCachePreserveAllRules(bool offline)
    {
        using var directory = new TemporaryDirectory();
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Content", "content-package-catalog.json"));
        var version = ContentPackageCatalog.Parse(json).CatalogVersion;
        if (offline) File.WriteAllText(Path.Combine(directory.Path, ContentPackageCatalogLoader.CatalogAssetName), json);
        using var client = offline ? new HttpClient(new StubHandler(new Dictionary<string, HttpResponseMessage>()))
            : CreateClient(BuildReleaseResponse(Release("catalog-v" + version, json)));
        var loader = new ContentPackageCatalogLoader(client, directory.Path, new Version(0, 28, 2), RepositoryUrl, ApiUrl);
        var result = await loader.LoadAsync();
        Assert.Equal(offline ? ContentPackageCatalogOrigin.LastKnownGoodCache : ContentPackageCatalogOrigin.RemoteRelease, result.Origin);
        Assert.Equal(2, result.Catalog.SchemaVersion);
        Assert.Equal(JsonSerializer.Serialize(ContentPackageCatalog.Parse(json).OwnershipPolicies),
            JsonSerializer.Serialize(result.Catalog.OwnershipPolicies));
        Assert.Equal(json, File.ReadAllText(loader.CachePath));
    }

    [Fact]
    public async Task OwnershipCatalog_RejectsRemoteAndCachedSchema1InsteadOfDowngrading()
    {
        using var directory = new TemporaryDirectory();
        var oldDocument = System.Text.Json.Nodes.JsonNode.Parse(BuildCatalog("99.0.0", "0.21.2", "old.package"))!.AsObject();
        oldDocument["schemaVersion"] = 1;
        oldDocument.Remove("ownershipPolicies");
        var old = oldDocument.ToJsonString();
        File.WriteAllText(Path.Combine(directory.Path, ContentPackageCatalogLoader.CatalogAssetName), old);
        using var client = CreateClient(BuildReleaseResponse(Release("catalog-v99.0.0", old)));
        var loader = new ContentPackageCatalogLoader(client, directory.Path, new Version(0, 28, 0), RepositoryUrl, ApiUrl);
        var result = await loader.LoadAsync();
        Assert.Equal(ContentPackageCatalogOrigin.Unavailable, result.Origin);
        Assert.False(result.Catalog.IsAvailable);
        Assert.Empty(result.Catalog.OwnershipPolicies);
    }

    [Fact]
    public async Task LoadAsync_OfflineWithoutCache_ReturnsUnavailableInsteadOfACompiledCatalog()
    {
        using var directory = new TemporaryDirectory();
        using var client = new HttpClient(new StubHandler(new Dictionary<string, HttpResponseMessage>()));
        var loader = new ContentPackageCatalogLoader(client, directory.Path, new Version(0, 28, 0), RepositoryUrl, ApiUrl);
        var result = await loader.LoadAsync();
        Assert.Equal(ContentPackageCatalogOrigin.Unavailable, result.Origin);
        Assert.False(result.Catalog.IsAvailable);
        Assert.Empty(result.Catalog.Packages);
        Assert.Empty(result.Catalog.OwnershipPolicies);
        Assert.False(File.Exists(loader.CachePath));
        Assert.DoesNotContain(typeof(ContentPackageCatalog).Assembly.GetManifestResourceNames(),
            name => name.EndsWith("content-package-catalog.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadAsync_AnOlderRemoteResponseCannotDowngradeTheSavedCatalog()
    {
        using var directory = new TemporaryDirectory();
        var saved = BuildCatalog("1.9.0", "0.28.0", "saved.package");
        var path = Path.Combine(directory.Path, ContentPackageCatalogLoader.CatalogAssetName);
        File.WriteAllText(path, saved);
        var remote = BuildCatalog("1.8.0", "0.28.0", "old.package");
        using var client = CreateClient(BuildReleaseResponse(Release("catalog-v1.8.0", remote)));
        var result = await new ContentPackageCatalogLoader(client, directory.Path, new Version(0, 28, 0), RepositoryUrl, ApiUrl).LoadAsync();
        Assert.Equal(ContentPackageCatalogOrigin.LastKnownGoodCache, result.Origin);
        Assert.Equal("1.9.0", result.Catalog.CatalogVersion);
        Assert.Equal(saved, File.ReadAllText(path));
    }

    [Fact]
    public async Task LoadAsync_WhenSavingTheCacheFails_StillUsesTheValidatedOnlineCatalog()
    {
        using var directory = new TemporaryDirectory();
        var blockedDirectory = Path.Combine(directory.Path, "not-a-directory");
        File.WriteAllText(blockedDirectory, "file");
        var remote = BuildCatalog("1.9.0", "0.28.0", "online.package");
        using var client = CreateClient(BuildReleaseResponse(Release("catalog-v1.9.0", remote)));
        var loader = new ContentPackageCatalogLoader(client, blockedDirectory, new Version(0, 28, 0), RepositoryUrl, ApiUrl);
        var result = await loader.LoadAsync();
        Assert.Equal(ContentPackageCatalogOrigin.RemoteRelease, result.Origin);
        Assert.Equal("online.package", Assert.Single(result.Catalog.Packages).PackageId);
        Assert.Contains("Could not save", result.Detail, StringComparison.Ordinal);
        Assert.False(File.Exists(loader.CachePath));
    }

    [Fact]
    public async Task LoadAsync_UpdatedOnlineRulesReplaceTheCacheWithoutChangingTheApplication()
    {
        using var directory = new TemporaryDirectory();
        var saved = BuildCatalog("1.8.0", "0.28.0", "test.package");
        File.WriteAllText(Path.Combine(directory.Path, ContentPackageCatalogLoader.CatalogAssetName), saved);
        var remote = BuildCatalog("1.9.0", "0.28.0", "test.package")
            .Replace(".standalone/state.json", ".standalone.lock", StringComparison.Ordinal);
        using (var client = CreateClient(BuildReleaseResponse(Release("catalog-v1.9.0", remote))))
        {
            var online = await new ContentPackageCatalogLoader(client, directory.Path, new Version(0, 28, 0), RepositoryUrl, ApiUrl).LoadAsync();
            Assert.Equal(ContentPackageCatalogOrigin.RemoteRelease, online.Origin);
            Assert.Equal([".standalone.lock"], Assert.Single(online.Catalog.OwnershipPolicies).StandaloneEvidencePaths);
        }
        using var offlineClient = new HttpClient(new StubHandler(new Dictionary<string, HttpResponseMessage>()));
        var offline = await new ContentPackageCatalogLoader(offlineClient, directory.Path, new Version(0, 28, 0), RepositoryUrl, ApiUrl).LoadAsync();
        Assert.Equal(ContentPackageCatalogOrigin.LastKnownGoodCache, offline.Origin);
        Assert.Equal([".standalone.lock"], Assert.Single(offline.Catalog.OwnershipPolicies).StandaloneEvidencePaths);
    }

    private static ReleaseFixture Release(string tag, string json, bool prerelease = false)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var assetUrl = $"{RepositoryUrl}/releases/download/{tag}/{ContentPackageCatalogLoader.CatalogAssetName}";
        var metadata = new
        {
            tag_name = tag,
            draft = false,
            prerelease,
            assets = new[]
            {
                new
                {
                    name = ContentPackageCatalogLoader.CatalogAssetName,
                    browser_download_url = assetUrl,
                    size = bytes.LongLength,
                    digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes))
                }
            }
        };
        return new ReleaseFixture(tag, assetUrl, bytes, metadata);
    }

    private static string BuildCatalog(string version, string minimumToolkitVersion, string packageId)
    {
        var minimumProperty = string.IsNullOrWhiteSpace(minimumToolkitVersion)
            ? ""
            : $"\"minimumToolkitVersion\": \"{minimumToolkitVersion}\",";
        return $$"""
            {
              "schemaVersion": 2,
              "catalogVersion": "{{version}}",
              {{minimumProperty}}
              "ownershipPolicies": [
                {
                  "packageId": "{{packageId}}",
                  "displayName": "Test package",
                  "repositoryUrl": "https://github.com/example/package",
                  "supportedProducts": ["zibo-737ng"],
                  "targetPaths": ["script.lua"],
                  "standaloneEvidencePaths": [".standalone/state.json"],
                  "recoveryInstruction": "Restore the test package with its installer."
                }
              ],
              "packages": [
                {
                  "packageId": "{{packageId}}",
                  "displayName": "Test package",
                  "description": "Test catalog package.",
                  "category": "managedContent",
                  "activation": "managed",
                  "supportedProducts": ["zibo-737ng"],
                  "repositoryUrl": "https://github.com/example/package",
                  "restartRequired": true,
                  "distribution": { "kind": "existingVnav" }
                }
              ]
            }
            """;
    }

    private static HttpResponseMessage JsonResponse(object value) =>
        new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(value))
        };

    private static HttpResponseMessage BytesResponse(byte[] value) =>
        new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(value)
        };

    private sealed class StubHandler(IReadOnlyDictionary<string, HttpResponseMessage> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (!responses.TryGetValue(request.RequestUri!.ToString(), out var response))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(response);
        }
    }

    private sealed record ReleaseFixture(string Tag, string AssetUrl, byte[] Bytes, object Metadata);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"catalog-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
