using System.Text;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class ApplyQuickViewCgAdaptOperationTests
{
    [Fact]
    public void Apply_WhenCgDiffers_AdjustsPrefsAndXCameraCreatesBackupsAndRecordsState()
    {
        using var fixture = QuickViewCgFixture.Create(cgZ: 49.840001678, lineEnding: "\r\n", includeXCamera: true);
        var variant = fixture.SingleVariant();
        var store = new ToolStateStore(Path.Combine(fixture.Path, ".tool-state"));
        var operation = new ApplyQuickViewCgAdaptOperation(store, isXPlaneRunning: () => false);

        var result = operation.Apply(variant);

        Assert.True(result.Succeeded);
        Assert.True(result.Changed);
        Assert.Equal(2, result.BackupPaths.Count);
        Assert.All(result.BackupPaths, path => Assert.True(File.Exists(path)));

        var prefs = File.ReadAllText(fixture.PrefsPath);
        Assert.Contains("\r\n", prefs);
        Assert.Contains("_iql_pe_z_0 2.969520\r\n", prefs);
        Assert.Contains("_iql_pe_z_1 3.969520\r\n", prefs);

        var xcamera = File.ReadAllText(fixture.XCameraPath);
        Assert.Contains("Cockpit,0,1.000000,1.969520,0.000000,0.000000", xcamera);
        Assert.Contains("External,1,10.000000,20.000000,0.000000,0.000000", xcamera);

        var state = store.Load();
        var target = Assert.Single(state.Aircraft.Values);
        Assert.Equal("ApplyQuickViewCgAdapt", target.LastOperation);
        Assert.Equal(49.840001678, target.LastQuickViewCgZFeet);
        Assert.Equal(2, target.Backups.Count);
    }

    [Fact]
    public void Apply_WhenRunTwice_UsesStoredCgBaselineAndDoesNotShiftAgain()
    {
        using var fixture = QuickViewCgFixture.Create(cgZ: 49.840001678, lineEnding: "\n", includeXCamera: false);
        var store = new ToolStateStore(Path.Combine(fixture.Path, ".tool-state"));
        var operation = new ApplyQuickViewCgAdaptOperation(store, isXPlaneRunning: () => false);

        var first = operation.Apply(fixture.SingleVariant());
        var prefsAfterFirstRun = File.ReadAllText(fixture.PrefsPath);
        var second = operation.Apply(fixture.SingleVariant());
        var prefsAfterSecondRun = File.ReadAllText(fixture.PrefsPath);

        Assert.True(first.Changed);
        Assert.True(second.Succeeded);
        Assert.False(second.Changed);
        Assert.Equal(prefsAfterFirstRun, prefsAfterSecondRun);
    }

    [Fact]
    public void Apply_WhenYAndZCgDiffer_AdjustsBothMeterAxesFromFeetDelta()
    {
        using var fixture = QuickViewCgFixture.Create(
            cgY: -1.949999952,
            cgZ: 49.840001678,
            lineEnding: "\n",
            includeXCamera: false);
        var store = new ToolStateStore(Path.Combine(fixture.Path, ".tool-state"));
        var operation = new ApplyQuickViewCgAdaptOperation(store, isXPlaneRunning: () => false);

        var result = operation.Apply(fixture.SingleVariant());

        Assert.True(result.Succeeded);
        Assert.True(result.Changed);
        var prefs = File.ReadAllText(fixture.PrefsPath);
        Assert.Contains("_iql_pe_y_0 1.969520\n", prefs);
        Assert.Contains("_iql_pe_z_0 2.969520\n", prefs);
    }

    [Fact]
    public void Apply_WhenXPlaneRuns_BlocksWithoutWritingState()
    {
        using var fixture = QuickViewCgFixture.Create(cgZ: 49.840001678, lineEnding: "\n", includeXCamera: false);
        var store = new ToolStateStore(Path.Combine(fixture.Path, ".tool-state"));
        var operation = new ApplyQuickViewCgAdaptOperation(store, isXPlaneRunning: () => true);

        var result = operation.Apply(fixture.SingleVariant());

        Assert.False(result.Succeeded);
        Assert.Equal("Blocked", result.Status);
        Assert.False(File.Exists(store.StatePath));
        Assert.Contains("_iql_pe_z_0 3.000000\n", File.ReadAllText(fixture.PrefsPath));
    }

    [Fact]
    public void Apply_WhenIdentityDiffersAndNoStoredBaselineAndCgDiffers_BlocksWithoutWritingState()
    {
        using var fixture = QuickViewCgFixture.Create(cgZ: 49.840001678, lineEnding: "\n", includeXCamera: false);
        var variant = fixture.SingleVariant() with
        {
            IdentityStatus = "Metadata differs (version.txt differs)"
        };
        var store = new ToolStateStore(Path.Combine(fixture.Path, ".tool-state"));
        var operation = new ApplyQuickViewCgAdaptOperation(store, isXPlaneRunning: () => false);

        var result = operation.Apply(variant);

        Assert.False(result.Succeeded);
        Assert.False(result.Changed);
        Assert.Equal("Blocked", result.Status);
        Assert.Empty(result.BackupPaths);
        Assert.False(File.Exists(store.StatePath));
        Assert.Contains("no stored quick-view CG baseline", result.Message);
        Assert.Contains("_iql_pe_z_0 3.000000\n", File.ReadAllText(fixture.PrefsPath));
    }

    [Fact]
    public void Apply_WhenIdentityDiffersAndNoCgDelta_RecordsBaselineWithoutChange()
    {
        using var fixture = QuickViewCgFixture.Create(cgZ: 49.740001678, lineEnding: "\n", includeXCamera: false);
        var variant = fixture.SingleVariant() with
        {
            IdentityStatus = "Metadata differs (version.txt differs)"
        };
        var store = new ToolStateStore(Path.Combine(fixture.Path, ".tool-state"));
        var operation = new ApplyQuickViewCgAdaptOperation(store, isXPlaneRunning: () => false);

        var result = operation.Apply(variant);

        Assert.True(result.Succeeded);
        Assert.False(result.Changed);
        Assert.Equal("No change", result.Status);
        Assert.Empty(result.BackupPaths);
        var target = Assert.Single(store.Load().Aircraft.Values);
        Assert.Equal("ApplyQuickViewCgAdaptNoChange", target.LastOperation);
        Assert.Equal(49.740001678, target.LastQuickViewCgZFeet);
        Assert.Contains("_iql_pe_z_0 3.000000\n", File.ReadAllText(fixture.PrefsPath));
    }

    private sealed class QuickViewCgFixture : IDisposable
    {
        private QuickViewCgFixture(string path)
        {
            Path = path;
            PrefsPath = System.IO.Path.Combine(path, "737_70NG_prefs.txt");
            XCameraPath = System.IO.Path.Combine(path, "X-Camera_737_70NG.csv");
        }

        public string Path { get; }

        public string PrefsPath { get; }

        public string XCameraPath { get; }

        public static QuickViewCgFixture Create(double cgZ, string lineEnding, bool includeXCamera) =>
            Create(cgY: -2.049999952, cgZ, lineEnding, includeXCamera);

        public static QuickViewCgFixture Create(double cgY, double cgZ, string lineEnding, bool includeXCamera)
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"xplane-737ng-qv-cg-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var fixture = new QuickViewCgFixture(root);
            WriteText(
                System.IO.Path.Combine(root, "737_70NG.acf"),
                BuildAcf(cgY, cgZ, lineEnding),
                lineEnding);
            WriteText(
                fixture.PrefsPath,
                BuildPrefs(lineEnding),
                lineEnding);

            if (includeXCamera)
            {
                WriteText(
                    fixture.XCameraPath,
                    BuildXCamera(lineEnding),
                    lineEnding);
            }

            return fixture;
        }

        public AircraftVariantViewAnalysis SingleVariant()
        {
            return Assert.Single(new AircraftViewAnalyzer().Analyze(Path).Variants);
        }

        private static string BuildAcf(double cgY, double cgZ, string lineEnding)
        {
            var lines = new[]
            {
                "1200 Version",
                "P acf/_descrip Boeing 737-700NG",
                "P acf/_file_writer_version 124311",
                "P acf/_name Boeing 737-700NG",
                "P acf/_studio LevelUp, Laminar Research, ZiboMod, flight tuned by Aeroguitarist",
                "P acf/_version XP12 2.S1.50B (20260709-2031 SAO)",
                FormattableString.Invariant($"P acf/_cgY {cgY:0.000000000}"),
                FormattableString.Invariant($"P acf/_cgZ {cgZ:0.000000000}"),
                "P acf/_pe_xyz/0 1.000000000",
                "P acf/_pe_xyz/1 2.000000000",
                "P acf/_pe_xyz/2 3.000000000",
                "P acf/_ang_offset/0,1 -2.500000000"
            };

            return string.Join(lineEnding, lines) + lineEnding;
        }

        private static string BuildPrefs(string lineEnding)
        {
            var lines = new[]
            {
                "_iql_pe_x_0 1.000000",
                "_iql_pe_y_0 2.000000",
                "_iql_pe_z_0 3.000000",
                "_iql_look_os_the_0 -2.500000",
                "_iql_pe_x_1 2.000000",
                "_iql_pe_y_1 3.000000",
                "_iql_pe_z_1 4.000000",
                "_iql_look_os_the_1 -1.500000"
            };

            return string.Join(lineEnding, lines) + lineEnding;
        }

        private static string BuildXCamera(string lineEnding)
        {
            var lines = new[]
            {
                "Category Name,Camera Origin,Y,Z,CGY Offset,CGZ Offset",
                "Cockpit,0,1.000000,2.000000,0.000000,-0.030480",
                "External,1,10.000000,20.000000,0.000000,0.000000"
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
