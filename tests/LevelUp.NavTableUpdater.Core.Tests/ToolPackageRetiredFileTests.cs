using System.Security.Cryptography;
using System.Text;
using LevelUp.NavTableUpdater.App.ViewModels;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.State;
using LevelUp.NavTableUpdater.Core.Tools;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class ToolPackageRetiredFileTests
{
    [Fact]
    public void MigrationConfirmation_ListsRemovedInstalledAndProtectedXluaPaths()
    {
        using var fixture = new Fixture();
        var release = fixture.CreateXlua2Package().Release;
        var details = MainWindowViewModel.BuildToolMigrationDetails(
        [
            new ToolPackagePlannedAction(
                fixture.Catalog,
                release,
                fixture.AircraftRoot,
                ToolPackageAction.Update,
                [])
        ]);

        Assert.Contains("mac_x64/xlua.xpl", details, StringComparison.Ordinal);
        Assert.Contains("mac_x64/xlua2.xpl", details, StringComparison.Ordinal);
        Assert.Contains("init.lua", details, StringComparison.Ordinal);
        Assert.Contains("scripts/**", details, StringComparison.Ordinal);
    }

    [Fact]
    public void SamePathMigrationConfirmation_DistinguishesReplacementsAndRemovals()
    {
        using var fixture = new Fixture();
        var release = fixture.CreateSamePathXlua2Package().Release;

        var details = MainWindowViewModel.BuildToolMigrationDetails(
        [
            new ToolPackagePlannedAction(
                fixture.Catalog,
                release,
                fixture.AircraftRoot,
                ToolPackageAction.Update,
                [])
        ]);

        Assert.Contains("Replace legacy files at the same paths: mac_x64/xlua.xpl", details, StringComparison.Ordinal);
        Assert.Contains("Remove obsolete files: mac_x64/xlua2.xpl", details, StringComparison.Ordinal);
        Assert.Contains("Preserve protected aircraft files: scripts/**", details, StringComparison.Ordinal);
    }

    [Fact]
    public void FreshInstall_SamePathPackageInstallsRuntimeAndPreservesAircraftScripts()
    {
        using var fixture = new Fixture();

        var result = fixture.Manager.Apply(
            fixture.Catalog,
            fixture.CreateSamePathXlua2Package(),
            fixture.AircraftRoot,
            ToolPackageAction.Update);

        Assert.True(result.Succeeded, result.Message + Environment.NewLine + string.Join(Environment.NewLine, result.Log));
        fixture.AssertSamePathXlua2Installed();
        Assert.Equal(fixture.ScriptBytes, fixture.ReadTargetBytes("scripts/B738.test/main.lua"));
    }

    [Fact]
    public void ManagedXlua1_SamePathUpdateBacksUpReplacesAndRestoresExactly()
    {
        using var fixture = new Fixture();
        Assert.True(fixture.Manager.Apply(
            fixture.Catalog,
            fixture.CreateXlua1Package(),
            fixture.AircraftRoot,
            ToolPackageAction.Update).Succeeded);
        var before = fixture.CaptureTarget();
        var opt4 = fixture.CreateSamePathXlua2Package();

        var update = fixture.Manager.Apply(fixture.Catalog, opt4, fixture.AircraftRoot, ToolPackageAction.Update);

        Assert.True(update.Succeeded, update.Message + Environment.NewLine + string.Join(Environment.NewLine, update.Log));
        Assert.Contains(update.Log, line => line.Contains("[REPLACE]", StringComparison.Ordinal));
        fixture.AssertSamePathXlua2Installed();
        Assert.Equal(ToolPackageInstallState.Current, fixture.Manager.Inspect(fixture.Catalog, fixture.AircraftRoot, opt4.Release).State);
        Assert.Equal([.. Fixture.Opt3RuntimePaths], fixture.StateStore
            .TryGetToolInstallation(fixture.AircraftRoot, fixture.Catalog.PackageId)!.RetiredFiles);

        var restore = fixture.Manager.Restore(fixture.Catalog, fixture.AircraftRoot);

        Assert.True(restore.Succeeded, restore.Message + Environment.NewLine + string.Join(Environment.NewLine, restore.Log));
        Assert.Equal(before, fixture.CaptureTarget());
    }

    [Fact]
    public void UnmanagedHashKnownXlua1_SamePathUpdateSucceeds()
    {
        using var fixture = new Fixture();
        fixture.WriteUnmanagedXlua1();

        var result = fixture.Manager.Apply(
            fixture.Catalog,
            fixture.CreateSamePathXlua2Package(),
            fixture.AircraftRoot,
            ToolPackageAction.Update);

        Assert.True(result.Succeeded, result.Message + Environment.NewLine + string.Join(Environment.NewLine, result.Log));
        fixture.AssertSamePathXlua2Installed();
        Assert.Equal(fixture.ScriptBytes, fixture.ReadTargetBytes("scripts/B738.test/main.lua"));
    }

    [Fact]
    public void ManagedOpt3_SamePathUpdateRemovesOldNamesAndRestoresOpt3Exactly()
    {
        using var fixture = new Fixture();
        var opt3 = fixture.CreateXlua2Package();
        Assert.True(fixture.Manager.Apply(fixture.Catalog, opt3, fixture.AircraftRoot, ToolPackageAction.Update).Succeeded);
        var before = fixture.CaptureTarget();

        var result = fixture.Manager.Apply(
            fixture.Catalog,
            fixture.CreateSamePathXlua2Package(),
            fixture.AircraftRoot,
            ToolPackageAction.Update);

        Assert.True(result.Succeeded, result.Message + Environment.NewLine + string.Join(Environment.NewLine, result.Log));
        fixture.AssertSamePathXlua2Installed();
        foreach (var path in Fixture.Opt3RuntimePaths)
            Assert.False(File.Exists(fixture.Target(path)));

        var restore = fixture.Manager.Restore(fixture.Catalog, fixture.AircraftRoot);

        Assert.True(restore.Succeeded, restore.Message + Environment.NewLine + string.Join(Environment.NewLine, restore.Log));
        Assert.Equal(before, fixture.CaptureTarget());
    }

    [Fact]
    public void UnmanagedHashKnownOpt3_SamePathUpdateRemovesOldNames()
    {
        using var fixture = new Fixture();
        fixture.WriteUnmanagedOpt3();

        var result = fixture.Manager.Apply(
            fixture.Catalog,
            fixture.CreateSamePathXlua2Package(),
            fixture.AircraftRoot,
            ToolPackageAction.Update);

        Assert.True(result.Succeeded, result.Message + Environment.NewLine + string.Join(Environment.NewLine, result.Log));
        fixture.AssertSamePathXlua2Installed();
        Assert.Equal(fixture.ScriptBytes, fixture.ReadTargetBytes("scripts/B738.test/main.lua"));
    }

    [Fact]
    public void ReappearedKnownXlua1AtReplacementPath_RequiresRepairAndIsReplacedAgain()
    {
        using var fixture = new Fixture();
        var opt4 = fixture.CreateSamePathXlua2Package();
        Assert.True(fixture.Manager.Apply(fixture.Catalog, opt4, fixture.AircraftRoot, ToolPackageAction.Update).Succeeded);
        fixture.WriteTarget("mac_x64/xlua.xpl", fixture.OldFiles["mac_x64/xlua.xpl"]);

        var inspection = fixture.Manager.Inspect(fixture.Catalog, fixture.AircraftRoot, opt4.Release);
        var repair = fixture.Manager.Apply(fixture.Catalog, opt4, fixture.AircraftRoot, ToolPackageAction.Repair);

        Assert.Equal(ToolPackageInstallState.RepairRequired, inspection.State);
        Assert.True(repair.Succeeded, repair.Message + Environment.NewLine + string.Join(Environment.NewLine, repair.Log));
        fixture.AssertSamePathXlua2Installed();
        Assert.NotEmpty(repair.BackupPaths);
    }

    [Fact]
    public void ManagedSamePathRuntime_CanUpdateToNewerGenerationThroughOwnershipProof()
    {
        using var fixture = new Fixture();
        var opt4 = fixture.CreateSamePathXlua2Package();
        Assert.True(fixture.Manager.Apply(fixture.Catalog, opt4, fixture.AircraftRoot, ToolPackageAction.Update).Succeeded);
        var opt5 = fixture.CreateSamePathXlua2Package("2.0.0b1-opt5", "xlua2-opt5:");

        var result = fixture.Manager.Apply(fixture.Catalog, opt5, fixture.AircraftRoot, ToolPackageAction.Update);

        Assert.True(result.Succeeded, result.Message + Environment.NewLine + string.Join(Environment.NewLine, result.Log));
        fixture.AssertSamePathXlua2Installed("xlua2-opt5:");
    }

    [Fact]
    public void UnknownSamePathRuntime_BlocksBeforeBackup()
    {
        using var fixture = new Fixture();
        fixture.WriteTarget("win_x64/xlua.xpl", Encoding.UTF8.GetBytes("locally modified"));
        var before = fixture.CaptureTarget();

        var result = fixture.Manager.Apply(
            fixture.Catalog,
            fixture.CreateSamePathXlua2Package(),
            fixture.AircraftRoot,
            ToolPackageAction.Update);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Log, line => line.Contains("unknown or locally modified", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(before, fixture.CaptureTarget());
        Assert.False(Directory.Exists(fixture.BackupRoot));
    }

    [Theory]
    [InlineData((int)ToolPackageTransactionPhase.BackupCreated)]
    [InlineData((int)ToolPackageTransactionPhase.StageValidated)]
    [InlineData((int)ToolPackageTransactionPhase.TargetActivated)]
    public void SamePathMigrationFailure_RestoresCompletePreviousDirectoryAndState(int failurePhaseValue)
    {
        var failurePhase = (ToolPackageTransactionPhase)failurePhaseValue;
        using var fixture = new Fixture();
        Assert.True(fixture.Manager.Apply(
            fixture.Catalog,
            fixture.CreateXlua1Package(),
            fixture.AircraftRoot,
            ToolPackageAction.Update).Succeeded);
        var before = fixture.CaptureTarget();
        var manager = new ToolPackageManager(
            fixture.StateStore,
            () => false,
            "osx-arm64",
            phase =>
            {
                if (phase == failurePhase)
                    throw new IOException($"Injected failure after {phase}.");
            });

        Assert.Throws<IOException>(() => manager.Apply(
            fixture.Catalog,
            fixture.CreateSamePathXlua2Package(),
            fixture.AircraftRoot,
            ToolPackageAction.Update));

        Assert.Equal(before, fixture.CaptureTarget());
        var state = fixture.StateStore.TryGetToolInstallation(fixture.AircraftRoot, fixture.Catalog.PackageId)!;
        Assert.Equal("1.3.7r5", state.InstalledVersion);
        Assert.Empty(state.RetiredFiles);
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(fixture.AircraftRoot, "plugins"), ".xlua.*"));
        Assert.Empty(Directory.EnumerateDirectories(fixture.AircraftRoot, ".xlua.*"));
    }

    [Fact]
    public void XluaUpdate_StagesAndRollsBackOutsidePlugins()
    {
        using var fixture = new Fixture();
        Assert.True(fixture.Manager.Apply(
            fixture.Catalog, fixture.CreateXlua1Package(), fixture.AircraftRoot, ToolPackageAction.Update).Succeeded);
        var stagesObserved = false;
        var rollbackObserved = false;
        var manager = new ToolPackageManager(fixture.StateStore, () => false, "osx-arm64", phase =>
        {
            Assert.Empty(Directory.EnumerateDirectories(Path.Combine(fixture.AircraftRoot, "plugins"), ".xlua.*"));
            if (phase == ToolPackageTransactionPhase.StageValidated)
                stagesObserved = Directory.EnumerateDirectories(fixture.AircraftRoot, ".xlua.stage-*").Any();
            if (phase == ToolPackageTransactionPhase.TargetActivated)
                rollbackObserved = Directory.EnumerateDirectories(fixture.AircraftRoot, ".xlua.rollback-*").Any();
        });

        var update = manager.Apply(
            fixture.Catalog, fixture.CreateSamePathXlua2Package(), fixture.AircraftRoot, ToolPackageAction.Update);
        var restore = manager.Restore(fixture.Catalog, fixture.AircraftRoot);

        Assert.True(update.Succeeded, update.Message);
        Assert.True(restore.Succeeded, restore.Message);
        Assert.True(stagesObserved);
        Assert.True(rollbackObserved);
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(fixture.AircraftRoot, "plugins"), ".xlua.*"));
        Assert.Empty(Directory.EnumerateDirectories(fixture.AircraftRoot, ".xlua.*"));
    }

    [Fact]
    public void ManagedXlua1_UpdateToXlua2_RetiresOldRuntimesPreservesScriptsAndRestoresExactly()
    {
        using var fixture = new Fixture();
        var stable = fixture.CreateXlua1Package();
        Assert.True(fixture.Manager.Apply(fixture.Catalog, stable, fixture.AircraftRoot, ToolPackageAction.Update).Succeeded);
        fixture.WriteTarget("scripts/B738.test/main.lua", fixture.ScriptBytes);
        var scriptMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(fixture.Target("scripts/B738.test/main.lua"), scriptMode);
        }
        var before = fixture.CaptureTarget();
        var beta = fixture.CreateXlua2Package();
        foreach (var retiredFile in beta.Release.Manifest.RetiredFiles)
            retiredFile.SourceSha256.Clear();

        var updated = fixture.Manager.Apply(fixture.Catalog, beta, fixture.AircraftRoot, ToolPackageAction.Update);

        Assert.True(updated.Succeeded);
        Assert.Contains(updated.Log, line => line.Contains("[RETIRE]", StringComparison.Ordinal));
        Assert.Contains(updated.Log, line => line.Contains("xlua2.xpl", StringComparison.Ordinal));
        Assert.Contains(updated.Log, line => line.Contains("scripts/**", StringComparison.Ordinal));
        fixture.AssertXlua2Installed();
        Assert.Equal(fixture.ScriptBytes, fixture.ReadTargetBytes("scripts/B738.test/main.lua"));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(scriptMode, File.GetUnixFileMode(fixture.Target("scripts/B738.test/main.lua")));
        Assert.False(Directory.Exists(Path.Combine(fixture.AircraftRoot, "plugins", "xlua2")));
        Assert.Equal(ToolPackageInstallState.Current, fixture.Manager.Inspect(fixture.Catalog, fixture.AircraftRoot, beta.Release).State);
        Assert.Equal([.. Fixture.OldRuntimePaths], fixture.StateStore
            .TryGetToolInstallation(fixture.AircraftRoot, fixture.Catalog.PackageId)!.RetiredFiles);

        var restored = fixture.Manager.Restore(fixture.Catalog, fixture.AircraftRoot);

        Assert.True(restored.Succeeded, restored.Message + Environment.NewLine + string.Join(Environment.NewLine, restored.Log));
        Assert.Equal(before, fixture.CaptureTarget());
        if (!OperatingSystem.IsWindows())
            Assert.Equal(scriptMode, File.GetUnixFileMode(fixture.Target("scripts/B738.test/main.lua")));
        Assert.Empty(fixture.StateStore
            .TryGetToolInstallation(fixture.AircraftRoot, fixture.Catalog.PackageId)!.RetiredFiles);
    }

    [Fact]
    public void UnmanagedHashKnownXlua1_UpdateSucceedsWithMissingOptionalPlatformBinary()
    {
        using var fixture = new Fixture();
        fixture.WriteUnmanagedXlua1(includeLinux: false);
        var beta = fixture.CreateXlua2Package();

        var result = fixture.Manager.Apply(fixture.Catalog, beta, fixture.AircraftRoot, ToolPackageAction.Update);

        Assert.True(result.Succeeded);
        fixture.AssertXlua2Installed();
        Assert.Equal(fixture.ScriptBytes, fixture.ReadTargetBytes("scripts/B738.test/main.lua"));
    }

    [Fact]
    public void MissingRequiredRetiredRuntime_BlocksMigration()
    {
        using var fixture = new Fixture();
        fixture.WriteUnmanagedXlua1();
        File.Delete(fixture.Target("mac_x64/xlua.xpl"));
        var beta = fixture.CreateXlua2Package();
        beta.Release.Manifest.RetiredFiles.Single(file => file.Path == "mac_x64/xlua.xpl").Optional = false;

        var result = fixture.Manager.Apply(
            fixture.Catalog,
            beta,
            fixture.AircraftRoot,
            ToolPackageAction.Update);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Log, line => line.Contains("Required retired file is missing", StringComparison.Ordinal));
        Assert.False(Directory.Exists(fixture.BackupRoot));
    }

    [Fact]
    public void UnknownOrModifiedRetiredRuntime_BlocksWithoutChangingTargetOrCreatingBackup()
    {
        using var fixture = new Fixture();
        fixture.WriteUnmanagedXlua1();
        fixture.WriteTarget("win_x64/xlua.xpl", Encoding.UTF8.GetBytes("locally modified"));
        var before = fixture.CaptureTarget();

        var result = fixture.Manager.Apply(
            fixture.Catalog,
            fixture.CreateXlua2Package(),
            fixture.AircraftRoot,
            ToolPackageAction.Update);

        Assert.False(result.Succeeded);
        Assert.Contains("cannot be safely removed", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.Log, line => line.Contains("unknown or locally modified", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(before, fixture.CaptureTarget());
        Assert.False(Directory.Exists(fixture.BackupRoot));
    }

    [Fact]
    public void ReappearedKnownRetiredRuntime_RequiresRepairAndRepairRemovesItWithBackup()
    {
        using var fixture = new Fixture();
        var stable = fixture.CreateXlua1Package();
        Assert.True(fixture.Manager.Apply(fixture.Catalog, stable, fixture.AircraftRoot, ToolPackageAction.Update).Succeeded);
        var beta = fixture.CreateXlua2Package();
        var betaUpdate = fixture.Manager.Apply(fixture.Catalog, beta, fixture.AircraftRoot, ToolPackageAction.Update);
        Assert.True(betaUpdate.Succeeded, betaUpdate.Message + Environment.NewLine + string.Join(Environment.NewLine, betaUpdate.Log));
        fixture.WriteTarget("mac_x64/xlua.xpl", fixture.OldFiles["mac_x64/xlua.xpl"]);

        var inspection = fixture.Manager.Inspect(fixture.Catalog, fixture.AircraftRoot, beta.Release);
        var offlineInspection = fixture.Manager.Inspect(fixture.Catalog, fixture.AircraftRoot, null);
        var repair = fixture.Manager.Apply(fixture.Catalog, beta, fixture.AircraftRoot, ToolPackageAction.Repair);

        Assert.Equal(ToolPackageInstallState.RepairRequired, inspection.State);
        Assert.Equal(ToolPackageInstallState.RepairRequired, offlineInspection.State);
        Assert.Contains(inspection.Findings, finding => finding.Contains("Retired file", StringComparison.Ordinal));
        Assert.True(repair.Succeeded);
        Assert.NotEmpty(repair.BackupPaths);
        Assert.False(File.Exists(fixture.Target("mac_x64/xlua.xpl")));
    }

    [Fact]
    public void ReappearedUnknownRetiredRuntime_BlocksRepairAndRestore()
    {
        using var fixture = new Fixture();
        Assert.True(fixture.Manager.Apply(
            fixture.Catalog,
            fixture.CreateXlua1Package(),
            fixture.AircraftRoot,
            ToolPackageAction.Update).Succeeded);
        var beta = fixture.CreateXlua2Package();
        var betaUpdate = fixture.Manager.Apply(fixture.Catalog, beta, fixture.AircraftRoot, ToolPackageAction.Update);
        Assert.True(betaUpdate.Succeeded, betaUpdate.Message + Environment.NewLine + string.Join(Environment.NewLine, betaUpdate.Log));
        fixture.WriteTarget("mac_x64/xlua.xpl", Encoding.UTF8.GetBytes("unknown old runtime"));

        var repair = fixture.Manager.Apply(fixture.Catalog, beta, fixture.AircraftRoot, ToolPackageAction.Repair);
        var restore = fixture.Manager.Restore(fixture.Catalog, fixture.AircraftRoot);

        Assert.False(repair.Succeeded);
        Assert.False(restore.Succeeded);
        Assert.Contains("Retired file appeared", restore.Log.Single(line => line.Contains("[BLOCKED]", StringComparison.Ordinal)));
        Assert.True(File.Exists(fixture.Target("mac_x64/xlua.xpl")));
        Assert.True(File.Exists(fixture.Target("mac_x64/xlua2.xpl")));
    }

    [Theory]
    [InlineData((int)ToolPackageTransactionPhase.BackupCreated)]
    [InlineData((int)ToolPackageTransactionPhase.StageValidated)]
    [InlineData((int)ToolPackageTransactionPhase.TargetActivated)]
    public void MigrationFailure_RestoresCompletePreviousDirectoryAndState(int failurePhaseValue)
    {
        var failurePhase = (ToolPackageTransactionPhase)failurePhaseValue;
        using var fixture = new Fixture();
        Assert.True(fixture.Manager.Apply(
            fixture.Catalog,
            fixture.CreateXlua1Package(),
            fixture.AircraftRoot,
            ToolPackageAction.Update).Succeeded);
        fixture.WriteTarget("scripts/B738.test/main.lua", fixture.ScriptBytes);
        var before = fixture.CaptureTarget();
        var manager = new ToolPackageManager(
            fixture.StateStore,
            () => false,
            "osx-arm64",
            phase =>
            {
                if (phase == failurePhase)
                    throw new IOException($"Injected failure after {phase}.");
            });

        Assert.Throws<IOException>(() => manager.Apply(
            fixture.Catalog,
            fixture.CreateXlua2Package(),
            fixture.AircraftRoot,
            ToolPackageAction.Update));

        Assert.Equal(before, fixture.CaptureTarget());
        var state = fixture.StateStore.TryGetToolInstallation(fixture.AircraftRoot, fixture.Catalog.PackageId)!;
        Assert.Equal("1.3.7r5", state.InstalledVersion);
        Assert.Empty(state.RetiredFiles);
    }

    private sealed class Fixture : IDisposable
    {
        public static readonly string[] OldRuntimePaths =
        [
            "mac_x64/xlua.xpl",
            "win_x64/xlua.xpl",
            "lin_x64/xlua.xpl"
        ];

        public static readonly string[] Opt3RuntimePaths =
        [
            "mac_x64/xlua2.xpl",
            "win_x64/xlua2.xpl",
            "lin_x64/xlua2.xpl"
        ];

        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"xlua-retirement-tests-{Guid.NewGuid():N}");
            AircraftRoot = Path.Combine(Root, "Aircraft", "B737-800X");
            Directory.CreateDirectory(Path.Combine(AircraftRoot, "plugins", "xlua", "scripts"));
            File.WriteAllText(Path.Combine(AircraftRoot, "b738.acf"), "acf", new UTF8Encoding(false));
            BackupRoot = Path.Combine(Root, "backups");
            StateStore = new ToolStateStore(Path.Combine(Root, "state"), BackupRoot);
            Manager = new ToolPackageManager(StateStore, () => false, "osx-arm64");
            OldFiles = OldRuntimePaths.ToDictionary(
                path => path,
                path => Encoding.UTF8.GetBytes("xlua1:" + path),
                StringComparer.Ordinal);
            NewFiles = Opt3RuntimePaths.ToDictionary(
                path => path,
                path => Encoding.UTF8.GetBytes("xlua2:" + path),
                StringComparer.Ordinal);
            WriteTarget("scripts/B738.test/main.lua", ScriptBytes);
        }

        public string Root { get; }
        public string AircraftRoot { get; }
        public string BackupRoot { get; }
        public ToolStateStore StateStore { get; }
        public ToolPackageManager Manager { get; }
        public Dictionary<string, byte[]> OldFiles { get; }
        public Dictionary<string, byte[]> NewFiles { get; }
        public byte[] ScriptBytes { get; } = [0, 1, 2, 13, 10, 255, 42];

        public ContentPackageCatalogEntry Catalog { get; } = new()
        {
            PackageId = "wahltho.optimized-xlua",
            DisplayName = "Optimized XLua",
            Description = "Test component",
            Category = ContentPackageCategory.AircraftComponent,
            Activation = ContentPatchActivation.ExplicitOptIn,
            SupportedProducts = ["zibo-737ng", "levelup-737ng"],
            RepositoryUrl = "https://github.com/wahltho/XLua",
            RestartRequired = true,
            InstallScope = "aircraftInstallation",
            TargetPath = "plugins/xlua",
            SupportedChannels = ["stable", "beta"],
            Distribution = new ContentPackageDistribution
            {
                Kind = ContentPackageDistributionKind.GitHubToolRelease,
                ManifestAssetNamePattern = "Xlua.*-manifest.json",
                ManifestSchemaVersions = new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["stable"] = 1,
                    ["beta"] = 3
                }
            }
        };

        public ToolPackageProvisionResult CreateXlua1Package()
        {
            var files = OldFiles.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            files["init.lua"] = Encoding.UTF8.GetBytes("xlua1 init");
            return CreatePackage("1.3.7r5", "stable", 1, files, []);
        }

        public ToolPackageProvisionResult CreateXlua2Package()
        {
            var files = NewFiles.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            files["init.lua"] = Encoding.UTF8.GetBytes("xlua2 init");
            var retired = OldFiles.Select(pair => new ToolPackageRetiredFile
            {
                Path = pair.Key,
                Optional = true,
                SourceSha256 = [Hash(pair.Value)]
            }).ToList();
            return CreatePackage("2.0.0b1-opt1", "beta", 3, files, retired);
        }

        public ToolPackageProvisionResult CreateSamePathXlua2Package(
            string version = "2.0.0b1-opt4",
            string contentPrefix = "xlua2-opt4:")
        {
            var files = OldRuntimePaths.ToDictionary(
                path => path,
                path => Encoding.UTF8.GetBytes(contentPrefix + path),
                StringComparer.Ordinal);
            files["init.lua"] = Encoding.UTF8.GetBytes(contentPrefix + "init");
            var retired = OldFiles.Select(pair => new ToolPackageRetiredFile
            {
                Path = pair.Key,
                Optional = true,
                SourceSha256 = [Hash(pair.Value)]
            }).Concat(NewFiles.Select(pair => new ToolPackageRetiredFile
            {
                Path = pair.Key,
                Optional = true,
                SourceSha256 = [Hash(pair.Value)]
            })).ToList();
            return CreatePackage(version, "beta", 3, files, retired);
        }

        public void WriteUnmanagedXlua1(bool includeLinux = true)
        {
            foreach (var file in OldFiles.Where(pair => includeLinux || pair.Key != "lin_x64/xlua.xpl"))
                WriteTarget(file.Key, file.Value);
            WriteTarget("init.lua", Encoding.UTF8.GetBytes("unmanaged xlua1 init"));
            WriteTarget("scripts/B738.test/main.lua", ScriptBytes);
        }

        public void WriteUnmanagedOpt3()
        {
            foreach (var file in NewFiles)
                WriteTarget(file.Key, file.Value);
            WriteTarget("init.lua", Encoding.UTF8.GetBytes("unmanaged opt3 init"));
            WriteTarget("scripts/B738.test/main.lua", ScriptBytes);
        }

        public void AssertXlua2Installed()
        {
            foreach (var path in OldRuntimePaths)
                Assert.False(File.Exists(Target(path)));
            foreach (var pair in NewFiles)
                Assert.Equal(pair.Value, ReadTargetBytes(pair.Key));
            Assert.Equal("xlua2 init", Encoding.UTF8.GetString(ReadTargetBytes("init.lua")));
        }

        public void AssertSamePathXlua2Installed(string contentPrefix = "xlua2-opt4:")
        {
            foreach (var path in OldRuntimePaths)
                Assert.Equal(contentPrefix + path, Encoding.UTF8.GetString(ReadTargetBytes(path)));
            foreach (var path in Opt3RuntimePaths)
                Assert.False(File.Exists(Target(path)));
            Assert.Equal(contentPrefix + "init", Encoding.UTF8.GetString(ReadTargetBytes("init.lua")));
        }

        public string Target(string relativePath) =>
            Path.Combine(AircraftRoot, "plugins", "xlua", relativePath.Replace('/', Path.DirectorySeparatorChar));

        public void WriteTarget(string relativePath, byte[] bytes)
        {
            var path = Target(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }

        public byte[] ReadTargetBytes(string relativePath) => File.ReadAllBytes(Target(relativePath));

        public Dictionary<string, string> CaptureTarget() => Directory
            .EnumerateFiles(Target("."), "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(Target("."), path).Replace('\\', '/'),
                path => Hash(File.ReadAllBytes(path)),
                StringComparer.Ordinal);

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }

        private ToolPackageProvisionResult CreatePackage(
            string version,
            string channel,
            int schemaVersion,
            IReadOnlyDictionary<string, byte[]> files,
            List<ToolPackageRetiredFile> retiredFiles)
        {
            var packageRoot = Path.Combine(Root, "packages", version, Guid.NewGuid().ToString("N"));
            foreach (var file in files)
            {
                var path = Path.Combine(packageRoot, file.Key.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, file.Value);
            }

            var tag = channel == "stable" ? "r" + version : "v" + version;
            var manifest = new ToolPackageManifest
            {
                SchemaVersion = schemaVersion,
                PackageId = Catalog.PackageId,
                PackageVersion = version,
                ReleaseTag = tag,
                Channel = channel,
                Repository = Catalog.RepositoryUrl,
                InstallScope = Catalog.InstallScope,
                Layout = "directory",
                TargetPath = Catalog.TargetPath,
                SupportedProducts = [.. Catalog.SupportedProducts],
                RestartRequired = true,
                Archive = new ToolPackageArchive
                {
                    FileName = $"Xlua.{version}.zip",
                    RootPath = "xlua",
                    Size = 1,
                    Sha256 = new string('a', 64)
                },
                ProtectedPaths = ["scripts/**"],
                RetiredFiles = retiredFiles,
                Files = files.Select(file => new ToolPackageFile
                {
                    Path = file.Key,
                    Size = file.Value.LongLength,
                    Sha256 = Hash(file.Value)
                }).ToList()
            };
            var release = new ToolPackageRelease(
                channel == "stable" ? ToolReleaseChannel.Stable : ToolReleaseChannel.Beta,
                tag,
                $"{Catalog.RepositoryUrl}/releases/tag/{tag}",
                $"Xlua.{version}-manifest.json",
                $"{Catalog.RepositoryUrl}/releases/download/{tag}/Xlua.{version}-manifest.json",
                1,
                new string('b', 64),
                $"{Catalog.RepositoryUrl}/releases/download/{tag}/{manifest.Archive.FileName}",
                manifest);
            return new ToolPackageProvisionResult(release, packageRoot, Downloaded: false);
        }

        private static string Hash(byte[] bytes) =>
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
