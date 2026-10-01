using System.Security.Cryptography;
using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.State;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.Tools;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class AircraftMoveOperationTests : IDisposable
{
    private readonly string _root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "mtk-move-" + Guid.NewGuid().ToString("N"));
    private readonly string? _destinationParent;
    internal string Source => Path.Combine(_root, "XPlane", "Aircraft", "LevelUp");
    internal string Destination => _destinationParent is null
        ? Path.Combine(_root, "XPlane", "Aircraft", "renamed")
        : Path.Combine(_destinationParent, Path.GetFileName(_root) + "-moved");
    internal ToolStateStore Store => new(Path.Combine(_root, "state"), Path.Combine(_root, "backups"));
    internal ToolkitSettingsStore Settings => new(Store.RootPath);
    private AircraftMoveOperation Operation(Action<string>? checkpoint = null, Func<bool>? running = null, Func<string, long>? free = null)
        => new(Store, Settings, running ?? (() => false), free ?? (_ => long.MaxValue), checkpoint);

    public AircraftMoveOperationTests() : this(null) { }

    internal AircraftMoveOperationTests(string? destinationParent)
    {
        _destinationParent = destinationParent;
        Directory.CreateDirectory(Path.Combine(_root, "XPlane", "Resources"));
        Write(Path.Combine(Source, "737_80NG.acf"), "1200 Version\n");
        Write(Path.Combine(Source, "plugins/xlua/scripts/B738.a_fms/B738.a_fms.lua"), "local custom script\r\n");
        Write(Path.Combine(Source, "liveries/private/objects/paint.dds"), "private livery bytes");
        Write(Path.Combine(Source, "b738_prefs.txt"), "my preferences");
        Directory.CreateDirectory(Path.Combine(Source, "empty"));
        Settings.Save(new() { SelectedAircraftPath = Source, BackupRootPath = Store.BackupRootPath,
            AircraftUpdateCacheRootPath = Path.Combine(_root, "cache"), OfflinePackageRootPath = Path.Combine(_root, "offline"),
            DiagnosticsExportRootPath = Path.Combine(_root, "reports"), CheckAircraftAndPatchUpdatesOnStartup = false,
            CheckToolkitUpdatesOnStartup = false });
    }

    [Fact]
    public void Move_PreservesEveryByteAndEmptyDirectory_AndSelectsNewFolderWithoutAdoption()
    {
        var op = Operation(); var plan = op.Prepare(Source, Destination);
        var result = op.Execute(plan);
        Assert.False(result.CleanupPending); Assert.False(op.HasPendingMove);
        Assert.False(Directory.Exists(Source));
        Assert.Equal(Destination, Settings.Load().SelectedAircraftPath);
        Assert.Empty(Store.Load().Aircraft); Assert.Empty(Store.Load().ContentInstallations);
        foreach (var file in plan.Entries.Where(f => !f.Directory)) Assert.Equal(file.Sha256, Hash(Path.Combine(Destination, file.RelativePath)));
        Assert.True(Directory.Exists(Path.Combine(Destination, "empty")));
        Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(Source)!, ".mtk-*"));
    }

    [Fact]
    public void Move_RebasesAllOwnershipAndHistory_WithoutChangingRecordedHashesOrExternalBackups()
    {
        SeedState();
        var op = Operation(); op.Execute(op.Prepare(Source, Destination));
        var state = Store.Load();
        Assert.True(state.Aircraft.ContainsKey(ToolStateStore.ProductTargetKey(AircraftProductIds.LevelUp737Ng, Destination)));
        Assert.True(state.Aircraft.ContainsKey(ToolStateStore.PathKey(Path.Combine(Destination, "737_80NG.acf"))));
        var installation = state.ContentInstallations[ToolStateStore.PathKey(Destination)];
        var file = installation.ContentComponents["patch"].Files.Single();
        Assert.Equal(Path.Combine(Destination, "b738_prefs.txt"), file.TargetPath);
        Assert.Equal("recorded-hash-must-not-be-rewritten", file.InstalledSha256);
        Assert.Equal(Path.Combine(_root, "backups", "original.txt"), file.BackupPath);
        Assert.Equal("original backup", File.ReadAllText(file.BackupPath));
        var history = state.Aircraft.Values.Single(a => a.AcfPath.Length == 0).Backups.Single();
        Assert.Equal(Destination, history.SourcePath);
        Assert.Equal(Path.Combine(Destination, "inside-backup.txt"), history.BackupPath);
        Assert.Equal(Path.Combine(Destination, "b738_prefs.txt"), history.AircraftContentGeneration!.ProductComponents["patch"].Files.Single().TargetPath);
        var tool = Store.TryGetToolInstallation(Destination, "xlua")!;
        Assert.Equal(Path.Combine(Destination, "plugins/xlua"), tool.TargetPath);
        Assert.Equal(Destination, tool.Dependencies.Single().InstallationRoot);
        Assert.Equal(Destination, tool.Backups.Single().PreviousDependencies.Single().InstallationRoot);
        Assert.NotNull(Store.TryGetLiveryInstallation(Destination, "my-livery"));
        Assert.Equal(Path.Combine(Destination, "liveries/private"), state.ResourceInstallations["resource"].TargetPath);
        Assert.Equal(Destination, state.ToolInstallations["global"].Dependencies.Single().InstallationRoot);
        Assert.DoesNotContain(Source, JsonSerializer.Serialize(state));

    }

    [Theory]
    [InlineData("Prepared")]
    [InlineData("SourceRetained")]
    [InlineData("Activated")]
    [InlineData("StateSaved")]
    [InlineData("SettingsSaved")]
    public void OrdinaryFailure_RollsBackFolderAndExactStateAndSettingsBytes(string phase)
    {
        SeedState(); var state = File.ReadAllBytes(Store.StatePath); var settings = File.ReadAllBytes(Settings.SettingsPath);
        var op = Operation(p => { if (p == phase) throw new IOException("injected fault"); });
        var plan = op.Prepare(Source, Destination);
        Assert.Throws<IOException>(() => op.Execute(plan));
        AssertOriginal(plan, state, settings); Assert.False(op.HasPendingMove);
    }

    [Theory]
    [InlineData("Prepared")]
    [InlineData("SourceRetained")]
    [InlineData("Activated")]
    [InlineData("StateSaved")]
    [InlineData("SettingsSaved")]
    [InlineData("Committed")]
    public void ProcessInterruption_IsRecoveredByANewOperationInstance(string phase)
    {
        SeedState(); var state = File.ReadAllBytes(Store.StatePath); var settings = File.ReadAllBytes(Settings.SettingsPath);
        var op = Operation(p => { if (p == phase) throw new AircraftMoveSimulatedCrashException(); });
        var plan = op.Prepare(Source, Destination);
        Assert.Throws<AircraftMoveSimulatedCrashException>(() => op.Execute(plan));
        Assert.True(op.HasPendingMove);
        var recoveredPath = Operation().Recover();
        Assert.False(op.HasPendingMove);
        if (phase == "Committed") { Assert.Equal(Destination, recoveredPath); Assert.False(Directory.Exists(Source)); }
        else { Assert.Equal(Source, recoveredPath); AssertOriginal(plan, state, settings); }
    }

    [Theory]
    [InlineData("Activated")]
    [InlineData("Committed")]
    public void Recovery_BlocksModifiedDestinationAndRetainsBothCopies(string phase)
    {
        var op = Operation(p => { if (p == phase) throw new AircraftMoveSimulatedCrashException(); });
        var plan = op.Prepare(Source, Destination);
        Assert.Throws<AircraftMoveSimulatedCrashException>(() => op.Execute(plan));
        Write(Path.Combine(Destination, "b738_prefs.txt"), "user change after interruption");
        Assert.Throws<IOException>(() => Operation().Recover());
        Assert.Equal("user change after interruption", File.ReadAllText(Path.Combine(Destination, "b738_prefs.txt")));
        Assert.True(op.HasPendingMove);
    }

    [Fact]
    public void Recovery_DoesNotDeleteModifiedRetainedSourceAfterCommit()
    {
        var op = Operation(p => { if (p == "Committed") throw new AircraftMoveSimulatedCrashException(); });
        Assert.Throws<AircraftMoveSimulatedCrashException>(() => op.Execute(op.Prepare(Source, Destination)));
        var hold = Directory.GetDirectories(Path.GetDirectoryName(Source)!, ".mtk-*-source").Single();
        Write(Path.Combine(hold, "b738_prefs.txt"), "new bytes in retained source");
        Assert.Throws<IOException>(() => Operation().Recover());
        Assert.Equal("new bytes in retained source", File.ReadAllText(Path.Combine(hold, "b738_prefs.txt")));
    }

    [Fact]
    public void Recovery_FinishesPartiallyDeletedRetainedSourceAfterCommit()
    {
        var op = Operation(p => { if (p == "Committed") throw new AircraftMoveSimulatedCrashException(); });
        Assert.Throws<AircraftMoveSimulatedCrashException>(() => op.Execute(op.Prepare(Source, Destination)));
        var hold = Directory.GetDirectories(Path.GetDirectoryName(Source)!, ".mtk-*-source").Single();
        File.Delete(Path.Combine(hold, "b738_prefs.txt"));
        Assert.Equal(Destination, Operation().Recover()); Assert.False(Directory.Exists(hold));
    }

    [Fact]
    public void StalePlanChangedFilesOrState_BlocksBeforeJournalAndCopy()
    {
        var op = Operation(); var plan = op.Prepare(Source, Destination);
        Write(Path.Combine(Source, "b738_prefs.txt"), "changed");
        Assert.Throws<InvalidOperationException>(() => op.Execute(plan));
        Assert.False(op.HasPendingMove); Assert.False(Directory.Exists(Destination));
        plan = op.Prepare(Source, Destination); Store.Save(new());
        Assert.Throws<InvalidOperationException>(() => op.Execute(plan));
    }

    [Fact]
    public void Preflight_BlocksOccupiedNestedLinkedAndLowSpaceDestinations_AndRunningXPlane()
    {
        Directory.CreateDirectory(Destination);
        Assert.Throws<InvalidOperationException>(() => Operation().Prepare(Source, Destination));
        Directory.Delete(Destination);
        Assert.Throws<InvalidOperationException>(() => Operation().Prepare(Source, Path.Combine(Source, "nested")));
        Assert.Throws<IOException>(() => Operation(free: _ => 0).Prepare(Source, Destination));
        Assert.Throws<InvalidOperationException>(() => Operation(running: () => true).Prepare(Source, Destination));
        File.CreateSymbolicLink(Path.Combine(Source, "linked"), Path.Combine(Source, "b738_prefs.txt"));
        Assert.Throws<InvalidOperationException>(() => Operation().Prepare(Source, Destination));
        Assert.False(File.Exists(Operation().JournalPath));
    }

    [Fact]
    public void Cancellation_DuringCopyRollsBackAndLeavesNoPendingJournal()
    {
        using var cancel = new CancellationTokenSource();
        var op = Operation(); var plan = op.Prepare(Source, Destination);
        Assert.Throws<OperationCanceledException>(() => op.Execute(plan, new ImmediateProgress(_ => cancel.Cancel()), cancel.Token));
        Assert.True(Directory.Exists(Source)); Assert.False(Directory.Exists(Destination)); Assert.False(op.HasPendingMove);
    }

    [Fact]
    public void Journal_BlocksConcurrentRecoveryAndStateSettingsWrites()
    {
        var op = Operation(p =>
        {
            if (p != "Prepared") return;
            Assert.Throws<IOException>(() => Operation().Recover());
            Assert.Throws<IOException>(() => Store.Save(new()));
            Assert.Throws<IOException>(() => Settings.Save(new()));
        });
        op.Execute(op.Prepare(Source, Destination));
    }

    [Fact]
    public void CrossInstallationMove_WithGlobalDependency_BlocksWithoutChanges()
    {
        SeedState();
        Store.UpdateToolInstallation(Source, "xlua", t => t.Dependencies.Add(new() { PackageId = "YAL", InstallationRoot = Path.Combine(_root, "XPlane") }));
        var other = Path.Combine(_root, "OtherXPlane"); Directory.CreateDirectory(Path.Combine(other, "Aircraft")); Directory.CreateDirectory(Path.Combine(other, "Resources"));
        Assert.Throws<InvalidOperationException>(() => Operation().Prepare(Source, Path.Combine(other, "Aircraft", "LU")));
        Assert.True(Directory.Exists(Source));
    }

    [Fact]
    public void OrphanedDestinationOwnership_BlocksEvenWithoutADestinationFolder()
    {
        Store.UpdateContentInstallation(Destination, _ => { });
        Assert.Throws<InvalidOperationException>(() => Operation().Prepare(Source, Destination));
    }

    [Fact]
    public void UnixPermissionsAndExecutableRuntime_ArePreserved()
    {
        if (OperatingSystem.IsWindows()) return;
        var path = Path.Combine(Source, "plugins/runtime.xpl"); Write(path, "runtime");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var op = Operation(); op.Execute(op.Prepare(Source, Destination));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(Path.Combine(Destination, "plugins/runtime.xpl")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CrossInstallationMove_WithInboundCurrentOrHistoricalDependency_Blocks(bool historical)
    {
        SeedState();
        if (historical)
        {
            var state = Store.Load();
            var global = state.ToolInstallations["global"];
            global.Backups.Add(new() { PreviousDependencies = global.Dependencies });
            global.Dependencies = [];
            Store.Save(state);
        }
        var other = OtherXPlane();
        Assert.Throws<InvalidOperationException>(() => Operation().Prepare(Source, Path.Combine(other, "Aircraft", "LU")));
        Assert.False(Operation().HasPendingMove); Assert.True(Directory.Exists(Source));
    }

    [Fact]
    public void CrossInstallationMove_WithoutExternalDependencies_PreservesScripts()
    {
        var destination = Path.Combine(OtherXPlane(), "Aircraft", "LU");
        var op = Operation(); var plan = op.Prepare(Source, destination); op.Execute(plan);
        foreach (var file in plan.Entries.Where(f => !f.Directory))
            Assert.Equal(file.Sha256, Hash(Path.Combine(destination, file.RelativePath)));
        Assert.Equal(destination, Settings.Load().SelectedAircraftPath);
    }

    [Fact]
    public void ConfigRestore_AfterMove_UsesOriginalBackupsAndNewAircraftPaths()
    {
        var reference = AircraftReferenceCatalog.All.Single(r => r.AcfFileName == "737_80NG.acf");
        Write(Path.Combine(Source, reference.AcfFileName), $"1200 Version\nP acf/_name {reference.ExpectedName}\nP acf/_descrip {reference.ExpectedDescription}\nP acf/_studio {reference.ExpectedStudioContains}\nP acf/_cgY 0\nP acf/_cgZ 0\n");
        var variant = Assert.Single(new AircraftViewAnalyzer().Analyze(Source).Variants);
        var original = File.ReadAllBytes(Path.Combine(Source, "b738_prefs.txt"));
        var configs = new ConfigBackupOperation(Store, () => false);
        Assert.True(configs.CreateBackup(variant).Succeeded);
        Write(Path.Combine(Source, "b738_prefs.txt"), "new preferences");
        var op = Operation(); op.Execute(op.Prepare(Source, Destination));
        var movedVariant = Assert.Single(new AircraftViewAnalyzer().Analyze(Destination).Variants);
        Assert.True(configs.RestoreLatestConfigBackup(movedVariant).Succeeded);
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(Destination, "b738_prefs.txt")));
        Assert.False(Directory.Exists(Source));
        Assert.All(Store.TryGetTarget(movedVariant)!.Backups, b => Assert.StartsWith(Destination, b.SourcePath));
    }

    [Fact]
    public void ToolUpdateAndRestore_AfterMove_ReturnsPreviousRuntimeAndPreservesScripts()
    {
        var catalog = new ContentPackageCatalogEntry
        {
            PackageId = "wahltho.optimized-xlua", DisplayName = "Optimized XLua",
            Category = ContentPackageCategory.AircraftComponent, Activation = ContentPatchActivation.ExplicitOptIn,
            SupportedProducts = [AircraftProductIds.LevelUp737Ng], InstallScope = "aircraftInstallation",
            TargetPath = "plugins/xlua", RepositoryUrl = "https://github.com/wahltho/XLua",
            SupportedChannels = ["stable", "beta"],
            Distribution = new() { ManifestSchemaVersion = 1 }
        };
        ToolPackageProvisionResult Package(string version, string runtime)
        {
            var root = Path.Combine(_root, "package-" + version);
            Write(Path.Combine(root, "init.lua"), runtime + " init");
            Write(Path.Combine(root, "mac_x64/xlua.xpl"), runtime);
            var manifest = new ToolPackageManifest
            {
                SchemaVersion = 1, PackageId = catalog.PackageId, PackageVersion = version,
                Channel = "stable", ReleaseTag = "v" + version, Repository = catalog.RepositoryUrl,
                InstallScope = catalog.InstallScope, TargetPath = catalog.TargetPath, Layout = "directory",
                SupportedProducts = catalog.SupportedProducts, ProtectedPaths = ["scripts/**"],
                Archive = new() { FileName = version + ".zip", RootPath = "xlua", Size = 1, Sha256 = new string('a', 64) },
                Files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(path => new ToolPackageFile
                { Path = Path.GetRelativePath(root, path).Replace('\\', '/'), Size = new FileInfo(path).Length, Sha256 = Hash(path) }).ToList()
            };
            return new(new(ToolReleaseChannel.Stable, manifest.ReleaseTag, "https://example.test/release", "manifest.json",
                "https://example.test/manifest.json", 1, new string('b', 64), "https://example.test/package.zip", manifest), root, false);
        }
        var manager = new ToolPackageManager(Store, () => false, "osx-arm64");
        var first = Package("1.0.0", "runtime 1"); var second = Package("2.0.0", "runtime 2");
        var initial = manager.Apply(catalog, first, Source, ToolPackageAction.Update);
        Assert.True(initial.Succeeded, initial.Message);
        Assert.True(manager.Apply(catalog, second, Source, ToolPackageAction.Update).Succeeded);
        var script = "plugins/xlua/scripts/B738.a_fms/B738.a_fms.lua";
        var original = File.ReadAllBytes(Path.Combine(Source, script));
        var op = Operation(); op.Execute(op.Prepare(Source, Destination));
        Assert.Equal(ToolPackageInstallState.Current, manager.Inspect(catalog, Destination, second.Release).State);
        Assert.True(manager.Restore(catalog, Destination).Succeeded);
        Assert.Equal("runtime 1", File.ReadAllText(Path.Combine(Destination, "plugins/xlua/mac_x64/xlua.xpl")));
        Assert.Equal("runtime 1 init", File.ReadAllText(Path.Combine(Destination, "plugins/xlua/init.lua")));
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(Destination, script)));
        Assert.False(Directory.Exists(Source));
        Assert.Equal("1.0.0", Store.TryGetToolInstallation(Destination, catalog.PackageId)!.InstalledVersion);
        Assert.True(manager.Apply(catalog, second, Destination, ToolPackageAction.Update).Succeeded);
        Assert.Equal(ToolPackageInstallState.Current, manager.Inspect(catalog, Destination, second.Release).State);
    }

    [Fact]
    public void Recovery_RejectsUnrelatedStateChangesWithoutOverwritingThem()
    {
        var op = Operation(p => { if (p == "Activated") throw new AircraftMoveSimulatedCrashException(); });
        Assert.Throws<AircraftMoveSimulatedCrashException>(() => op.Execute(op.Prepare(Source, Destination)));
        File.WriteAllText(Store.StatePath, "unrelated state change");
        Assert.Throws<IOException>(() => Operation().Recover());
        Assert.Equal("unrelated state change", File.ReadAllText(Store.StatePath));
        Assert.True(Directory.Exists(Destination)); Assert.True(op.HasPendingMove);
    }

    [Fact]
    public void ConcurrentDestinationCreation_IsNeverDeletedWithoutOurOwnershipMarker()
    {
        var op = Operation(p =>
        {
            if (p != "SourceRetained") return;
            Write(Path.Combine(Destination, "my-file.txt"), "do not delete");
        });
        Assert.Throws<IOException>(() => op.Execute(op.Prepare(Source, Destination)));
        Assert.Equal("do not delete", File.ReadAllText(Path.Combine(Destination, "my-file.txt")));
        Assert.True(op.HasPendingMove);
    }

    [Fact]
    public void SourceChangedDuringCopy_IsRetainedAndMoveRollsBack()
    {
        var op = Operation(); var plan = op.Prepare(Source, Destination);
        var changed = false;
        Assert.Throws<IOException>(() => op.Execute(plan, new ImmediateProgress(_ =>
        {
            if (changed) return;
            changed = true; Write(Path.Combine(Source, "b738_prefs.txt"), "changed during copy");
        })));
        Assert.Equal("changed during copy", File.ReadAllText(Path.Combine(Source, "b738_prefs.txt")));
        Assert.False(op.HasPendingMove); Assert.False(Directory.Exists(Destination));
    }

    [Fact]
    public async Task PatchRestore_AfterMove_UsesOriginalFileAndOwnershipHistory()
    {
        var reference = AircraftReferenceCatalog.All.Single(r => r.AcfFileName == "737_80NG.acf");
        Write(Path.Combine(Source, reference.AcfFileName), $"1200 Version\nP acf/_name {reference.ExpectedName}\nP acf/_descrip {reference.ExpectedDescription}\nP acf/_studio {reference.ExpectedStudioContains}\nP acf/_cgY 0\nP acf/_cgZ 0\n");
        var variant = Assert.Single(new AircraftViewAnalyzer().Analyze(Source).Variants);
        const string relative = "plugins/xlua/scripts/B738.tablet/B738.tablet.lua";
        var target = Path.Combine(Source, relative); Write(target, "before\r\n");
        var original = File.ReadAllBytes(target);
        var package = Path.Combine(_root, "patch-package");
        var payload = System.Text.Encoding.UTF8.GetBytes("""
            {"format":"exact-text-replacements-v1","replacements":[{"name":"fix","oldLines":["before"],"newLines":["after"]}]}
            """);
        Write(Path.Combine(package, "patches/change.json"), System.Text.Encoding.UTF8.GetString(payload));
        Write(Path.Combine(package, "package-manifest.json"), DeclarativePatchManifestTests.BuildManifest(
            "patches/change.json", payload, relative, DeclarativePatchManifestTests.Sha256(original),
            DeclarativePatchManifestTests.Sha256(System.Text.Encoding.UTF8.GetBytes("after\r\n"))));
        var patches = new DeclarativeContentPatchOperation(Store, () => false);
        var installed = await patches.RunAsync(ContentPatchAction.Install, variant, package);
        Assert.True(installed.Succeeded, installed.Message);
        var op = Operation(); op.Execute(op.Prepare(Source, Destination));
        var movedVariant = Assert.Single(new AircraftViewAnalyzer().Analyze(Destination).Variants);
        var restored = patches.Restore(movedVariant, package);
        Assert.True(restored.Succeeded, restored.Message);
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(Destination, relative)));
        Assert.False(Directory.Exists(Source));
    }

    private string OtherXPlane()
    {
        var other = Path.Combine(_root, "OtherXPlane");
        Directory.CreateDirectory(Path.Combine(other, "Aircraft")); Directory.CreateDirectory(Path.Combine(other, "Resources"));
        return other;
    }

    [Fact]
    public void UnixReadOnlyAircraftRoot_IsRejectedBeforeCopyOrJournal()
    {
        if (OperatingSystem.IsWindows()) return;
        var original = File.GetUnixFileMode(Source);
        try
        {
            File.SetUnixFileMode(Source, original & ~UnixFileMode.UserWrite);
            var exception = Assert.Throws<InvalidOperationException>(() => Operation().Prepare(Source, Destination));
            Assert.Contains("owner", exception.Message);
            Assert.False(Operation().HasPendingMove); Assert.False(Directory.Exists(Destination));
            Assert.True(File.Exists(Path.Combine(Source, "b738_prefs.txt")));
        }
        finally { File.SetUnixFileMode(Source, original); }
    }

    private void SeedState()
    {
        Write(Path.Combine(_root, "backups", "original.txt"), "original backup");
        Write(Path.Combine(Source, "inside-backup.txt"), "inside backup");
        var component = new ContentComponentState { ComponentId = "patch", Files = [new()
        {
            RelativePath = "b738_prefs.txt", TargetPath = Path.Combine(Source, "b738_prefs.txt"),
            BackupPath = Path.Combine(_root, "backups", "original.txt"), OriginalExisted = true,
            InstalledSha256 = "recorded-hash-must-not-be-rewritten"
        }] };
        var state = new ToolStateDocument();
        state.ContentInstallations[ToolStateStore.PathKey(Source)] = new() { AircraftFolder = Source, ContentComponents = new() { ["patch"] = component } };
        state.Aircraft[ToolStateStore.ProductTargetKey(AircraftProductIds.LevelUp737Ng, Source)] = new()
        { AircraftId = AircraftProductIds.LevelUp737Ng, AircraftFolder = Source, Backups = [new()
        { SourcePath = Source, BackupPath = Path.Combine(Source, "inside-backup.txt"), AircraftContentGeneration = new() { ProductComponents = new() { ["patch"] = component } } }] };
        state.Aircraft[ToolStateStore.PathKey(Path.Combine(Source, "737_80NG.acf"))] = new()
        { AircraftId = "LevelUp-737-800", AircraftFolder = Source, AcfPath = Path.Combine(Source, "737_80NG.acf"), PrefsPath = Path.Combine(Source, "b738_prefs.txt") };
        state.ToolInstallations[ToolStateStore.ToolKey(Source, "xlua")] = new()
        { PackageId = "xlua", XPlaneRoot = Source, TargetPath = Path.Combine(Source, "plugins/xlua"), Dependencies = [new() { InstallationRoot = Source }],
            Backups = [new() { BackupPath = Path.Combine(_root, "backups", "xlua"), PreviousDependencies = [new() { InstallationRoot = Source }] }] };
        state.ToolInstallations["global"] = new() { XPlaneRoot = Path.Combine(_root, "XPlane"), Dependencies = [new() { InstallationRoot = Source }] };
        state.LiveryInstallations[ToolStateStore.LiveryKey(Source, "my-livery")] = new()
        { PackageId = "my-livery", DestinationDirectory = Path.Combine(Source, "liveries"), TargetPath = Path.Combine(Source, "liveries/private") };
        state.ResourceInstallations["resource"] = new() { TargetPath = Path.Combine(Source, "liveries/private") };
        Store.Save(state);
    }
    private void AssertOriginal(AircraftMovePlan plan, byte[] state, byte[] settings)
    {
        Assert.True(Directory.Exists(Source)); Assert.False(Directory.Exists(Destination));
        foreach (var f in plan.Entries.Where(f => !f.Directory)) Assert.Equal(f.Sha256, Hash(Path.Combine(Source, f.RelativePath)));
        Assert.Equal(state, File.ReadAllBytes(Store.StatePath)); Assert.Equal(settings, File.ReadAllBytes(Settings.SettingsPath));
    }
    private sealed class ImmediateProgress(Action<AircraftMoveProgress> action) : IProgress<AircraftMoveProgress>
    { public void Report(AircraftMoveProgress value) => action(value); }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Write(string path, string value) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, value); }
    public void Dispose()
    {
        void RemoveTestTree(string path)
        {
            if (!Directory.Exists(path)) return;
            if (OperatingSystem.IsWindows())
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
            Directory.Delete(path, true);
        }
        if (_destinationParent is not null) RemoveTestTree(Destination);
        RemoveTestTree(_root);
    }
}
