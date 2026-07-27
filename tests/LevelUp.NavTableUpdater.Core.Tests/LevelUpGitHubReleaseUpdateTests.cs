using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Upstream;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class LevelUpGitHubReleaseUpdateTests
{
    private static readonly Version ToolkitVersion = new(0, 3, 6);

    [Fact]
    public async Task Source_LoadsAndCrossValidatesFullAndCumulativePackages()
    {
        var fixture = CreateReleaseFixture();
        using var client = fixture.CreateClient();
        var source = new LevelUpGitHubReleaseIndexSource(client, ToolkitVersion, fixture.IndexUrl);

        var index = await source.LoadAsync();

        Assert.Equal(LevelUpGitHubReleaseIndexSource.Family, index.Family);
        Assert.Equal(fixture.IndexUrl, index.SourceUrl);
        Assert.Collection(
            index.Packages.OrderBy(package => package.Kind),
            full =>
            {
                Assert.Equal(AircraftUpdatePackageKind.FullBaseline, full.Kind);
                Assert.Equal("v2.S1.50C", full.ReleaseVersion);
                Assert.Equal(3, full.Version.Patch);
                Assert.EndsWith("/LU%20full.7z", full.SourceUrl, StringComparison.Ordinal);
                Assert.NotNull(full.Manifest);
            },
            patch =>
            {
                Assert.Equal(AircraftUpdatePackageKind.CumulativePatch, patch.Kind);
                Assert.Equal("V2.S1", patch.BaselineVersion);
                Assert.Equal(["2.S1", "2.S1.0"], patch.Manifest!.BaselineAliases);
                Assert.EndsWith("/LU%20patch.7z", patch.SourceUrl, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task Source_WhenManifestHashDiffers_RejectsRelease()
    {
        var fixture = CreateReleaseFixture(corruptPatchManifestHash: true);
        using var client = fixture.CreateClient();
        var source = new LevelUpGitHubReleaseIndexSource(client, ToolkitVersion, fixture.IndexUrl);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => source.LoadAsync());

        Assert.Contains("manifest SHA-256 mismatch", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Checker_WithPublishedBaselineSelectsOnlyCumulativePatch()
    {
        var checker = CreateChecker(CreateReleaseFixture());

        var result = await checker.CheckAsync(BuildVariant("V2.S1"));

        Assert.Equal(AircraftUpdatePlanAction.ApplyCumulativePatch, result.Action);
        Assert.Equal(AircraftUpdateMode.Incremental, result.UpdateMode);
        Assert.Equal("v2.S1.50C", result.AvailableVersionDisplay);
        Assert.Equal(AircraftUpdatePackageKind.CumulativePatch, Assert.Single(result.RequiredPackages).Kind);
    }

    [Fact]
    public async Task Checker_WithUnknownBaselineSelectsExactFullPackage()
    {
        var checker = CreateChecker(CreateReleaseFixture());

        var result = await checker.CheckAsync(BuildVariant(null));

        Assert.Equal(AircraftUpdatePlanAction.InstallBaselineAndCumulativePatch, result.Action);
        Assert.Equal(AircraftUpdateMode.Full, result.UpdateMode);
        Assert.Equal(AircraftUpdatePackageKind.FullBaseline, Assert.Single(result.RequiredPackages).Kind);
    }

    [Fact]
    public async Task Checker_WhenInstalledVersionMatchesNeedsNoPackage()
    {
        var checker = CreateChecker(CreateReleaseFixture());

        var result = await checker.CheckAsync(BuildVariant("2.S1.50C"));

        Assert.Equal(AircraftUpdatePlanAction.UpToDate, result.Action);
        Assert.Empty(result.RequiredPackages);
        Assert.Equal("Up to date", result.StateLabel);
    }

    [Fact]
    public async Task Source_WhenToolkitIsTooOldRejectsRelease()
    {
        var fixture = CreateReleaseFixture(minimumToolkitVersion: "0.4.0");
        using var client = fixture.CreateClient();
        var source = new LevelUpGitHubReleaseIndexSource(client, ToolkitVersion, fixture.IndexUrl);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => source.LoadAsync());

        Assert.Contains("requires toolkit 0.4.0 or newer", exception.Message, StringComparison.Ordinal);
    }

    private static LevelUpReleaseUpdateChecker CreateChecker(ReleaseFixture fixture)
    {
        var client = fixture.CreateClient();
        return new LevelUpReleaseUpdateChecker(
            new LevelUpGitHubReleaseIndexSource(client, ToolkitVersion, fixture.IndexUrl));
    }

    private static ReleaseFixture CreateReleaseFixture(
        bool corruptPatchManifestHash = false,
        string minimumToolkitVersion = "0.3.6")
    {
        const string indexUrl = "https://example.test/release-index.json";
        const string assetBaseUrl =
            "https://github.com/petrolpram/737NG-Updates/releases/download/v2.S1.50C";
        const string fullArchiveSha = "1111111111111111111111111111111111111111111111111111111111111111";
        const string patchArchiveSha = "2222222222222222222222222222222222222222222222222222222222222222";

        var fullManifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            productId = "levelup-737ng",
            packageType = "full",
            releaseVersion = "v2.S1.50C",
            releaseSequence = 3,
            contentRoot = "737NG Series_v2.S1.50C",
            files = Array.Empty<object>(),
            deletedPaths = Array.Empty<string>(),
            archive = new
            {
                fileName = "LU full.7z",
                size = 900L,
                sha256 = fullArchiveSha
            }
        });
        var patchManifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            productId = "levelup-737ng",
            packageType = "cumulativePatch",
            baselineVersion = "V2.S1",
            baselineAliases = new[] { "2.S1", "2.S1.0" },
            targetVersion = "v2.S1.50C",
            releaseSequence = 3,
            contentRoot = "737NG Series_v2.S1.50C",
            files = Array.Empty<object>(),
            deletedPaths = Array.Empty<string>(),
            archive = new
            {
                fileName = "LU patch.7z",
                size = 70L,
                sha256 = patchArchiveSha
            }
        });
        var fullManifestHash = Sha256(fullManifest);
        var patchManifestHash = corruptPatchManifestHash
            ? new string('f', 64)
            : Sha256(patchManifest);
        var index = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            productId = "levelup-737ng",
            repository = "petrolpram/737NG-Updates",
            releaseVersion = "v2.S1.50C",
            releaseSequence = 3,
            releaseTag = "v2.S1.50C",
            releaseChannel = "stable",
            minimumToolkitVersion,
            packages = new object[]
            {
                new
                {
                    packageType = "full",
                    releaseVersion = "v2.S1.50C",
                    manifestFile = "LU full.manifest.json",
                    manifestSha256 = fullManifestHash,
                    archiveFile = "LU full.7z",
                    archiveSize = 900L,
                    archiveSha256 = fullArchiveSha
                },
                new
                {
                    packageType = "cumulativePatch",
                    releaseVersion = "v2.S1.50C",
                    baselineVersion = "V2.S1",
                    baselineAliases = new[] { "2.S1", "2.S1.0" },
                    manifestFile = "LU patch.manifest.json",
                    manifestSha256 = patchManifestHash,
                    archiveFile = "LU patch.7z",
                    archiveSize = 70L,
                    archiveSha256 = patchArchiveSha
                }
            }
        });

        return new ReleaseFixture(
            indexUrl,
            new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [indexUrl] = index,
                [$"{assetBaseUrl}/LU%20full.manifest.json"] = fullManifest,
                [$"{assetBaseUrl}/LU%20patch.manifest.json"] = patchManifest
            });
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static AircraftVariantViewAnalysis BuildVariant(string? localVersion) =>
        new(
            AircraftId: "levelup-737-800",
            DisplayName: "LevelUp 737-800",
            Family: LevelUpAircraftUpdatePackageLoader.Family,
            AcfPath: "/tmp/737NG Series/737_80NG.acf",
            PrefsPath: "/tmp/737NG Series/737_80NG_prefs.txt",
            Source: "test",
            SourceRef: "test",
            SourceVersion: localVersion ?? "",
            LocalVersion: localVersion,
            AcfVersion: null,
            FileWriterVersion: null,
            CurrentCgYFeet: null,
            CurrentCgZFeet: null,
            ReferenceCgYFeet: 0,
            ReferenceCgZFeet: 0,
            DeltaYFeet: null,
            DeltaZFeet: null,
            DeltaYMeters: null,
            DeltaZMeters: null,
            Status: "test",
            IdentityStatus: "Expected metadata",
            QuickViewStatus: "test",
            DefaultViewStatus: "test");

    private sealed record ReleaseFixture(
        string IndexUrl,
        IReadOnlyDictionary<string, byte[]> Responses)
    {
        public HttpClient CreateClient() => new(new StubHandler(Responses));
    }

    private sealed class StubHandler(IReadOnlyDictionary<string, byte[]> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.AbsoluteUri ?? "";
            if (!responses.TryGetValue(url, out var body))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body)
            });
        }
    }
}
