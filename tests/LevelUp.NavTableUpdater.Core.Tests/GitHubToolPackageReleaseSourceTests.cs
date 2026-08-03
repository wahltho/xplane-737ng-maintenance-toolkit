using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.Tools;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class GitHubToolPackageReleaseSourceTests
{
    [Fact]
    public async Task StableRelease_ResolvesVerifiesAndExtractsExactManifestFiles()
    {
        using var fixture = new ReleaseFixture("4.7", "stable", prerelease: false);
        using var client = fixture.CreateClient();
        var source = new GitHubToolPackageReleaseSource(client, fixture.CacheRoot);

        var release = await source.GetLatestAsync(fixture.Catalog, ToolReleaseChannel.Stable);
        var provisioned = await source.ProvisionAsync(fixture.Catalog, Assert.IsType<ToolPackageRelease>(release));

        Assert.Equal("4.7", release?.Manifest.PackageVersion);
        Assert.True(File.Exists(Path.Combine(provisioned.PackageDirectory, "data", "modules", "main.lua")));
        Assert.Equal(2, Directory.EnumerateFiles(provisioned.PackageDirectory, "*", SearchOption.AllDirectories).Count());
    }

    [Fact]
    public async Task BetaRelease_UsesPrereleaseEndpointAndChannel()
    {
        using var fixture = new ReleaseFixture("4.8-beta.1", "beta", prerelease: true);
        using var client = fixture.CreateClient();
        var source = new GitHubToolPackageReleaseSource(client, fixture.CacheRoot);

        var release = await source.GetLatestAsync(fixture.Catalog, ToolReleaseChannel.Beta);

        Assert.NotNull(release);
        Assert.Equal(ToolReleaseChannel.Beta, release.Channel);
        Assert.Equal("beta", release.Manifest.Channel);
    }

    [Fact]
    public async Task StableRelease_WithRPrefixedTag_MatchesPackageVersion()
    {
        using var fixture = new ReleaseFixture("2.1", "stable", prerelease: false, tagPrefix: "r");
        using var client = fixture.CreateClient();
        var source = new GitHubToolPackageReleaseSource(client, fixture.CacheRoot);

        var release = await source.GetLatestAsync(fixture.Catalog, ToolReleaseChannel.Stable);

        Assert.NotNull(release);
        Assert.Equal("r2.1", release.Tag);
        Assert.Equal("2.1", release.Manifest.PackageVersion);
    }

    [Fact]
    public async Task Provision_WhenArchiveContainsUndeclaredFile_RejectsPackage()
    {
        using var fixture = new ReleaseFixture("4.7", "stable", prerelease: false, addUndeclaredFile: true);
        using var client = fixture.CreateClient();
        var source = new GitHubToolPackageReleaseSource(client, fixture.CacheRoot);
        var release = await source.GetLatestAsync(fixture.Catalog, ToolReleaseChannel.Stable);

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => source.ProvisionAsync(fixture.Catalog, Assert.IsType<ToolPackageRelease>(release)));

        Assert.Contains("undeclared", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ReleaseFixture : IDisposable
    {
        private const string Repository = "https://github.com/example/yal";
        private readonly byte[] _archive;
        private readonly byte[] _manifest;
        private readonly byte[] _metadata;
        private readonly string _version;

        private readonly string _tag;

        public ReleaseFixture(
            string version,
            string channel,
            bool prerelease,
            bool addUndeclaredFile = false,
            string tagPrefix = "v")
        {
            _version = version;
            _tag = tagPrefix + version;
            Root = Path.Combine(Path.GetTempPath(), $"xplane-tool-release-tests-{Guid.NewGuid():N}");
            CacheRoot = Path.Combine(Root, "cache");
            Directory.CreateDirectory(Root);
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["data/modules/main.lua"] = Encoding.UTF8.GetBytes("return true\n"),
                ["data/modules/configuration/version.ini"] = Encoding.UTF8.GetBytes(version)
            };
            _archive = BuildArchive(files, addUndeclaredFile);
            var archiveName = $"YAL-{version}.zip";
            var manifestName = $"YAL-{version}-manifest.json";
            _manifest = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 1,
                packageId = "wahltho.yal",
                packageVersion = version,
                releaseTag = _tag,
                channel,
                repository = Repository,
                installScope = "xPlaneInstallation",
                targetPath = "Resources/plugins/YAL",
                supportedProducts = new[] { "zibo-737ng", "levelup-737ng" },
                restartRequired = true,
                archive = new
                {
                    fileName = archiveName,
                    rootPath = "YAL",
                    size = _archive.LongLength,
                    sha256 = Hash(_archive)
                },
                protectedPaths = new[]
                {
                    "data/modules/configuration/configuration.ini",
                    "data/modules/configuration/wprefs.ini",
                    "data/output/**"
                },
                files = files.Select(file => new
                {
                    path = file.Key,
                    size = file.Value.LongLength,
                    sha256 = Hash(file.Value)
                })
            });
            _metadata = JsonSerializer.SerializeToUtf8Bytes(new
            {
                tag_name = _tag,
                html_url = $"{Repository}/releases/tag/{_tag}",
                draft = false,
                prerelease,
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
                        size = _archive.LongLength,
                        digest = "sha256:" + Hash(_archive)
                    }
                }
            });
            Channel = channel;
        }

        public string Root { get; }

        public string CacheRoot { get; }

        public string Channel { get; }

        public ContentPackageCatalogEntry Catalog { get; } = new()
        {
            PackageId = "wahltho.yal",
            DisplayName = "Yet Another Linda",
            Description = "Test tool",
            Category = ContentPackageCategory.Tool,
            Activation = ContentPatchActivation.ExplicitOptIn,
            SupportedProducts = ["zibo-737ng", "levelup-737ng"],
            RepositoryUrl = Repository,
            RestartRequired = true,
            InstallScope = "xPlaneInstallation",
            TargetPath = "Resources/plugins/YAL",
            SupportedChannels = ["stable", "beta"],
            Distribution = new ContentPackageDistribution
            {
                Kind = ContentPackageDistributionKind.GitHubToolRelease,
                ManifestAssetNamePattern = "YAL-*-manifest.json",
                ManifestSchemaVersion = 1
            }
        };

        public HttpClient CreateClient()
        {
            var manifestName = $"YAL-{_version}-manifest.json";
            var archiveName = $"YAL-{_version}.zip";
            var metadataUrl = Channel == "stable"
                ? "https://api.github.com/repos/example/yal/releases/latest"
                : "https://api.github.com/repos/example/yal/releases?per_page=30";
            var metadata = Channel == "stable"
                ? _metadata
                : Encoding.UTF8.GetBytes("[" + Encoding.UTF8.GetString(_metadata) + "]");
            return new HttpClient(new StubHandler(new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [metadataUrl] = metadata,
                [AssetUrl(manifestName)] = _manifest,
                [AssetUrl(archiveName)] = _archive
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

        private static byte[] BuildArchive(IReadOnlyDictionary<string, byte[]> files, bool addUndeclaredFile)
        {
            using var output = new MemoryStream();
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var file in files)
                {
                    WriteEntry(archive, "YAL/" + file.Key, file.Value);
                }

                if (addUndeclaredFile)
                {
                    WriteEntry(archive, "YAL/undeclared.txt", Encoding.UTF8.GetBytes("blocked"));
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

        private static string Hash(byte[] bytes) =>
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
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
