namespace LevelUp.NavTableUpdater.Core.Upstream;

public sealed class ZiboFeedAircraftUpdateIndexSource : IAircraftUpdateIndexSource
{
    private readonly HttpClient _httpClient;
    private readonly ZiboUpstreamFeedParser _parser;

    public ZiboFeedAircraftUpdateIndexSource(
        HttpClient httpClient,
        ZiboUpstreamFeedParser? parser = null,
        string feedUrl = ZiboUpstreamFeedParser.DefaultFeedUrl)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(feedUrl);

        _httpClient = httpClient;
        _parser = parser ?? new ZiboUpstreamFeedParser();
        FeedUrl = feedUrl;
    }

    public string FeedUrl { get; }

    public async Task<AircraftUpdateIndex> LoadAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(FeedUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        var feedXml = await response.Content.ReadAsStringAsync(cancellationToken);
        return _parser.Parse(feedXml, FeedUrl);
    }
}
