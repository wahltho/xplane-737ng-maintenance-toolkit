using System.IO.Compression;
using LevelUp.NavTableUpdater.Core.Aircraft;
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

    [Fact]
    public async Task CheckZiboAsync_WhenLocalSameBaselineOutdated_ReturnsReadOnlyPatchPlan()
    {
        var index = new ZiboUpstreamFeedParser().Parse(FeedXml);
        var source = new FakeUpdateIndexSource(index);
        var checker = new AircraftUpstreamUpdateChecker(source);

        var result = await checker.CheckZiboAsync(BuildVariant("zibo-737ng", "4.05.34"));

        Assert.Equal(1, source.LoadCount);
        Assert.Equal("Patch available", result.StateLabel);
        Assert.Equal("4.05.34", result.LocalVersionDisplay);
        Assert.Equal("4.05.35", result.AvailableVersionDisplay);
        Assert.Equal(AircraftUpdatePlanAction.ApplyCumulativePatch, result.Action);
        Assert.Contains("Read-only check", result.Findings[0]);
        var package = Assert.Single(result.RequiredPackages);
        Assert.Equal("B738X_XP12_4_05_35.zip", package.FileName);
    }

    [Fact]
    public async Task CheckZiboAsync_WhenVersionTxtIsMissing_ReadsVersionFromLuaFms()
    {
        var root = Path.Combine(Path.GetTempPath(), $"xplane-737ng-zibo-version-tests-{Guid.NewGuid():N}");
        var fmsDir = Path.Combine(root, "plugins", "xlua", "scripts", "B738.a_fms");
        Directory.CreateDirectory(fmsDir);
        await File.WriteAllTextAsync(
            Path.Combine(fmsDir, "B738.a_fms.lua"),
            "version = \"v4.05.35\"\n");
        var acfPath = Path.Combine(root, "b738_4k.acf");
        await File.WriteAllTextAsync(acfPath, "");

        try
        {
            var index = new ZiboUpstreamFeedParser().Parse(FeedXml);
            var source = new FakeUpdateIndexSource(index);
            var checker = new AircraftUpstreamUpdateChecker(source);

            var result = await checker.CheckZiboAsync(BuildVariant("zibo-737ng", localVersion: null, acfPath));

            Assert.Equal(1, source.LoadCount);
            Assert.Equal("Up to date", result.StateLabel);
            Assert.Equal("4.05.35", result.LocalVersionDisplay);
            Assert.Equal("4.05.35", result.AvailableVersionDisplay);
            Assert.Equal(AircraftUpdatePlanAction.UpToDate, result.Action);
            Assert.Empty(result.RequiredPackages);
            Assert.Contains(result.Findings, finding => finding.Contains("B738.a_fms.lua", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CheckZiboAsync_WhenVariantIsNotZibo_ReturnsNotApplicableWithoutLoadingIndex()
    {
        var index = new ZiboUpstreamFeedParser().Parse(FeedXml);
        var source = new FakeUpdateIndexSource(index);
        var checker = new AircraftUpstreamUpdateChecker(source);

        var result = await checker.CheckZiboAsync(BuildVariant("levelup-737ng", "2.S1.50B"));

        Assert.Equal(0, source.LoadCount);
        Assert.Equal("Not applicable", result.StateLabel);
        Assert.Equal(AircraftUpdatePlanAction.Unknown, result.Action);
        Assert.Empty(result.RequiredPackages);
    }

    [Fact]
    public async Task CheckZiboAsync_WhenMaintenanceMetadataMarksCustomPort_ReturnsReviewOnlyWithoutRequiredPackages()
    {
        using var fixture = AircraftUpdateFixture.Create();
        await File.WriteAllTextAsync(
            Path.Combine(fixture.AircraftPath, AircraftMaintenanceMetadata.FileName),
            """
            {
              "schemaVersion": 1,
              "aircraftFamily": "zibo-737ng",
              "distribution": "wahltho-no-lua-port",
              "distributionVersion": "5.00.00",
              "upstreamFamily": "zibo-737ng",
              "upstreamBaseVersion": "4.05.35",
              "runtime": "no-lua-cpp"
            }
            """);
        var index = new ZiboUpstreamFeedParser().Parse(FeedXml);
        var source = new FakeUpdateIndexSource(index);
        var checker = new AircraftUpstreamUpdateChecker(source);

        var result = await checker.CheckZiboAsync(BuildVariant("zibo-737ng", "5.00.00", fixture.AcfPath));

        Assert.Equal(1, source.LoadCount);
        Assert.True(result.IsCustomDistribution);
        Assert.Equal("Custom port detected", result.StateLabel);
        Assert.Equal("Review-only upstream information", result.ActionDisplay);
        Assert.Equal("5.00.00", result.LocalVersionDisplay);
        Assert.Equal("4.05.35", result.AvailableVersionDisplay);
        Assert.Empty(result.RequiredPackages);
        Assert.Contains(result.Findings, finding => finding.Contains("Official upstream packages are review-only", StringComparison.Ordinal));
    }

    [Fact]
    public void Cache_ImportZipCopiesExpectedPackageAndComputesHash()
    {
        using var fixture = AircraftUpdateFixture.Create();
        var package = BuildPatchPackage();
        var sourceZip = Path.Combine(fixture.Path, package.FileName);
        CreateZip(sourceZip, ("README.txt", "test package"));
        var cache = new AircraftUpdatePackageCache(Path.Combine(fixture.Path, "cache"));

        var imported = cache.ImportZip(sourceZip, package);
        var inspected = cache.Inspect(package);

        Assert.Equal(AircraftUpdatePackageCacheState.Imported, imported.State);
        Assert.True(File.Exists(imported.CachePath));
        Assert.Equal(64, imported.Sha256!.Length);
        Assert.Equal(AircraftUpdatePackageCacheState.Cached, inspected.State);
        Assert.Equal(imported.Sha256, inspected.Sha256);
        Assert.True(inspected.SizeBytes > 0);
    }

    [Fact]
    public void Cache_ImportZipWhenFilenameDiffers_ThrowsBeforeCopy()
    {
        using var fixture = AircraftUpdateFixture.Create();
        var package = BuildPatchPackage();
        var sourceZip = Path.Combine(fixture.Path, "wrong.zip");
        CreateZip(sourceZip, ("README.txt", "test package"));
        var cache = new AircraftUpdatePackageCache(Path.Combine(fixture.Path, "cache"));

        var ex = Assert.Throws<InvalidOperationException>(() => cache.ImportZip(sourceZip, package));

        Assert.Contains("does not match expected package", ex.Message);
        Assert.False(File.Exists(cache.GetPackagePath(package)));
    }

    [Fact]
    public void DryRun_ReadsZipEntriesProtectsLocalFilesAndBlocksUnsafePaths()
    {
        using var fixture = AircraftUpdateFixture.Create();
        var existingFile = Path.Combine(fixture.AircraftPath, "plugins", "xlua", "scripts", "B738.a_fms", "B738.a_fms.lua");
        Directory.CreateDirectory(Path.GetDirectoryName(existingFile)!);
        File.WriteAllText(existingFile, "existing");
        File.WriteAllText(Path.Combine(fixture.AircraftPath, "b738_4k_prefs.txt"), "prefs");

        var package = BuildPatchPackage();
        var zipPath = Path.Combine(fixture.Path, package.FileName);
        CreateZip(
            zipPath,
            ("new-file.txt", "new"),
            ("plugins/xlua/scripts/B738.a_fms/B738.a_fms.lua", "replacement"),
            ("b738_4k_prefs.txt", "prefs from package"),
            (AircraftMaintenanceMetadata.FileName, "{}"),
            ("../escape.txt", "unsafe"));
        var cache = new AircraftUpdatePackageCache(Path.Combine(fixture.Path, "cache"));
        var imported = cache.ImportZip(zipPath, package);

        var result = new AircraftUpdateDryRunAnalyzer().Analyze(fixture.AircraftPath, [imported]);

        Assert.False(result.Succeeded);
        Assert.Equal(1, result.AddCount);
        Assert.Equal(1, result.ReplaceCount);
        Assert.Equal(2, result.ProtectedCount);
        Assert.Equal(1, result.BlockedCount);
        Assert.Contains(result.Entries, entry => entry.RelativePath == "new-file.txt" && entry.Action == AircraftUpdateDryRunEntryAction.Add);
        Assert.Contains(result.Entries, entry => entry.RelativePath.EndsWith("B738.a_fms.lua", StringComparison.Ordinal) && entry.Action == AircraftUpdateDryRunEntryAction.Replace);
        Assert.Contains(result.Entries, entry => entry.RelativePath.EndsWith("b738_4k_prefs.txt", StringComparison.Ordinal) && entry.Action == AircraftUpdateDryRunEntryAction.PreserveProtectedLocalFile);
        Assert.Contains(result.Entries, entry => entry.RelativePath == "../escape.txt" && entry.Action == AircraftUpdateDryRunEntryAction.BlockedUnsafePath);
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

    private static AircraftUpdatePackage BuildPatchPackage() =>
        new(
            ZiboUpstreamFeedParser.Family,
            AircraftUpdatePackageKind.CumulativePatch,
            new AircraftUpstreamVersion(4, 5, 35),
            "B738X_XP12_4_05_35.zip",
            "https://skymatixva.com/tfiles/B738X_XP12_4_05_35.zip.torrent");

    private static void CreateZip(string path, params (string Name, string Content)[] entries)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
    }

    private static AircraftVariantViewAnalysis BuildVariant(string family, string? localVersion, string acfPath = "/tmp/test.acf") =>
        new(
            AircraftId: family == "zibo-737ng" ? "zibo-737-800-4k" : "levelup-737-800",
            DisplayName: family == "zibo-737ng" ? "Zibo 737-800 4K" : "LevelUp 737-800",
            Family: family,
            AcfPath: acfPath,
            PrefsPath: "/tmp/test_prefs.txt",
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

    private sealed class FakeUpdateIndexSource(AircraftUpdateIndex index) : IAircraftUpdateIndexSource
    {
        public int LoadCount { get; private set; }

        public Task<AircraftUpdateIndex> LoadAsync(CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.FromResult(index);
        }
    }

    private sealed class AircraftUpdateFixture : IDisposable
    {
        private AircraftUpdateFixture(string path)
        {
            Path = path;
            AircraftPath = System.IO.Path.Combine(path, "Aircraft", "B737-800X");
            AcfPath = System.IO.Path.Combine(AircraftPath, "b738_4k.acf");
            Directory.CreateDirectory(AircraftPath);
            File.WriteAllText(AcfPath, "");
        }

        public string Path { get; }

        public string AircraftPath { get; }

        public string AcfPath { get; }

        public static AircraftUpdateFixture Create() =>
            new(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"xplane-737ng-update-tests-{Guid.NewGuid():N}"));

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
