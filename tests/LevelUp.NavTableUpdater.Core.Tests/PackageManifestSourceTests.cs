using System.Net;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.Manifest;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class PackageManifestSourceTests
{
    [Fact]
    public async Task RefreshAsync_WhenReleaseManifestMatches_ReturnsReleaseManifest()
    {
        var seed = ManifestParser.ParsePipeManifest(TestFixtures.ManifestText);
        using var httpClient = new HttpClient(new StubHandler((request, _) =>
        {
            Assert.Equal("https://github.com/JT8D-17/X-Plane-LevelUp-737NG-Descent-Tables/releases/download/v0.2.0/package-manifest.txt", request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(TestFixtures.ManifestText.Replace("v0.2.0", "v0.2.1", StringComparison.Ordinal))
            };
        }));
        var source = new GitHubReleasePackageManifestSource(httpClient);

        var refreshed = await source.RefreshAsync(seed);

        Assert.Equal(seed.PackageId, refreshed.PackageId);
        Assert.Equal("v0.2.1", refreshed.PackageVersion);
    }

    [Fact]
    public async Task RefreshAsync_WhenPackageIdDiffers_Throws()
    {
        var seed = ManifestParser.ParsePipeManifest(TestFixtures.ManifestText);
        using var httpClient = new HttpClient(new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(TestFixtures.ManifestText.Replace(
                "x-plane-levelup-737ng-vnav-descent-tables",
                "wrong-package",
                StringComparison.Ordinal))
        }));
        var source = new GitHubReleasePackageManifestSource(httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(() => source.RefreshAsync(seed));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _handler;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_handler(request, cancellationToken));
    }
}
