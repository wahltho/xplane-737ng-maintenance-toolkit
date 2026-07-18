using System.Text;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class RestoreLatestBackupOperationTests
{
    [Fact]
    public void Restore_WhenLatestGenerationExists_RestoresFilesAndCreatesPreRestoreBackups()
    {
        using var fixture = RestoreFixture.Create();
        var variant = fixture.SingleVariant();
        var store = TestToolStateStore.Create(fixture.Path);
        var adapt = new ApplyQuickViewCgAdaptOperation(store, isXPlaneRunning: () => false);
        var restore = new RestoreLatestBackupOperation(store, isXPlaneRunning: () => false);

        adapt.Apply(variant);
        File.AppendAllText(fixture.PrefsPath, "_manual_test_change 1\n", new UTF8Encoding(false));
        File.AppendAllText(fixture.XCameraPath, "Manual,0,9.000000,9.000000,0.000000,0.000000\n", new UTF8Encoding(false));

        var result = restore.Restore(fixture.SingleVariant());

        Assert.True(result.Succeeded);
        Assert.True(result.Changed);
        Assert.Equal("Restored", result.Status);
        Assert.DoesNotContain("_manual_test_change", File.ReadAllText(fixture.PrefsPath), StringComparison.Ordinal);
        Assert.DoesNotContain("Manual,0,9.000000", File.ReadAllText(fixture.XCameraPath), StringComparison.Ordinal);

        var target = Assert.Single(store.Load().Aircraft.Values);
        Assert.Equal("RestoreLatestBackup", target.LastOperation);
        Assert.Equal(4, target.Backups.Count);
        Assert.Equal(2, target.Backups.Count(record => record.Operation == "RestorePreImage"));
        Assert.All(target.Backups.Where(record => record.Operation == "RestorePreImage"), record => Assert.True(File.Exists(record.BackupPath)));
    }

    [Fact]
    public void Restore_WhenNoBackupExists_BlocksWithoutWritingState()
    {
        using var fixture = RestoreFixture.Create();
        var store = TestToolStateStore.Create(fixture.Path);
        var restore = new RestoreLatestBackupOperation(store, isXPlaneRunning: () => false);

        var result = restore.Restore(fixture.SingleVariant());

        Assert.False(result.Succeeded);
        Assert.Equal("Blocked", result.Status);
        Assert.False(File.Exists(store.StatePath));
    }

    private sealed class RestoreFixture : IDisposable
    {
        private RestoreFixture(string path)
        {
            Path = path;
            PrefsPath = System.IO.Path.Combine(path, "737_70NG_prefs.txt");
            XCameraPath = System.IO.Path.Combine(path, "X-Camera_737_70NG.csv");
        }

        public string Path { get; }

        public string PrefsPath { get; }

        public string XCameraPath { get; }

        public static RestoreFixture Create()
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"xplane-737ng-restore-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var fixture = new RestoreFixture(root);
            WriteText(
                System.IO.Path.Combine(root, "737_70NG.acf"),
                """
                1200 Version
                P acf/_descrip Boeing 737-700NG
                P acf/_file_writer_version 124311
                P acf/_name Boeing 737-700NG
                P acf/_studio LevelUp, Laminar Research, ZiboMod, flight tuned by Aeroguitarist
                P acf/_version XP12 2.S1.50B (20260709-2031 SAO)
                P acf/_cgY -2.049999952
                P acf/_cgZ 49.840001678
                P acf/_pe_xyz/0 1.000000000
                P acf/_pe_xyz/1 2.000000000
                P acf/_pe_xyz/2 3.000000000
                P acf/_ang_offset/0,1 -2.500000000
                """);
            WriteText(
                fixture.PrefsPath,
                """
                _iql_pe_x_0 1.000000
                _iql_pe_y_0 2.000000
                _iql_pe_z_0 3.000000
                _iql_look_os_the_0 -2.500000
                """);
            WriteText(
                fixture.XCameraPath,
                """
                Category Name,Camera Origin,Y,Z,CGY Offset,CGZ Offset
                Cockpit,0,1.000000,2.000000,0.000000,-0.030480
                """);
            return fixture;
        }

        public AircraftVariantViewAnalysis SingleVariant()
        {
            return Assert.Single(new AircraftViewAnalyzer().Analyze(Path).Variants);
        }

        private static void WriteText(string path, string text)
        {
            File.WriteAllText(path, text.Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));
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
