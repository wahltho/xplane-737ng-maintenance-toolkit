using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.Manifest;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class GitHubContentPatchReleaseSourceTests
{
    [Fact]
    public async Task MetadataCache_CoalescesChecksSurvivesRestartAndExpires()
    {
        using var directory = new DeclarativePatchManifestTests.TemporaryDirectory();
        using var inner = CreateClient(BuildArchive());
        var handler = new CountingReleaseHandler(inner);
        using var client = new HttpClient(handler);
        var clock = new ReleaseClock();
        var source = new GitHubContentPatchReleaseSource(client, directory.Path, clock);
        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => source.GetLatestAsync(BuildCatalogEntry())));
        Assert.Equal(1, handler.Count);
        await new GitHubContentPatchReleaseSource(client, directory.Path, clock).GetLatestAsync(BuildCatalogEntry());
        Assert.Equal(1, handler.Count);
        clock.Now += TimeSpan.FromMinutes(11);
        await source.GetLatestAsync(BuildCatalogEntry());
        Assert.Equal(2, handler.Count);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(429)]
    public async Task RateLimit_SuppressesRetriesAcrossInstancesUntilReset(int status)
    {
        using var directory = new DeclarativePatchManifestTests.TemporaryDirectory();
        using var inner = CreateClient(BuildArchive());
        var clock = new ReleaseClock();
        var handler = new CountingReleaseHandler(inner, count =>
        {
            if (count != 1) return null;
            var response = new HttpResponseMessage((HttpStatusCode)status);
            response.Headers.Add("x-ratelimit-remaining", "0");
            response.Headers.Add("x-ratelimit-reset", clock.Now.AddMinutes(20).ToUnixTimeSeconds().ToString());
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(25));
            return response;
        });
        using var client = new HttpClient(handler);
        var source = new GitHubContentPatchReleaseSource(client, directory.Path, clock);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => source.GetLatestAsync(BuildCatalogEntry()));
        Assert.Contains("Retry after", error.Message);
        Assert.Contains("Do not reinstall", error.Message);
        clock.Now += TimeSpan.FromMinutes(21);
        await Assert.ThrowsAsync<HttpRequestException>(() => new GitHubContentPatchReleaseSource(client, directory.Path, clock).GetLatestAsync(BuildCatalogEntry()));
        Assert.Equal(1, handler.Count);
        clock.Now += TimeSpan.FromMinutes(5);
        var release = await source.GetLatestAsync(BuildCatalogEntry());
        Assert.Equal("v0.1.2", release.Tag);
        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task PermissionFailure_IsNotTreatedAsRateLimit()
    {
        using var directory = new DeclarativePatchManifestTests.TemporaryDirectory();
        using var inner = CreateClient(BuildArchive());
        var handler = new CountingReleaseHandler(inner, _ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        using var client = new HttpClient(handler);
        var source = new GitHubContentPatchReleaseSource(client, directory.Path);
        for (var i = 0; i < 2; i++)
        {
            var error = await Assert.ThrowsAsync<HttpRequestException>(() => source.GetLatestAsync(BuildCatalogEntry()));
            Assert.DoesNotContain("Retry after", error.Message);
        }
        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task CorruptMetadataCache_IsRefetched()
    {
        using var directory = new DeclarativePatchManifestTests.TemporaryDirectory();
        using var inner = CreateClient(BuildArchive());
        var handler = new CountingReleaseHandler(inner);
        using var client = new HttpClient(handler);
        var source = new GitHubContentPatchReleaseSource(client, directory.Path);
        await source.GetLatestAsync(BuildCatalogEntry());
        var file = Assert.Single(Directory.GetFiles(Path.Combine(directory.Path, "release-metadata")));
        File.WriteAllText(file, "broken json");
        await source.GetLatestAsync(BuildCatalogEntry());
        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task RateLimit_AlsoBlocksOtherRepositoriesAndNeverUsesExpiredMetadata()
    {
        using var directory = new DeclarativePatchManifestTests.TemporaryDirectory();
        using var inner = CreateClient(BuildArchive());
        var clock = new ReleaseClock();
        var handler = new CountingReleaseHandler(inner, count => count == 1 ? null
            : new HttpResponseMessage(HttpStatusCode.Forbidden) { ReasonPhrase = "rate limit exceeded" });
        using var client = new HttpClient(handler);
        var source = new GitHubContentPatchReleaseSource(client, directory.Path, clock);
        await source.GetLatestAsync(BuildCatalogEntry());
        clock.Now += TimeSpan.FromMinutes(11);
        await Assert.ThrowsAsync<HttpRequestException>(() => source.GetLatestAsync(BuildCatalogEntry()));
        var other = BuildCatalogEntry();
        other.RepositoryUrl = "https://github.com/example/another-patch";
        await Assert.ThrowsAsync<HttpRequestException>(() => source.GetLatestAsync(other));
        await Assert.ThrowsAsync<HttpRequestException>(() => source.GetLatestAsync(BuildCatalogEntry()));
        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task NetworkFailure_IsNotCachedAsSuccessfulMetadata()
    {
        using var directory = new DeclarativePatchManifestTests.TemporaryDirectory();
        using var inner = CreateClient(BuildArchive());
        var handler = new CountingReleaseHandler(inner, count => count == 1
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : null);
        using var client = new HttpClient(handler);
        var source = new GitHubContentPatchReleaseSource(client, directory.Path);
        await Assert.ThrowsAsync<HttpRequestException>(() => source.GetLatestAsync(BuildCatalogEntry()));
        var release = await source.GetLatestAsync(BuildCatalogEntry());
        Assert.Equal("v0.1.2", release.Tag);
        Assert.Equal(2, handler.Count);
    }

    private sealed class ReleaseClock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 9, 18, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class CountingReleaseHandler(HttpClient inner, Func<int, HttpResponseMessage?>? intercept = null) : HttpMessageHandler
    {
        public int Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Count++;
            var response = intercept?.Invoke(Count);
            return response is not null ? Task.FromResult(response)
                : inner.GetAsync(request.RequestUri, token);
        }
    }

    private const string ApiUrl = "https://api.github.com/repos/example/levelup-fans/releases/latest";
    private const string AssetUrl = "https://github.com/example/levelup-fans/releases/download/v0.1.2/LevelUp-FANS-v0.1.2.zip";

    [Fact]
    public async Task GetLatestAndProvision_VerifiesAndExtractsOnlyManifestPayloads()
    {
        var archive = BuildArchive();
        using var directory = new DeclarativePatchManifestTests.TemporaryDirectory();
        using var client = CreateClient(archive);
        var source = new GitHubContentPatchReleaseSource(client, directory.Path);

        var release = await source.GetLatestAsync(BuildCatalogEntry());
        var result = await source.ProvisionAsync(BuildCatalogEntry(), release);

        Assert.Equal("v0.1.2", release.Tag);
        Assert.Equal("0.1.2", result.Package.Manifest.PackageVersion);
        Assert.True(File.Exists(Path.Combine(result.PackageDirectory, "package-manifest.json")));
        Assert.True(File.Exists(Path.Combine(result.PackageDirectory, "patches", "change.json")));
        Assert.False(File.Exists(Path.Combine(result.PackageDirectory, "README.md")));
    }

    [Fact]
    public async Task Schema4CompatibilityArchive_ProvisionsAndReloadsManagedScopeFromCache()
    {
        var payload = "new object"u8.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 4, packageType = "compatibilityPackage",
            packageId = "example.levelup.fans", packageVersion = "0.1.2",
            repositoryUrl = "https://github.com/example/levelup-fans",
            aircraftFamily = "LevelUp 737NG Series", supportedProducts = new[] { "levelup-737ng" },
            restartRequired = true,
            modules = new[] { new
            {
                moduleId = "gse", displayName = "GSE", description = "Managed objects",
                policy = "required", defaultEnabled = true, installationOrder = 10,
                payloads = new[] { new { path = "new.obj", size = payload.Length, sha256 = hash } },
                targets = new[] { new { operation = "copy-file-v1", payload = "new.obj",
                    relativePath = "objects/GSE/new.obj", resultSha256 = hash } },
                retiredFiles = new[] { new { relativePath = "objects/GSE/old.obj",
                    sourceSha256 = new[] { new string('a', 64) } } },
                managedScopes = new[] { new { relativePath = "objects/GSE", mode = "flatExclusive" } }
            } }
        });
        using var archiveBytes = new MemoryStream();
        using (var zip = new ZipArchive(archiveBytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "bundle/package-manifest.json", manifest);
            WriteEntry(zip, "bundle/modules/gse/new.obj", payload);
        }
        using var directory = new DeclarativePatchManifestTests.TemporaryDirectory();
        using var client = CreateClient(archiveBytes.ToArray());
        var source = new GitHubContentPatchReleaseSource(client, directory.Path);
        var entry = BuildCatalogEntry();
        entry.Category = ContentPackageCategory.CompatibilityPackage;
        entry.Activation = ContentPatchActivation.Managed;
        entry.Distribution.ManifestSchemaVersion = 4;
        var release = await source.GetLatestAsync(entry);

        var first = await source.ProvisionCompatibilityAsync(entry, release);
        var cached = await source.ProvisionCompatibilityAsync(entry, release);
        Assert.Equal(4, first.Package.Manifest.SchemaVersion);
        Assert.Equal("objects/GSE", Assert.Single(Assert.Single(cached.Package.Manifest.Modules).ManagedScopes).RelativePath);
        Assert.Equal("objects/GSE/old.obj", Assert.Single(Assert.Single(cached.Package.Manifest.Modules).RetiredFiles).RelativePath);
    }

    [Fact]
    public async Task CatalogGroup_MaterializesSchema4FunctionAndSchema5ConditionalFix()
    {
        using var directory = new DeclarativePatchManifestTests.TemporaryDirectory();
        var functionalPayload = "optional\r\n"u8.ToArray();
        var fixPayload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            format = "exact-text-replacements-v1",
            replacements = new[] { new { oldLines = new[] { "optional" }, newLines = new[] { "hardened" } } }
        });
        static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        static byte[] Archive(byte[] manifest, string moduleId, string payloadName, byte[] payload)
        {
            using var output = new MemoryStream();
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                WriteEntry(zip, "package-manifest.json", manifest);
                WriteEntry(zip, $"modules/{moduleId}/{payloadName}", payload);
            }
            return output.ToArray();
        }

        var functionalArchive = Archive(JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 4, packageType = "compatibilityPackage",
            packageId = "example.functional", packageVersion = "1.0.0",
            repositoryUrl = "https://github.com/example/functional",
            aircraftFamily = "LevelUp 737NG Series", supportedProducts = new[] { "levelup-737ng" },
            modules = new[] { new
            {
                moduleId = "functional", displayName = "Functional", description = "Optional feature",
                policy = "optional", defaultEnabled = false, installationOrder = 10,
                payloads = new[] { new { path = "functional.lua", size = functionalPayload.Length, sha256 = Hash(functionalPayload) } },
                targets = new[] { new { operation = "copy-file-v1", payload = "functional.lua",
                    relativePath = "plugins/xlua/scripts/shared.lua", resultSha256 = Hash(functionalPayload) } }
            } }
        }), "functional", "functional.lua", functionalPayload);
        var fixArchive = Archive(JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 5, packageType = "compatibilityPackage",
            packageId = "example.fix", packageVersion = "1.0.0",
            repositoryUrl = "https://github.com/example/fix",
            aircraftFamily = "LevelUp 737NG Series", supportedProducts = new[] { "levelup-737ng" },
            modules = new[] { new
            {
                moduleId = "fix", displayName = "Fix", description = "Conditional fix",
                policy = "required", defaultEnabled = true, installationOrder = 90,
                payloads = new[] { new { path = "fix.json", size = fixPayload.Length, sha256 = Hash(fixPayload) } },
                targets = new[] { new { operation = "exact-text-replacements-v1", payload = "fix.json",
                    relativePath = "plugins/xlua/scripts/shared.lua", whenModulesSelected = new[] { "functional" } } }
            } }
        }), "fix", "fix.json", fixPayload);

        ContentPackageCatalogEntry SourceEntry(string id, int schema) => new()
        {
            PackageId = $"example.{id}", DisplayName = id, Description = id,
            RepositoryUrl = $"https://github.com/example/{id}", SupportedProducts = ["levelup-737ng"],
            Distribution = new() { Kind = ContentPackageDistributionKind.GitHubReleaseArchive,
                ManifestSchemaVersion = schema }
        };
        ContentPatchRelease Release(string id, byte[] archive) => new("v1.0.0", "",
            $"{id}.zip", $"https://github.com/example/{id}/releases/download/v1.0.0/{id}.zip",
            archive.Length, Hash(archive));
        var group = new ContentPackageCatalogEntry
        {
            PackageId = "example.group", DisplayName = "Grouped fixes",
            RepositoryUrl = "https://github.com/example/group", SupportedProducts = ["levelup-737ng"],
            Distribution = new() { Kind = ContentPackageDistributionKind.CatalogGroup }
        };
        var sources = new List<(CatalogGroupMember, ContentPackageCatalogEntry, ContentPatchRelease)>
        {
            (new() { ModuleId = "functional", PackageId = "example.functional", SourceFormat = "compatibility",
                ManifestPath = "package-manifest.json", Policy = CompatibilityModulePolicy.Optional,
                InstallationOrder = 10 }, SourceEntry("functional", 4), Release("functional", functionalArchive)),
            (new() { ModuleId = "fix", PackageId = "example.fix", SourceFormat = "compatibility",
                ManifestPath = "package-manifest.json", Policy = CompatibilityModulePolicy.Required,
                InstallationOrder = 90 }, SourceEntry("fix", 5), Release("fix", fixArchive))
        };
        using var client = new HttpClient(new StubHandler(new Dictionary<string, byte[]>
        {
            [sources[0].Item3.AssetUrl] = functionalArchive,
            [sources[1].Item3.AssetUrl] = fixArchive
        }));
        var releaseSource = new GitHubContentPatchReleaseSource(client, directory.Path);

        var provisioned = await releaseSource.ProvisionGroupAsync(new(group, sources, "catalog-test"));
        var reloaded = CompatibilityPackageLoader.LoadDirectory(provisioned.PackageDirectory);
        Assert.Equal(5, reloaded.Manifest.SchemaVersion);
        Assert.Equal(["functional", "fix"], reloaded.Manifest.Modules.Select(module => module.ModuleId));
        Assert.Equal(["functional"], Assert.Single(reloaded.Manifest.Modules[1].Targets).WhenModulesSelected);
        Assert.Equal(["fix"], CompatibilityPackagePlanBuilder.DefaultSelection(reloaded.Manifest));
        Assert.Equal(["functional", "fix"], CompatibilityPackagePlanBuilder.ResolveSelection(
            reloaded.Manifest, ["functional", "fix"]));
    }

    [Fact]
    public async Task GetLatest_WhenAssetDigestIsInvalid_RejectsRelease()
    {
        var archive = BuildArchive();
        using var directory = new DeclarativePatchManifestTests.TemporaryDirectory();
        using var client = CreateClient(archive, digest: "sha256:" + new string('0', 64));
        var source = new GitHubContentPatchReleaseSource(client, directory.Path);

        var release = await source.GetLatestAsync(BuildCatalogEntry());
        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => source.ProvisionAsync(BuildCatalogEntry(), release));

        Assert.Contains("size/SHA-256", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Provision_WhenArchiveContainsTraversal_RejectsArchive()
    {
        var archive = BuildArchive(addTraversalEntry: true);
        using var directory = new DeclarativePatchManifestTests.TemporaryDirectory();
        using var client = CreateClient(archive);
        var source = new GitHubContentPatchReleaseSource(client, directory.Path);
        var release = await source.GetLatestAsync(BuildCatalogEntry());

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => source.ProvisionAsync(BuildCatalogEntry(), release));

        Assert.Contains("Unsafe content patch archive path", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Provision_WhenManifestProductDiffersFromCatalog_RejectsPackage()
    {
        var archive = BuildArchive(supportedProduct: "zibo-737ng");
        using var directory = new DeclarativePatchManifestTests.TemporaryDirectory();
        using var client = CreateClient(archive);
        var source = new GitHubContentPatchReleaseSource(client, directory.Path);
        var release = await source.GetLatestAsync(BuildCatalogEntry());

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => source.ProvisionAsync(BuildCatalogEntry(), release));

        Assert.Contains("does not match the trusted catalog entry", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Provision_WhenCallerSuppliesUnsafeReleasePath_RejectsBeforeDownload()
    {
        using var directory = new DeclarativePatchManifestTests.TemporaryDirectory();
        using var client = new HttpClient(new StubHandler(new Dictionary<string, byte[]>()));
        var source = new GitHubContentPatchReleaseSource(client, directory.Path);
        var release = new ContentPatchRelease(
            "v0.1.2",
            "https://github.com/example/levelup-fans/releases/tag/v0.1.2",
            "../LevelUp-FANS-v0.1.2.zip",
            AssetUrl,
            10,
            new string('1', 64));

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => source.ProvisionAsync(BuildCatalogEntry(), release));

        Assert.Contains("does not match the trusted catalog entry", error.Message, StringComparison.Ordinal);
    }

    private static ContentPackageCatalogEntry BuildCatalogEntry() =>
        new()
        {
            PackageId = "example.levelup.fans",
            DisplayName = "LevelUp FANS",
            Description = "Optional FANS patch.",
            Category = ContentPackageCategory.OptionalPatch,
            Activation = ContentPatchActivation.ExplicitOptIn,
            SupportedProducts = ["levelup-737ng"],
            RepositoryUrl = "https://github.com/example/levelup-fans",
            RestartRequired = true,
            Distribution = new ContentPackageDistribution
            {
                Kind = ContentPackageDistributionKind.GitHubReleaseArchive,
                AssetNamePattern = "LevelUp-FANS-v*.zip",
                ManifestSchemaVersion = 2
            }
        };

    private static byte[] BuildArchive(bool addTraversalEntry = false, string supportedProduct = "levelup-737ng")
    {
        var payload = Encoding.UTF8.GetBytes("{}");
        var manifest = DeclarativePatchManifestTests.BuildManifest(
            "patches/change.json",
            payload,
            "objects/test.txt",
            supportedProducts: [supportedProduct]);
        manifest = manifest
            .Replace("wahltho.levelup-737ng.fans-cdu-3d", "example.levelup.fans", StringComparison.Ordinal)
            .Replace(
                "https://github.com/wahltho/X-Plane-LevelUp-737NG-FANS-CDU",
                "https://github.com/example/levelup-fans",
                StringComparison.Ordinal)
            .Replace("\"packageVersion\":\"0.1.0\"", "\"packageVersion\":\"0.1.2\"", StringComparison.Ordinal);

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "bundle/package-manifest.json", Encoding.UTF8.GetBytes(manifest));
            WriteEntry(archive, "bundle/patches/change.json", payload);
            WriteEntry(archive, "bundle/README.md", Encoding.UTF8.GetBytes("Not required by the manifest."));
            if (addTraversalEntry)
            {
                WriteEntry(archive, "../escape.txt", Encoding.UTF8.GetBytes("blocked"));
            }
        }

        return output.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string path, byte[] bytes)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    private static HttpClient CreateClient(byte[] archive, string? digest = null)
    {
        digest ??= "sha256:" + Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant();
        var metadata = JsonSerializer.SerializeToUtf8Bytes(new
        {
            tag_name = "v0.1.2",
            html_url = "https://github.com/example/levelup-fans/releases/tag/v0.1.2",
            draft = false,
            prerelease = false,
            assets = new[]
            {
                new
                {
                    name = "LevelUp-FANS-v0.1.2.zip",
                    browser_download_url = AssetUrl,
                    size = archive.LongLength,
                    digest
                }
            }
        });
        return new HttpClient(new StubHandler(new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [ApiUrl] = metadata,
            [AssetUrl] = archive
        }));
    }

    private sealed class StubHandler(IReadOnlyDictionary<string, byte[]> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.AbsoluteUri ?? "";
            return Task.FromResult(responses.TryGetValue(url, out var bytes)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
