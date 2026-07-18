using System.Text;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class ConfigBackupOperationTests
{
    [Fact]
    public void CreateBackup_BacksUpConfigFilesAndRecordsState()
    {
        using var fixture = ConfigBackupFixture.Create();
        var store = new ToolStateStore(Path.Combine(fixture.Path, ".tool-state"));
        var operation = new ConfigBackupOperation(store, isXPlaneRunning: () => false);

        var result = operation.CreateBackup(fixture.SingleVariant());

        Assert.True(result.Succeeded);
        Assert.True(result.Changed);
        Assert.Equal("Applied", result.Status);
        Assert.Equal(7, result.BackupPaths.Count);
        Assert.All(result.BackupPaths, path => Assert.True(File.Exists(path)));
        Assert.DoesNotContain(result.BackupPaths, path => path.EndsWith(".acf", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.BackupPaths, path => path.EndsWith("README.txt", StringComparison.OrdinalIgnoreCase));

        var target = Assert.Single(store.Load().Aircraft.Values);
        Assert.Equal("ConfigBackup", target.LastOperation);
        Assert.Equal(7, target.Backups.Count(record => record.Operation == "ConfigBackup"));
    }

    [Fact]
    public void RestoreLatestConfigBackup_RestoresOnlyLatestConfigGenerationAndCreatesPreImages()
    {
        using var fixture = ConfigBackupFixture.Create();
        var store = new ToolStateStore(Path.Combine(fixture.Path, ".tool-state"));
        var operation = new ConfigBackupOperation(store, isXPlaneRunning: () => false);
        var variant = fixture.SingleVariant();

        operation.CreateBackup(variant);
        File.WriteAllText(fixture.PrefsPath, "changed prefs\n", new UTF8Encoding(false));
        File.WriteAllText(fixture.ConfigPath, "changed config\n", new UTF8Encoding(false));
        File.AppendAllText(fixture.AcfPath, "P acf/_manual_test_change 1\n", new UTF8Encoding(false));

        var result = operation.RestoreLatestConfigBackup(fixture.SingleVariant());

        Assert.True(result.Succeeded);
        Assert.True(result.Changed);
        Assert.Equal("Restored", result.Status);
        Assert.Contains("_iql_pe_x_0 1.000000", File.ReadAllText(fixture.PrefsPath), StringComparison.Ordinal);
        Assert.Contains("original config", File.ReadAllText(fixture.ConfigPath), StringComparison.Ordinal);
        Assert.Contains("_manual_test_change", File.ReadAllText(fixture.AcfPath), StringComparison.Ordinal);

        var target = Assert.Single(store.Load().Aircraft.Values);
        Assert.Equal("RestoreLatestConfigBackup", target.LastOperation);
        Assert.Equal(7, target.Backups.Count(record => record.Operation == "ConfigBackup"));
        Assert.Equal(7, target.Backups.Count(record => record.Operation == "ConfigRestorePreImage"));
        Assert.All(target.Backups.Where(record => record.Operation == "ConfigRestorePreImage"), record => Assert.True(File.Exists(record.BackupPath)));
    }

    [Fact]
    public void CreateBackup_WhenXPlaneRuns_BlocksWithoutWritingState()
    {
        using var fixture = ConfigBackupFixture.Create();
        var store = new ToolStateStore(Path.Combine(fixture.Path, ".tool-state"));
        var operation = new ConfigBackupOperation(store, isXPlaneRunning: () => true);

        var result = operation.CreateBackup(fixture.SingleVariant());

        Assert.False(result.Succeeded);
        Assert.Equal("Blocked", result.Status);
        Assert.False(File.Exists(store.StatePath));
    }

    private sealed class ConfigBackupFixture : IDisposable
    {
        private ConfigBackupFixture(string path)
        {
            Path = path;
            AcfPath = System.IO.Path.Combine(path, "b738_4k.acf");
            PrefsPath = System.IO.Path.Combine(path, "b738_4k_prefs.txt");
            ConfigPath = System.IO.Path.Combine(path, "b738_config.txt");
        }

        public string Path { get; }

        public string AcfPath { get; }

        public string PrefsPath { get; }

        public string ConfigPath { get; }

        public static ConfigBackupFixture Create()
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"xplane-737ng-config-backup-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var fixture = new ConfigBackupFixture(root);
            WriteText(
                fixture.AcfPath,
                """
                1200 Version
                P acf/_descrip Boeing 737-800X
                P acf/_file_writer_version 124201
                P acf/_name Boeing 737-800X (4k)
                P acf/_studio Zibo
                P acf/_version ver 4.05
                P acf/_cgY -2.000000000
                P acf/_cgZ 60.340000153
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
            WriteText(System.IO.Path.Combine(root, "b738_4k_vrconfig.txt"), "vr config\n");
            WriteText(System.IO.Path.Combine(root, "X-Camera_b738_4k.csv"), "camera config\n");
            WriteText(fixture.ConfigPath, "original config\n");
            WriteText(System.IO.Path.Combine(root, "ND_overlays.cfg"), "overlay config\n");
            WriteText(System.IO.Path.Combine(root, AircraftMaintenanceMetadata.FileName), "{}\n");
            WriteText(System.IO.Path.Combine(root, "version.txt"), "4.05.35\n");
            WriteText(System.IO.Path.Combine(root, "README.txt"), "not a config backup target\n");
            return fixture;
        }

        public AircraftVariantViewAnalysis SingleVariant() =>
            Assert.Single(new AircraftViewAnalyzer().Analyze(Path).Variants);

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
