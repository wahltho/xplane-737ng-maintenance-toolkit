using System.Text;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class ApplyDefaultViewFromQv0OperationTests
{
    [Fact]
    public void Apply_WhenDefaultViewDiffers_UpdatesAcfCreatesBackupAndRecordsState()
    {
        using var fixture = DefaultViewOperationFixture.Create(defaultViewMatchesQv0: false, lineEnding: "\r\n");
        var variant = fixture.SingleVariant();
        var store = new ToolStateStore(Path.Combine(fixture.Path, ".tool-state"));
        var operation = new ApplyDefaultViewFromQv0Operation(store, isXPlaneRunning: () => false);

        var result = operation.Apply(variant);

        Assert.True(result.Succeeded);
        Assert.True(result.Changed);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
        Assert.Contains("P acf/_pe_xyz/0 0.000000000\r\n", File.ReadAllText(result.BackupPath));

        var rewritten = File.ReadAllText(Path.Combine(fixture.Path, "737_70NG.acf"));
        Assert.Contains("\r\n", rewritten);
        Assert.DoesNotContain("\n", rewritten.Replace("\r\n", "", StringComparison.Ordinal));

        var metadata = AircraftFileParser.ReadAcfMetadata(Path.Combine(fixture.Path, "737_70NG.acf"));
        var expected = AircraftFileParser.CalculateDefaultViewFromQuickView(metadata.Cg!, AircraftFileParser.ReadQuickView0(Path.Combine(fixture.Path, "737_70NG_prefs.txt"))!);
        Assert.Equal(expected.XFeet, metadata.DefaultView!.XFeet, precision: 6);
        Assert.Equal(expected.YFeet, metadata.DefaultView.YFeet, precision: 6);
        Assert.Equal(expected.ZFeet, metadata.DefaultView.ZFeet, precision: 6);
        Assert.Equal(expected.PitchDegrees, metadata.DefaultView.PitchDegrees, precision: 6);

        var state = store.Load();
        var target = Assert.Single(state.Aircraft.Values);
        Assert.Equal("levelup-737-700", target.AircraftId);
        Assert.Equal("ApplyQv0ToDefaultView", target.LastOperation);
        Assert.Single(target.Backups);
    }

    [Fact]
    public void Apply_WhenDefaultViewAlreadyMatches_DoesNotCreateBackupButRecordsState()
    {
        using var fixture = DefaultViewOperationFixture.Create(defaultViewMatchesQv0: true, lineEnding: "\n");
        var store = new ToolStateStore(Path.Combine(fixture.Path, ".tool-state"));
        var operation = new ApplyDefaultViewFromQv0Operation(store, isXPlaneRunning: () => false);

        var result = operation.Apply(fixture.SingleVariant());

        Assert.True(result.Succeeded);
        Assert.False(result.Changed);
        Assert.Null(result.BackupPath);
        Assert.False(Directory.Exists(Path.Combine(fixture.Path, ".tool-state", "backups")));

        var target = Assert.Single(store.Load().Aircraft.Values);
        Assert.Equal("ApplyQv0ToDefaultViewNoChange", target.LastOperation);
        Assert.Empty(target.Backups);
    }

    [Fact]
    public void Apply_WhenXPlaneRuns_BlocksWithoutWritingState()
    {
        using var fixture = DefaultViewOperationFixture.Create(defaultViewMatchesQv0: false, lineEnding: "\n");
        var store = new ToolStateStore(Path.Combine(fixture.Path, ".tool-state"));
        var operation = new ApplyDefaultViewFromQv0Operation(store, isXPlaneRunning: () => true);

        var result = operation.Apply(fixture.SingleVariant());

        Assert.False(result.Succeeded);
        Assert.Equal("Blocked", result.Status);
        Assert.False(File.Exists(store.StatePath));
        Assert.Contains("P acf/_pe_xyz/0 0.000000000\n", File.ReadAllText(Path.Combine(fixture.Path, "737_70NG.acf")));
    }

    [Fact]
    public void Apply_WhenDefaultViewKeyIsDuplicated_ThrowsBeforeBackup()
    {
        using var fixture = DefaultViewOperationFixture.Create(defaultViewMatchesQv0: false, lineEnding: "\n", duplicateDefaultKey: true);
        var store = new ToolStateStore(Path.Combine(fixture.Path, ".tool-state"));
        var operation = new ApplyDefaultViewFromQv0Operation(store, isXPlaneRunning: () => false);

        var ex = Assert.Throws<InvalidOperationException>(() => operation.Apply(fixture.SingleVariant()));

        Assert.Contains("must be unique", ex.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(fixture.Path, ".tool-state", "backups")));
    }

    private sealed class DefaultViewOperationFixture : IDisposable
    {
        private DefaultViewOperationFixture(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static DefaultViewOperationFixture Create(bool defaultViewMatchesQv0, string lineEnding, bool duplicateDefaultKey = false)
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"xplane-737ng-default-view-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var qv0 = new QuickView0(XMeters: 1.0, YMeters: 2.0, ZMeters: -3.0, PitchDegrees: -2.5);
            var cg = new AircraftCg(YFeet: -2.049999952, ZFeet: 49.740001678);
            var defaultView = defaultViewMatchesQv0
                ? AircraftFileParser.CalculateDefaultViewFromQuickView(cg, qv0)
                : new DefaultView(0.0, 0.0, 0.0, 0.0);

            WriteText(
                System.IO.Path.Combine(root, "737_70NG.acf"),
                BuildAcf(cg, defaultView, duplicateDefaultKey, lineEnding),
                lineEnding);
            WriteText(
                System.IO.Path.Combine(root, "737_70NG_prefs.txt"),
                BuildPrefs(qv0, lineEnding),
                lineEnding);

            return new DefaultViewOperationFixture(root);
        }

        public AircraftVariantViewAnalysis SingleVariant()
        {
            return Assert.Single(new AircraftViewAnalyzer().Analyze(Path).Variants);
        }

        private static string BuildAcf(AircraftCg cg, DefaultView defaultView, bool duplicateDefaultKey, string lineEnding)
        {
            var lines = new List<string>
            {
                "1200 Version",
                "P acf/_descrip Boeing 737-700NG",
                "P acf/_file_writer_version 124311",
                "P acf/_name Boeing 737-700NG",
                "P acf/_studio LevelUp, Laminar Research, ZiboMod, flight tuned by Aeroguitarist",
                "P acf/_version XP12 2.S1.50B (20260709-2031 SAO)",
                FormattableString.Invariant($"P acf/_cgY {cg.YFeet:0.000000000}"),
                FormattableString.Invariant($"P acf/_cgZ {cg.ZFeet:0.000000000}"),
                FormattableString.Invariant($"P acf/_pe_xyz/0 {defaultView.XFeet:0.000000000}"),
                FormattableString.Invariant($"P acf/_pe_xyz/1 {defaultView.YFeet:0.000000000}"),
                FormattableString.Invariant($"P acf/_pe_xyz/2 {defaultView.ZFeet:0.000000000}"),
                FormattableString.Invariant($"P acf/_ang_offset/0,1 {defaultView.PitchDegrees:0.000000000}")
            };
            if (duplicateDefaultKey)
            {
                lines.Add("P acf/_pe_xyz/0 1.000000000");
            }

            return string.Join(lineEnding, lines) + lineEnding;
        }

        private static string BuildPrefs(QuickView0 qv0, string lineEnding)
        {
            var lines = new[]
            {
                FormattableString.Invariant($"_iql_pe_x_0 {qv0.XMeters:0.000000}"),
                FormattableString.Invariant($"_iql_pe_y_0 {qv0.YMeters:0.000000}"),
                FormattableString.Invariant($"_iql_pe_z_0 {qv0.ZMeters:0.000000}"),
                FormattableString.Invariant($"_iql_look_os_the_0 {qv0.PitchDegrees:0.000000}")
            };

            return string.Join(lineEnding, lines) + lineEnding;
        }

        private static void WriteText(string path, string text, string lineEnding)
        {
            var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", lineEnding, StringComparison.Ordinal);
            File.WriteAllText(path, normalized, new UTF8Encoding(false));
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
