using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.Manifest;
using LevelUp.NavTableUpdater.Core.Resources;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class GitHubLiveryPackageReleaseSourceTests : IDisposable
{
    private const string Repository = "https://github.com/wahltho/X-Plane-LevelUp-737NG-Lufthansa-Livery";
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mtk-livery-source-{Guid.NewGuid():N}");

    [Fact]
    public async Task PublishedContract_IsResolvedAndArchiveIsDownloadedWithVerification()
    {
        Directory.CreateDirectory(_root);
        var archive = Encoding.UTF8.GetBytes("zip payload");
        const string archiveName = "X-Plane-LevelUp-737NG-Lufthansa-Livery-v1.0.0.zip";
        const string manifestName = "X-Plane-LevelUp-737NG-Lufthansa-Livery-v1.0.0.manifest.json";
        const string tag = "v1.0.0";
        var file = Encoding.UTF8.GetBytes("texture");
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            packageType = "livery",
            packageId = "wahltho.levelup-737ng.livery.lufthansa",
            packageVersion = "1.0.0",
            releaseTag = tag,
            channel = "stable",
            repository = Repository,
            supportedProducts = new[] { "levelup-737ng" },
            supportedVariants = new[] { "737-700", "737-900ER" },
            installScope = "aircraftLivery",
            targetDirectory = "Lufthansa",
            archiveRoot = "Lufthansa",
            restartRequired = true,
            files = new[] { new { path = "objects/texture.png", size = file.LongLength, sha256 = Hash(file) } },
            archive = new { fileName = archiveName, size = archive.LongLength, sha256 = Hash(archive) },
            totals = new { fileCount = 1, uncompressedBytes = file.LongLength }
        });
        var manifestUrl = $"{Repository}/releases/download/{tag}/{manifestName}";
        var archiveUrl = $"{Repository}/releases/download/{tag}/{archiveName}";
        var metadata = JsonSerializer.SerializeToUtf8Bytes(new[]
        {
            new
            {
                tag_name = tag,
                html_url = $"{Repository}/releases/tag/{tag}",
                draft = false,
                prerelease = false,
                assets = new[]
                {
                    new { name = manifestName, browser_download_url = manifestUrl, size = manifest.LongLength, digest = "sha256:" + Hash(manifest) },
                    new { name = archiveName, browser_download_url = archiveUrl, size = archive.LongLength, digest = "sha256:" + Hash(archive) }
                }
            }
        });
        using var client = new HttpClient(new StubHandler(new Dictionary<string, byte[]>
        {
            ["https://api.github.com/repos/wahltho/X-Plane-LevelUp-737NG-Lufthansa-Livery/releases?per_page=100"] = metadata,
            [manifestUrl] = manifest,
            [archiveUrl] = archive
        }));
        var source = new GitHubResourcePackageReleaseSource(client);
        var catalog = Catalog();

        var release = await source.GetLatestAsync(catalog, ResourceReleaseChannel.Stable);
        var result = await source.DownloadAsync(catalog, Assert.IsType<ResourcePackageRelease>(release), _root);

        Assert.Equal("1.0.0", release?.Manifest.PackageVersion);
        Assert.Equal(["737-700", "737-900ER"], release?.Manifest.SupportedVariants);
        Assert.Equal(archive, File.ReadAllBytes(result.ArchivePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static ContentPackageCatalogEntry Catalog() => new()
    {
        PackageId = "wahltho.levelup-737ng.livery.lufthansa",
        DisplayName = "Lufthansa",
        Description = "Optional LevelUp livery.",
        Category = ContentPackageCategory.Livery,
        Activation = ContentPatchActivation.ExplicitOptIn,
        SupportedProducts = ["levelup-737ng"],
        RepositoryUrl = Repository,
        RestartRequired = true,
        InstallScope = "aircraftLivery",
        SupportedChannels = ["stable"],
        Distribution = new ContentPackageDistribution
        {
            Kind = ContentPackageDistributionKind.GitHubLiveryRelease,
            AssetNamePattern = "X-Plane-LevelUp-737NG-Lufthansa-Livery-v*.zip",
            ManifestAssetNamePattern = "X-Plane-LevelUp-737NG-Lufthansa-Livery-v*.manifest.json",
            ManifestSchemaVersion = 1
        }
    };

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class StubHandler(IReadOnlyDictionary<string, byte[]> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is not null && responses.TryGetValue(request.RequestUri.ToString(), out var bytes))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(bytes),
                    RequestMessage = request
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });
        }
    }
}
