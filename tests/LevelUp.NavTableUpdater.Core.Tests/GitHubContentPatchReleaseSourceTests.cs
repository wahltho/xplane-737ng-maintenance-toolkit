using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Content;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class GitHubContentPatchReleaseSourceTests
{
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
