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

    [Fact]
    public async Task OverlayRelease_WithArchiveRootFiles_ResolvesAndExtractsExactPaths()
    {
        using var fixture = new ReleaseFixture("0.1.3", "stable", prerelease: false, overlay: true);
        using var client = fixture.CreateClient();
        var source = new GitHubToolPackageReleaseSource(client, fixture.CacheRoot);

        var release = await source.GetLatestAsync(fixture.Catalog, ToolReleaseChannel.Stable);
        var provisioned = await source.ProvisionAsync(fixture.Catalog, Assert.IsType<ToolPackageRelease>(release));

        Assert.Equal("xPlaneOverlay", release?.Manifest.Layout);
        Assert.True(File.Exists(Path.Combine(
            provisioned.PackageDirectory,
            "Resources", "plugins", "DataRefMonitor", "64", "mac.xpl")));
        Assert.True(File.Exists(Path.Combine(
            provisioned.PackageDirectory,
            "Output", "preferences", "DataRefMonitor.prf")));
    }

    private sealed class ReleaseFixture : IDisposable
    {
        private readonly string _repository;
        private readonly byte[] _archive;
        private readonly byte[] _manifest;
        private readonly byte[] _metadata;
        private readonly string _archiveName;
        private readonly string _manifestName;
        private readonly string _version;

        private readonly string _tag;

        public ReleaseFixture(
            string version,
            string channel,
            bool prerelease,
            bool addUndeclaredFile = false,
            string tagPrefix = "v",
            bool overlay = false)
        {
            _version = version;
            _tag = tagPrefix + version;
            _repository = overlay
                ? "https://github.com/example/realbench"
                : "https://github.com/example/yal";
            Root = Path.Combine(Path.GetTempPath(), $"xplane-tool-release-tests-{Guid.NewGuid():N}");
            CacheRoot = Path.Combine(Root, "cache");
            Directory.CreateDirectory(Root);
            var files = overlay
                ? new Dictionary<string, byte[]>(StringComparer.Ordinal)
                {
                    ["Resources/plugins/DataRefMonitor/64/mac.xpl"] = Encoding.UTF8.GetBytes("plugin"),
                    ["Output/preferences/DataRefMonitor.prf"] = Encoding.UTF8.GetBytes("preferences")
                }
                : new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["data/modules/main.lua"] = Encoding.UTF8.GetBytes("return true\n"),
                ["data/modules/configuration/version.ini"] = Encoding.UTF8.GetBytes(version)
            };
            _archive = BuildArchive(files, addUndeclaredFile, overlay ? "" : "YAL/");
            _archiveName = overlay ? $"737NGRealbenchLogger-{version}-toolkit.zip" : $"YAL-{version}.zip";
            _manifestName = overlay ? $"737NGRealbenchLogger-{version}-manifest.json" : $"YAL-{version}-manifest.json";
            _manifest = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = overlay ? 2 : 1,
                packageId = overlay ? "wahltho.737ng-realbench-logger" : "wahltho.yal",
                packageVersion = version,
                releaseTag = _tag,
                channel,
                repository = _repository,
                installScope = "xPlaneInstallation",
                layout = overlay ? "xPlaneOverlay" : "directory",
                targetPath = overlay ? "" : "Resources/plugins/YAL",
                supportedProducts = new[] { "zibo-737ng", "levelup-737ng" },
                restartRequired = true,
                archive = new
                {
                    fileName = _archiveName,
                    rootPath = overlay ? "" : "YAL",
                    size = _archive.LongLength,
                    sha256 = Hash(_archive)
                },
                protectedPaths = overlay
                    ? ["Output/DataRefMonitor/**"]
                    : new[]
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
                html_url = $"{_repository}/releases/tag/{_tag}",
                draft = false,
                prerelease,
                assets = new[]
                {
                    new
                    {
                        name = _manifestName,
                        browser_download_url = AssetUrl(_manifestName),
                        size = _manifest.LongLength,
                        digest = "sha256:" + Hash(_manifest)
                    },
                    new
                    {
                        name = _archiveName,
                        browser_download_url = AssetUrl(_archiveName),
                        size = _archive.LongLength,
                        digest = "sha256:" + Hash(_archive)
                    }
                }
            });
            Channel = channel;
            Catalog = new ContentPackageCatalogEntry
            {
                PackageId = overlay ? "wahltho.737ng-realbench-logger" : "wahltho.yal",
                DisplayName = overlay ? "737NG Realbench Logger" : "Yet Another Linda",
                Description = "Test tool",
                Category = ContentPackageCategory.Tool,
                Activation = ContentPatchActivation.ExplicitOptIn,
                SupportedProducts = ["zibo-737ng", "levelup-737ng"],
                RepositoryUrl = _repository,
                RestartRequired = true,
                InstallScope = "xPlaneInstallation",
                TargetPath = overlay ? "" : "Resources/plugins/YAL",
                SupportedChannels = ["stable", "beta"],
                Distribution = new ContentPackageDistribution
                {
                    Kind = overlay
                        ? ContentPackageDistributionKind.GitHubXPlaneOverlayRelease
                        : ContentPackageDistributionKind.GitHubToolRelease,
                    ManifestAssetNamePattern = overlay
                        ? "737NGRealbenchLogger-*-manifest.json"
                        : "YAL-*-manifest.json",
                    ManifestSchemaVersion = overlay ? 2 : 1
                }
            };
        }

        public string Root { get; }

        public string CacheRoot { get; }

        public string Channel { get; }

        public ContentPackageCatalogEntry Catalog { get; }

        public HttpClient CreateClient()
        {
            var uri = new Uri(_repository);
            var repositoryPath = uri.AbsolutePath.Trim('/');
            var metadataUrl = Channel == "stable"
                ? $"https://api.github.com/repos/{repositoryPath}/releases/latest"
                : $"https://api.github.com/repos/{repositoryPath}/releases?per_page=30";
            var metadata = Channel == "stable"
                ? _metadata
                : Encoding.UTF8.GetBytes("[" + Encoding.UTF8.GetString(_metadata) + "]");
            return new HttpClient(new StubHandler(new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [metadataUrl] = metadata,
                [AssetUrl(_manifestName)] = _manifest,
                [AssetUrl(_archiveName)] = _archive
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
            $"{_repository}/releases/download/{_tag}/{name}";

        private static byte[] BuildArchive(
            IReadOnlyDictionary<string, byte[]> files,
            bool addUndeclaredFile,
            string prefix)
        {
            using var output = new MemoryStream();
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var file in files)
                {
                    WriteEntry(archive, prefix + file.Key, file.Value);
                }

                if (addUndeclaredFile)
                {
                    WriteEntry(archive, prefix + "undeclared.txt", Encoding.UTF8.GetBytes("blocked"));
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
