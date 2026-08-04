using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.Resources;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class GitHubResourcePackageReleaseSourceTests
{
    [Fact]
    public async Task StableRelease_SkipsUnrelatedReleaseAndDownloadsVerifiedArchive()
    {
        using var fixture = new ReleaseFixture();
        using var client = fixture.CreateClient();
        var source = new GitHubResourcePackageReleaseSource(client);

        var release = await source.GetLatestAsync(fixture.Catalog, ResourceReleaseChannel.Stable);
        var provisioned = await source.DownloadAsync(
            fixture.Catalog,
            Assert.IsType<ResourcePackageRelease>(release),
            fixture.Destination);

        Assert.Equal("2.S1", release?.Manifest.PackageVersion);
        Assert.True(provisioned.Downloaded);
        Assert.True(provisioned.Temporary);
        Assert.Equal(Path.GetFullPath(fixture.Destination), Path.GetDirectoryName(provisioned.ArchivePath));
        Assert.EndsWith(".download", provisioned.ArchivePath, StringComparison.Ordinal);
        Assert.Equal(fixture.Archive, File.ReadAllBytes(provisioned.ArchivePath));
    }

    [Fact]
    public async Task BetaRelease_SelectsPrereleaseWithMatchingManifestChannel()
    {
        using var fixture = new ReleaseFixture(channel: ResourceReleaseChannel.Beta);
        using var client = fixture.CreateClient();
        var source = new GitHubResourcePackageReleaseSource(client);

        var release = await source.GetLatestAsync(fixture.Catalog, ResourceReleaseChannel.Beta);

        Assert.NotNull(release);
        Assert.Equal(ResourceReleaseChannel.Beta, release.Channel);
        Assert.Equal("beta", release.Manifest.Channel);
    }

    [Fact]
    public async Task Release_WithArchiveDigestDifferentFromManifest_RejectsRelease()
    {
        using var fixture = new ReleaseFixture(manifestArchiveHash: new string('a', 64));
        using var client = fixture.CreateClient();
        var source = new GitHubResourcePackageReleaseSource(client);

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => source.GetLatestAsync(fixture.Catalog, ResourceReleaseChannel.Stable));

        Assert.Contains("does not match", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Download_WithCanceledToken_RemovesTemporaryDestinationFile()
    {
        using var fixture = new ReleaseFixture();
        using var client = fixture.CreateClient();
        var source = new GitHubResourcePackageReleaseSource(client);
        var release = await source.GetLatestAsync(fixture.Catalog, ResourceReleaseChannel.Stable);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.DownloadAsync(
                fixture.Catalog,
                Assert.IsType<ResourcePackageRelease>(release),
                fixture.Destination,
                cancellation.Token));

        Assert.Empty(Directory.EnumerateFiles(fixture.Destination, "*.download"));
    }

    [Fact]
    public async Task Download_WithCorruptArchive_RemovesTemporaryDestinationFile()
    {
        var corruptArchive = Encoding.UTF8.GetBytes("paintkit archivf");
        using var fixture = new ReleaseFixture(downloadedArchive: corruptArchive);
        using var client = fixture.CreateClient();
        var source = new GitHubResourcePackageReleaseSource(client);
        var release = await source.GetLatestAsync(fixture.Catalog, ResourceReleaseChannel.Stable);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => source.DownloadAsync(
                fixture.Catalog,
                Assert.IsType<ResourcePackageRelease>(release),
                fixture.Destination));

        Assert.Empty(Directory.EnumerateFiles(fixture.Destination));
    }

    private sealed class ReleaseFixture : IDisposable
    {
        private const string Repository = "https://github.com/petrolpram/737NG-Updates";
        private readonly byte[] _manifest;
        private readonly byte[] _metadata;
        private readonly string _tag;

        public ReleaseFixture(
            string? manifestArchiveHash = null,
            byte[]? downloadedArchive = null,
            ResourceReleaseChannel channel = ResourceReleaseChannel.Stable)
        {
            Root = Path.Combine(Path.GetTempPath(), $"xplane-resource-release-tests-{Guid.NewGuid():N}");
            Destination = Path.Combine(Root, "destination");
            Directory.CreateDirectory(Root);
            Archive = Encoding.UTF8.GetBytes("paintkit archive");
            DownloadedArchive = downloadedArchive ?? Archive;
            _tag = channel is ResourceReleaseChannel.Beta
                ? "resource-paintkit-v2.S1-beta.1"
                : "resource-paintkit-v2.S1";
            var channelName = channel is ResourceReleaseChannel.Beta ? "beta" : "stable";
            var archiveName = "LevelUp-Paintkit-2.S1.7z";
            var manifestName = "LevelUp-Paintkit-2.S1-manifest.json";
            _manifest = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 1,
                packageType = "resource",
                packageId = "levelup.paintkit",
                packageVersion = "2.S1",
                releaseTag = _tag,
                channel = channelName,
                repository = Repository,
                supportedProducts = new[] { "levelup-737ng" },
                deliveryMode = "extract",
                archiveRoot = "Paintkit",
                targetDirectory = "Paintkit",
                extractedSize = 8,
                files = new[]
                {
                    new
                    {
                        path = "readme.txt",
                        size = 8,
                        sha256 = Hash(Encoding.UTF8.GetBytes("paintkit"))
                    }
                },
                archive = new
                {
                    fileName = archiveName,
                    size = Archive.LongLength,
                    sha256 = manifestArchiveHash ?? Hash(Archive)
                }
            });
            var resourceRelease = new
            {
                tag_name = _tag,
                html_url = $"{Repository}/releases/tag/{_tag}",
                draft = false,
                prerelease = channel is ResourceReleaseChannel.Beta,
                assets = new[]
                {
                    new
                    {
                        name = manifestName,
                        browser_download_url = AssetUrl(manifestName),
                        size = _manifest.LongLength,
                        digest = "sha256:" + Hash(_manifest)
                    },
                    new
                    {
                        name = archiveName,
                        browser_download_url = AssetUrl(archiveName),
                        size = Archive.LongLength,
                        digest = "sha256:" + Hash(Archive)
                    }
                }
            };
            _metadata = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                new
                {
                    tag_name = "v2.S1.50",
                    html_url = $"{Repository}/releases/tag/v2.S1.50",
                    draft = false,
                    prerelease = false,
                    assets = Array.Empty<object>()
                },
                resourceRelease
            });
        }

        public string Root { get; }

        public string Destination { get; }

        public byte[] Archive { get; }

        public byte[] DownloadedArchive { get; }

        public ContentPackageCatalogEntry Catalog { get; } = new()
        {
            PackageId = "levelup.paintkit",
            DisplayName = "LevelUp Paintkit",
            Description = "Test resource",
            Category = ContentPackageCategory.Resource,
            Activation = ContentPatchActivation.ExplicitOptIn,
            SupportedProducts = ["levelup-737ng"],
            RepositoryUrl = Repository,
            RestartRequired = false,
            InstallScope = "userSelectedDirectory",
            SupportedChannels = ["stable", "beta"],
            Distribution = new ContentPackageDistribution
            {
                Kind = ContentPackageDistributionKind.GitHubResourceRelease,
                AssetNamePattern = "LevelUp-Paintkit-*.7z",
                ManifestAssetNamePattern = "LevelUp-Paintkit-*-manifest.json",
                ManifestSchemaVersion = 1
            }
        };

        public HttpClient CreateClient()
        {
            const string metadataUrl = "https://api.github.com/repos/petrolpram/737NG-Updates/releases?per_page=100";
            const string manifestName = "LevelUp-Paintkit-2.S1-manifest.json";
            const string archiveName = "LevelUp-Paintkit-2.S1.7z";
            return new HttpClient(new StubHandler(new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [metadataUrl] = _metadata,
                [AssetUrl(manifestName)] = _manifest,
                [AssetUrl(archiveName)] = DownloadedArchive
            }));
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private string AssetUrl(string name) =>
            $"{Repository}/releases/download/{_tag}/{name}";

        private static string Hash(byte[] bytes) =>
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private sealed class StubHandler(IReadOnlyDictionary<string, byte[]> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var url = request.RequestUri?.AbsoluteUri ?? "";
            return Task.FromResult(responses.TryGetValue(url, out var bytes)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
