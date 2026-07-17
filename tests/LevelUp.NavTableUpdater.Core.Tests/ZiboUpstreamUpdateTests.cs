using LevelUp.NavTableUpdater.Core.Upstream;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class ZiboUpstreamUpdateTests
{
    private const string FeedXml = """
        <?xml version="1.0" encoding="UTF-8" ?>
        <rss version="2.0">
          <channel>
            <title>Zibo Mod Files</title>
            <item>
              <title>B737-800X_XP12_4_05_full.zip</title>
              <link>https://skymatixva.com/tfiles/B737-800X_XP12_4_05_full.zip.torrent</link>
              <description>Full new base file for the Zibo Mod V4.05</description>
            </item>
            <item>
              <title>B738X_XP12_4_05_34.zip</title>
              <link>https://skymatixva.com/tfiles/B738X_XP12_4_05_34.zip.torrent</link>
              <description>Fix file for the Zibo Mod V4.05</description>
            </item>
            <item>
              <title>B738X_XP12_4_05_35.zip</title>
              <link>https://skymatixva.com/tfiles/B738X_XP12_4_05_35.zip.torrent</link>
              <description>Fix file for the Zibo Mod V4.05</description>
            </item>
            <item>
              <title>not-a-zibo-package.txt</title>
              <link>https://example.invalid/not-a-zibo-package.txt</link>
            </item>
          </channel>
        </rss>
        """;

    [Fact]
    public void Parse_ReadsFullBaselineAndCumulativePatchPackages()
    {
        var index = new ZiboUpstreamFeedParser().Parse(FeedXml);

        Assert.Equal(ZiboUpstreamFeedParser.Family, index.Family);
        Assert.Equal(3, index.Packages.Count);

        var full = Assert.Single(index.Packages, package => package.Kind == AircraftUpdatePackageKind.FullBaseline);
        Assert.Equal("B737-800X_XP12_4_05_full.zip", full.FileName);
        Assert.Equal(new AircraftUpstreamVersion(4, 5, 0), full.Version);
        Assert.Equal(new AircraftUpstreamVersion(4, 5, 0), full.Baseline);

        var latestPatch = index.Packages.Last(package => package.Kind == AircraftUpdatePackageKind.CumulativePatch);
        Assert.Equal("B738X_XP12_4_05_35.zip", latestPatch.FileName);
        Assert.Equal(new AircraftUpstreamVersion(4, 5, 35), latestPatch.Version);
        Assert.EndsWith(".torrent", latestPatch.SourceUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_WhenLocalSameBaselineOutdated_RequiresOnlyLatestCumulativePatch()
    {
        var index = new ZiboUpstreamFeedParser().Parse(FeedXml);

        var plan = new BaselineCumulativeUpdatePlanner().Plan(index, new AircraftUpstreamVersion(4, 5, 31));

        Assert.Equal(AircraftUpdatePlanAction.ApplyCumulativePatch, plan.Action);
        Assert.True(plan.HasUpdate);
        Assert.Equal(new AircraftUpstreamVersion(4, 5, 35), plan.AvailableVersion);
        var package = Assert.Single(plan.RequiredPackages);
        Assert.Equal("B738X_XP12_4_05_35.zip", package.FileName);
    }

    [Fact]
    public void Plan_WhenLocalOlderBaseline_RequiresFullBaselineAndLatestCumulativePatch()
    {
        var index = new ZiboUpstreamFeedParser().Parse(FeedXml);

        var plan = new BaselineCumulativeUpdatePlanner().Plan(index, new AircraftUpstreamVersion(4, 4, 12));

        Assert.Equal(AircraftUpdatePlanAction.InstallBaselineAndCumulativePatch, plan.Action);
        Assert.True(plan.HasUpdate);
        Assert.Equal(new AircraftUpstreamVersion(4, 5, 0), plan.TargetBaseline);
        Assert.Equal(
            ["B737-800X_XP12_4_05_full.zip", "B738X_XP12_4_05_35.zip"],
            plan.RequiredPackages.Select(package => package.FileName).ToArray());
    }

    [Fact]
    public void Plan_WhenNewFullBaselineIsNewest_DoesNotPreferOldBaselinePatch()
    {
        const string feedXml = """
            <rss version="2.0">
              <channel>
                <item>
                  <title>B737-800X_XP12_4_05_full.zip</title>
                  <link>https://skymatixva.com/tfiles/B737-800X_XP12_4_05_full.zip.torrent</link>
                </item>
                <item>
                  <title>B738X_XP12_4_05_35.zip</title>
                  <link>https://skymatixva.com/tfiles/B738X_XP12_4_05_35.zip.torrent</link>
                </item>
                <item>
                  <title>B737-800X_XP12_4_06_full.zip</title>
                  <link>https://skymatixva.com/tfiles/B737-800X_XP12_4_06_full.zip.torrent</link>
                </item>
              </channel>
            </rss>
            """;
        var index = new ZiboUpstreamFeedParser().Parse(feedXml);

        var plan = new BaselineCumulativeUpdatePlanner().Plan(index, new AircraftUpstreamVersion(4, 5, 35));

        Assert.Equal(AircraftUpdatePlanAction.InstallBaselineAndCumulativePatch, plan.Action);
        Assert.Equal(new AircraftUpstreamVersion(4, 6, 0), plan.AvailableVersion);
        Assert.Equal(new AircraftUpstreamVersion(4, 6, 0), plan.TargetBaseline);
        var package = Assert.Single(plan.RequiredPackages);
        Assert.Equal("B737-800X_XP12_4_06_full.zip", package.FileName);
    }

    [Fact]
    public void Plan_WhenLocalVersionIsMissing_RequiresFullBaselineAndLatestCumulativePatch()
    {
        var index = new ZiboUpstreamFeedParser().Parse(FeedXml);

        var plan = new BaselineCumulativeUpdatePlanner().Plan(index, localVersion: null);

        Assert.Equal(AircraftUpdatePlanAction.InstallBaselineAndCumulativePatch, plan.Action);
        Assert.Equal(
            ["B737-800X_XP12_4_05_full.zip", "B738X_XP12_4_05_35.zip"],
            plan.RequiredPackages.Select(package => package.FileName).ToArray());
    }

    [Fact]
    public void Plan_WhenLocalIsCurrent_ReturnsUpToDate()
    {
        var index = new ZiboUpstreamFeedParser().Parse(FeedXml);

        var plan = new BaselineCumulativeUpdatePlanner().Plan(index, new AircraftUpstreamVersion(4, 5, 35));

        Assert.Equal(AircraftUpdatePlanAction.UpToDate, plan.Action);
        Assert.False(plan.HasUpdate);
        Assert.Empty(plan.RequiredPackages);
    }

    [Fact]
    public void Plan_WhenLocalIsNewerThanFeed_ReturnsLocalNewerThanIndex()
    {
        var index = new ZiboUpstreamFeedParser().Parse(FeedXml);

        var plan = new BaselineCumulativeUpdatePlanner().Plan(index, new AircraftUpstreamVersion(4, 5, 36));

        Assert.Equal(AircraftUpdatePlanAction.LocalNewerThanIndex, plan.Action);
        Assert.False(plan.HasUpdate);
        Assert.Empty(plan.RequiredPackages);
    }

    [Fact]
    public void Plan_IsFamilyAgnosticForFutureLevelUpSource()
    {
        var index = new AircraftUpdateIndex(
            "levelup-737ng",
            "https://example.invalid/levelup-feed.json",
            [
                new AircraftUpdatePackage(
                    "levelup-737ng",
                    AircraftUpdatePackageKind.FullBaseline,
                    new AircraftUpstreamVersion(2, 4, 0),
                    "LevelUp_737NG_v2_04_full.zip",
                    "https://example.invalid/LevelUp_737NG_v2_04_full.zip"),
                new AircraftUpdatePackage(
                    "levelup-737ng",
                    AircraftUpdatePackageKind.CumulativePatch,
                    new AircraftUpstreamVersion(2, 4, 3),
                    "LevelUp_737NG_v2_04_03.zip",
                    "https://example.invalid/LevelUp_737NG_v2_04_03.zip")
            ]);

        var plan = new BaselineCumulativeUpdatePlanner().Plan(index, new AircraftUpstreamVersion(2, 3, 9));

        Assert.Equal("levelup-737ng", plan.Family);
        Assert.Equal(AircraftUpdatePlanAction.InstallBaselineAndCumulativePatch, plan.Action);
        Assert.Equal(
            ["LevelUp_737NG_v2_04_full.zip", "LevelUp_737NG_v2_04_03.zip"],
            plan.RequiredPackages.Select(package => package.FileName).ToArray());
    }

    [Theory]
    [InlineData("4.05.35", 4, 5, 35)]
    [InlineData("Zibo 4.05", 4, 5, 0)]
    [InlineData("B738X_XP12_4_05_35.zip", 0, 0, 0)]
    public void TryParse_ParsesDotVersionsOnly(string value, int major, int minor, int patch)
    {
        var ok = AircraftUpstreamVersion.TryParse(value, out var version);

        if (major == 0)
        {
            Assert.False(ok);
            return;
        }

        Assert.True(ok);
        Assert.Equal(new AircraftUpstreamVersion(major, minor, patch), version);
    }
}
