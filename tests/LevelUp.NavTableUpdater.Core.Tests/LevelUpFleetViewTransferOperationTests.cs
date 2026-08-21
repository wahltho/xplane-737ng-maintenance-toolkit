using System.Text;
using LevelUp.NavTableUpdater.Core.Aircraft;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class LevelUpFleetViewTransferOperationTests
{
    [Fact]
    public void Apply_CopiesQuickViewsToOtherLevelUpVariantsWithCgCorrectionAndBackups()
    {
        using var fixture = FleetFixture.Create();
        var store = TestToolStateStore.Create(fixture.Root);
        var operation = new LevelUpFleetViewTransferOperation(store, isXPlaneRunning: () => false);
        var sourceBefore = File.ReadAllBytes(fixture.Source.PrefsPath);

        var result = operation.Apply(fixture.Source, fixture.Variants, setDefaultViewFromQuickView0: true);

        Assert.True(result.Succeeded);
        Assert.True(result.Changed);
        Assert.Equal(4, result.BackupPaths.Count);
        Assert.Equal(sourceBefore, File.ReadAllBytes(fixture.Source.PrefsPath));

        var shortPrefs = File.ReadAllText(fixture.ShortTarget.PrefsPath);
        Assert.Contains("target-short-setting keep", shortPrefs, StringComparison.Ordinal);
        Assert.Contains("_iql_pe_y_0 2.000000\r\n", shortPrefs, StringComparison.Ordinal);
        Assert.Contains("_iql_pe_z_0 4.219200\r\n", shortPrefs, StringComparison.Ordinal);
        Assert.Contains("_iql_look_os_psi_1 12.000000\r\n", shortPrefs, StringComparison.Ordinal);
        Assert.DoesNotContain("_iql_pe_z_0 99.000000", shortPrefs, StringComparison.Ordinal);
        var shortBytes = File.ReadAllBytes(fixture.ShortTarget.PrefsPath);
        Assert.True(shortBytes.AsSpan(0, 3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.DoesNotContain("\n", shortPrefs.Replace("\r\n", "", StringComparison.Ordinal));

        var longPrefs = File.ReadAllText(fixture.LongTarget.PrefsPath);
        Assert.Contains("target-long-setting keep\n", longPrefs, StringComparison.Ordinal);
        Assert.Contains("_iql_pe_y_0 1.695200\n", longPrefs, StringComparison.Ordinal);
        Assert.Contains("_iql_pe_z_0 -1.876800\n", longPrefs, StringComparison.Ordinal);

        AssertDefaultViewMatchesTransferredQv0(fixture.ShortTarget);
        AssertDefaultViewMatchesTransferredQv0(fixture.LongTarget);

        var state = store.Load();
        Assert.Equal(2, state.Aircraft.Count);
        Assert.All(state.Aircraft.Values, target =>
        {
            Assert.Equal("TransferLevelUpFleetViews", target.LastOperation);
            Assert.Equal($"CopiedFrom:{fixture.Source.AircraftId}", target.LastQuickViewBaselineSource);
            Assert.Equal(2, target.Backups.Count);
        });
    }

    [Fact]
    public void Apply_WhenDefaultViewTransferIsDeclined_CopiesQuickViewsAndRetainsAcfDefaults()
    {
        using var fixture = FleetFixture.Create();
        var store = TestToolStateStore.Create(fixture.Root);
        var operation = new LevelUpFleetViewTransferOperation(store, isXPlaneRunning: () => false);
        var shortAcfBefore = File.ReadAllBytes(fixture.ShortTarget.AcfPath);
        var longAcfBefore = File.ReadAllBytes(fixture.LongTarget.AcfPath);

        var result = operation.Apply(fixture.Source, fixture.Variants, setDefaultViewFromQuickView0: false);

        Assert.True(result.Succeeded);
        Assert.True(result.Changed);
        Assert.Equal(2, result.BackupPaths.Count);
        Assert.Equal(shortAcfBefore, File.ReadAllBytes(fixture.ShortTarget.AcfPath));
        Assert.Equal(longAcfBefore, File.ReadAllBytes(fixture.LongTarget.AcfPath));
        Assert.Contains("Existing Default Viewpoints were retained", result.Message, StringComparison.Ordinal);
        Assert.Contains("_iql_pe_z_0 4.219200", File.ReadAllText(fixture.ShortTarget.PrefsPath), StringComparison.Ordinal);
        Assert.Contains("_iql_pe_z_0 -1.876800", File.ReadAllText(fixture.LongTarget.PrefsPath), StringComparison.Ordinal);

        var state = store.Load();
        Assert.All(state.Aircraft.Values, target =>
        {
            Assert.Single(target.Backups);
            Assert.Null(target.LastDefaultViewCgYFeet);
            Assert.Null(target.LastDefaultViewCgZFeet);
            Assert.Null(target.LastDefaultViewAppliedUtc);
        });
    }

    [Fact]
    public void Apply_WhenRunAgain_IsIdempotentAndCreatesNoAdditionalBackups()
    {
        using var fixture = FleetFixture.Create();
        var store = TestToolStateStore.Create(fixture.Root);
        var operation = new LevelUpFleetViewTransferOperation(store, isXPlaneRunning: () => false);

        var first = operation.Apply(fixture.Source, fixture.Variants, setDefaultViewFromQuickView0: true);
        var second = operation.Apply(fixture.Source, fixture.Variants, setDefaultViewFromQuickView0: true);

        Assert.True(first.Changed);
        Assert.True(second.Succeeded);
        Assert.False(second.Changed);
        Assert.Empty(second.BackupPaths);
        Assert.Equal(4, Directory.GetFiles(store.BackupRootPath, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void Apply_WhenAnyTargetIsInvalid_BlocksBeforeChangingAnyTarget()
    {
        using var fixture = FleetFixture.Create();
        File.Delete(fixture.LongTarget.PrefsPath);
        var shortPrefsBefore = File.ReadAllBytes(fixture.ShortTarget.PrefsPath);
        var shortAcfBefore = File.ReadAllBytes(fixture.ShortTarget.AcfPath);
        var store = TestToolStateStore.Create(fixture.Root);
        var operation = new LevelUpFleetViewTransferOperation(store, isXPlaneRunning: () => false);

        Assert.Throws<FileNotFoundException>(() => operation.Apply(fixture.Source, fixture.Variants, setDefaultViewFromQuickView0: true));

        Assert.Equal(shortPrefsBefore, File.ReadAllBytes(fixture.ShortTarget.PrefsPath));
        Assert.Equal(shortAcfBefore, File.ReadAllBytes(fixture.ShortTarget.AcfPath));
        Assert.False(File.Exists(store.StatePath));
        Assert.False(Directory.Exists(store.BackupRootPath));
    }

    [Fact]
    public void Apply_WhenSourceHasDuplicateQuickViewKey_BlocksBeforeWriting()
    {
        using var fixture = FleetFixture.Create();
        File.AppendAllText(fixture.Source.PrefsPath, "_iql_pe_y_0 7.000000\n", new UTF8Encoding(false));
        var targetBefore = File.ReadAllBytes(fixture.ShortTarget.PrefsPath);
        var store = TestToolStateStore.Create(fixture.Root);
        var operation = new LevelUpFleetViewTransferOperation(store, isXPlaneRunning: () => false);

        var ex = Assert.Throws<InvalidOperationException>(() => operation.Apply(fixture.Source, fixture.Variants, setDefaultViewFromQuickView0: true));

        Assert.Contains("must be unique", ex.Message, StringComparison.Ordinal);
        Assert.Equal(targetBefore, File.ReadAllBytes(fixture.ShortTarget.PrefsPath));
        Assert.False(File.Exists(store.StatePath));
    }

    [Fact]
    public void Apply_ForZiboSource_ReturnsBlockedWithoutWriting()
    {
        using var fixture = FleetFixture.Create();
        var ziboSource = fixture.Source with { Family = AircraftProductIds.Zibo737Ng };
        var store = TestToolStateStore.Create(fixture.Root);
        var operation = new LevelUpFleetViewTransferOperation(store, isXPlaneRunning: () => false);

        var result = operation.Apply(ziboSource, fixture.Variants, setDefaultViewFromQuickView0: true);

        Assert.False(result.Succeeded);
        Assert.Equal("Blocked", result.Status);
        Assert.False(File.Exists(store.StatePath));
    }

    [Fact]
    public void Apply_WhenXPlaneIsRunning_ReturnsBlockedWithoutWriting()
    {
        using var fixture = FleetFixture.Create();
        var store = TestToolStateStore.Create(fixture.Root);
        var operation = new LevelUpFleetViewTransferOperation(store, isXPlaneRunning: () => true);

        var result = operation.Apply(fixture.Source, fixture.Variants, setDefaultViewFromQuickView0: true);

        Assert.False(result.Succeeded);
        Assert.Equal("Blocked", result.Status);
        Assert.False(File.Exists(store.StatePath));
    }

    private static void AssertDefaultViewMatchesTransferredQv0(AircraftVariantViewAnalysis target)
    {
        var metadata = AircraftFileParser.ReadAcfMetadata(target.AcfPath);
        var qv0 = AircraftFileParser.ReadQuickView0(target.PrefsPath);
        var expected = AircraftFileParser.CalculateDefaultViewFromQuickView(metadata.Cg!, qv0!);
        Assert.Equal(expected.XFeet, metadata.DefaultView!.XFeet, precision: 6);
        Assert.Equal(expected.YFeet, metadata.DefaultView.YFeet, precision: 6);
        Assert.Equal(expected.ZFeet, metadata.DefaultView.ZFeet, precision: 6);
        Assert.Equal(expected.PitchDegrees, metadata.DefaultView.PitchDegrees, precision: 6);
    }

    private sealed class FleetFixture : IDisposable
    {
        private FleetFixture(
            string root,
            AircraftVariantViewAnalysis source,
            AircraftVariantViewAnalysis shortTarget,
            AircraftVariantViewAnalysis longTarget)
        {
            Root = root;
            Source = source;
            ShortTarget = shortTarget;
            LongTarget = longTarget;
            Variants = [source, shortTarget, longTarget];
        }

        public string Root { get; }

        public AircraftVariantViewAnalysis Source { get; }

        public AircraftVariantViewAnalysis ShortTarget { get; }

        public AircraftVariantViewAnalysis LongTarget { get; }

        public IReadOnlyList<AircraftVariantViewAnalysis> Variants { get; }

        public static FleetFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), $"xplane-737ng-fleet-view-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);

            var source = CreateVariant(
                root,
                "levelup-737-700",
                "LevelUp 737-700",
                "737_70NG",
                new AircraftCg(-2.0, 50.0),
                BuildSourcePrefs("\n"),
                "\n",
                hasBom: false);
            var shortTarget = CreateVariant(
                root,
                "levelup-737-600",
                "LevelUp 737-600",
                "737_60NG",
                new AircraftCg(-2.0, 46.0),
                "target-short-setting keep\r\n_iql_pe_x_0 99.000000\r\n_iql_pe_y_0 99.000000\r\n_iql_pe_z_0 99.000000\r\n_iql_look_os_the_0 99.000000\r\n",
                "\r\n",
                hasBom: true);
            var longTarget = CreateVariant(
                root,
                "levelup-737-900er",
                "LevelUp 737-900ER",
                "737_9ENG",
                new AircraftCg(-1.0, 66.0),
                "target-long-setting keep\n",
                "\n",
                hasBom: false);

            return new FleetFixture(root, source, shortTarget, longTarget);
        }

        private static AircraftVariantViewAnalysis CreateVariant(
            string root,
            string aircraftId,
            string displayName,
            string stem,
            AircraftCg cg,
            string prefs,
            string lineEnding,
            bool hasBom)
        {
            var acfPath = Path.Combine(root, $"{stem}.acf");
            var prefsPath = Path.Combine(root, $"{stem}_prefs.txt");
            WriteText(acfPath, BuildAcf(cg, lineEnding), hasBom: false);
            WriteText(prefsPath, prefs, hasBom);
            return new AircraftVariantViewAnalysis(
                aircraftId,
                displayName,
                AircraftProductIds.LevelUp737Ng,
                acfPath,
                prefsPath,
                "test",
                "test",
                "test",
                "test",
                "test",
                "test",
                cg.YFeet,
                cg.ZFeet,
                cg.YFeet,
                cg.ZFeet,
                0,
                0,
                0,
                0,
                "Reference CG",
                "Expected metadata",
                "QV0 readable",
                "Default view differs from QV0");
        }

        private static string BuildSourcePrefs(string lineEnding) => string.Join(lineEnding, new[]
        {
            "source-setting do-not-copy",
            "_iql_view_type_0 v_3dc",
            "_iql_pe_x_0 1.000000",
            "_iql_pe_y_0 2.000000",
            "_iql_pe_z_0 3.000000",
            "_iql_look_os_psi_0 0.000000",
            "_iql_look_os_the_0 -2.500000",
            "_iql_zoom_rat_0 1.000000",
            "_iql_view_type_1 v_3dc",
            "_iql_pe_x_1 2.000000",
            "_iql_pe_y_1 3.000000",
            "_iql_pe_z_1 4.000000",
            "_iql_look_os_psi_1 12.000000",
            "_iql_look_os_the_1 -1.500000"
        }) + lineEnding;

        private static string BuildAcf(AircraftCg cg, string lineEnding) => string.Join(lineEnding, new[]
        {
            "1200 Version",
            FormattableString.Invariant($"P acf/_cgY {cg.YFeet:0.000000000}"),
            FormattableString.Invariant($"P acf/_cgZ {cg.ZFeet:0.000000000}"),
            "P acf/_pe_xyz/0 0.000000000",
            "P acf/_pe_xyz/1 0.000000000",
            "P acf/_pe_xyz/2 0.000000000",
            "P acf/_ang_offset/0,1 0.000000000"
        }) + lineEnding;

        private static void WriteText(string path, string text, bool hasBom)
        {
            File.WriteAllText(path, text, new UTF8Encoding(hasBom));
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
