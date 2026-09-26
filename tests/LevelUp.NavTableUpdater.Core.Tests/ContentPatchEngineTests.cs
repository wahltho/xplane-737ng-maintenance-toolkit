using System.Text;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.Manifest;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class ContentPatchEngineTests
{
    private const string Fms = "plugins/xlua/scripts/B738.a_fms/B738.a_fms.lua";

    [Fact]
    public void Execute_WhenStandalonePatchOwnsTarget_BlocksBeforeBackupOrWrite()
    {
        using var directory = new DeclarativePatchManifestTests.TemporaryDirectory();
        var root = Path.Combine(directory.Path, "aircraft");
        var target = Path.Combine(root, Fms.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "standalone patch");
        Directory.CreateDirectory(Path.Combine(root, ".zibo-cpdlc-patch"));
        File.WriteAllText(Path.Combine(root, ".zibo-cpdlc-patch", "state.json"), "{}");
        var store = TestToolStateStore.Create(Path.Combine(directory.Path, "state"));
        var engine = new ContentPatchEngine(store, () => false);
        var plan = CreatePlan(CreateDescriptor(ContentPatchActivation.Managed), root,
            ContentPatchMutation.Write(Fms, Encoding.UTF8.GetBytes("MTK patch"), "test"));

        var result = engine.Execute(plan, CreateVariant(Path.Combine(root, "737_70NG.acf")));

        Assert.False(result.Succeeded);
        Assert.Equal("Blocked", result.Status);
        Assert.Equal("standalone patch", File.ReadAllText(target));
        Assert.Empty(store.Load().ContentInstallations);
    }

    [Fact]
    public void Restore_WhenStandalonePatchStateAppears_BlocksBeforeChangingTarget()
    {
        using var directory = new DeclarativePatchManifestTests.TemporaryDirectory();
        var root = Path.Combine(directory.Path, "aircraft");
        var target = Path.Combine(root, Fms.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "stock");
        var store = TestToolStateStore.Create(Path.Combine(directory.Path, "state"));
        var engine = new ContentPatchEngine(store, () => false);
        var descriptor = CreateDescriptor(ContentPatchActivation.Managed);
        var variant = CreateVariant(Path.Combine(root, "737_70NG.acf"));
        Assert.True(engine.Execute(CreatePlan(descriptor, root,
            ContentPatchMutation.Write(Fms, Encoding.UTF8.GetBytes("MTK patch"), "test")), variant).Succeeded);
        Directory.CreateDirectory(Path.Combine(root, ".zibo-cpdlc-patch"));
        File.WriteAllText(Path.Combine(root, ".zibo-cpdlc-patch", "state.json"), "{}");

        var result = engine.Restore(descriptor, variant);

        Assert.False(result.Succeeded);
        Assert.Equal("Blocked", result.Status);
        Assert.Equal("MTK patch", File.ReadAllText(target));
        Assert.NotNull(store.TryGetContentInstallation(root)?.ContentComponents.GetValueOrDefault(descriptor.ComponentId));
    }

    [Fact]
    public void ExecuteAndRestore_RestoresExactPreInstallState()
    {
        using var directory = new DeclarativePatchManifestTests.TemporaryDirectory();
        var aircraftRoot = Path.Combine(directory.Path, "aircraft");
        Directory.CreateDirectory(aircraftRoot);
        var existingPath = Path.Combine(aircraftRoot, "existing.txt");
        var createdPath = Path.Combine(aircraftRoot, "created.txt");
        File.WriteAllText(existingPath, "original");
        var variant = CreateVariant(Path.Combine(aircraftRoot, "737_70NG.acf"));
        var store = TestToolStateStore.Create(Path.Combine(directory.Path, "state"));
        var engine = new ContentPatchEngine(store, isXPlaneRunning: () => false);
        var descriptor = CreateDescriptor(ContentPatchActivation.Managed);
        var plan = CreatePlan(
            descriptor,
            aircraftRoot,
            ContentPatchMutation.Write("existing.txt", Encoding.UTF8.GetBytes("updated"), "update existing"),
            ContentPatchMutation.Write("created.txt", Encoding.UTF8.GetBytes("created"), "create file"));

        var applied = engine.Execute(plan, variant);

        Assert.True(applied.Succeeded);
        Assert.True(applied.Changed);
        Assert.Equal("updated", File.ReadAllText(existingPath));
        Assert.Equal("created", File.ReadAllText(createdPath));
        Assert.Single(applied.BackupPaths);
        Assert.Contains("levelup-737ng-series", applied.BackupPaths[0]);

        var restored = engine.Restore(descriptor, variant);

        Assert.True(restored.Succeeded);
        Assert.Equal("Restored", restored.Status);
        Assert.Equal("original", File.ReadAllText(existingPath));
        Assert.False(File.Exists(createdPath));
        Assert.False(store.TryGetContentInstallation(aircraftRoot)?.ContentComponents.ContainsKey(descriptor.ComponentId));
        Assert.False(store.TryGetProductTarget(variant)?.ContentComponents.ContainsKey(descriptor.ComponentId));
    }

    [Fact]
    public void Restore_WhenInstalledFileChanged_BlocksWithoutChangingAnyTarget()
    {
        using var directory = new DeclarativePatchManifestTests.TemporaryDirectory();
        var aircraftRoot = Path.Combine(directory.Path, "aircraft");
        Directory.CreateDirectory(aircraftRoot);
        var existingPath = Path.Combine(aircraftRoot, "existing.txt");
        var createdPath = Path.Combine(aircraftRoot, "created.txt");
        File.WriteAllText(existingPath, "original");
        var variant = CreateVariant(Path.Combine(aircraftRoot, "737_70NG.acf"));
        var store = TestToolStateStore.Create(Path.Combine(directory.Path, "state"));
        var engine = new ContentPatchEngine(store, isXPlaneRunning: () => false);
        var descriptor = CreateDescriptor(ContentPatchActivation.ExplicitOptIn);
        var plan = CreatePlan(
            descriptor,
            aircraftRoot,
            ContentPatchMutation.Write("existing.txt", Encoding.UTF8.GetBytes("updated"), "update existing"),
            ContentPatchMutation.Write("created.txt", Encoding.UTF8.GetBytes("created"), "create file"));
        Assert.True(engine.Execute(plan, variant).Succeeded);
        File.WriteAllText(existingPath, "later user change");

        var restored = engine.Restore(descriptor, variant);

        Assert.False(restored.Succeeded);
        Assert.Equal("Blocked", restored.Status);
        Assert.Contains("later change", restored.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("later user change", File.ReadAllText(existingPath));
        Assert.Equal("created", File.ReadAllText(createdPath));
    }

    [Fact]
    public void Execute_WhenLaterMutationFails_RollsBackEarlierMutation()
    {
        using var directory = new DeclarativePatchManifestTests.TemporaryDirectory();
        var aircraftRoot = Path.Combine(directory.Path, "aircraft");
        Directory.CreateDirectory(aircraftRoot);
        var firstPath = Path.Combine(aircraftRoot, "first.txt");
        File.WriteAllText(firstPath, "original");
        Directory.CreateDirectory(Path.Combine(aircraftRoot, "cannot-replace-directory"));
        var variant = CreateVariant(Path.Combine(aircraftRoot, "737_70NG.acf"));
        var store = TestToolStateStore.Create(Path.Combine(directory.Path, "state"));
        var engine = new ContentPatchEngine(store, isXPlaneRunning: () => false);
        var descriptor = new ContentPatchDescriptor(
            "test.optional",
            "Test patch",
            "https://github.com/example/test",
            new ContentPatchLifecyclePolicy(ContentPatchActivation.ExplicitOptIn, new HashSet<ContentPatchTrigger> { ContentPatchTrigger.Manual }),
            RestartRequired: true);
        var plan = new ContentPatchPlan(
            descriptor,
            "1.0.0",
            ContentPatchAction.Install,
            aircraftRoot,
            [
                ContentPatchMutation.Write("first.txt", Encoding.UTF8.GetBytes("changed"), "change first"),
                ContentPatchMutation.Write("cannot-replace-directory", Encoding.UTF8.GetBytes("invalid"), "force failure")
            ],
            [],
            IsSafe: true,
            "test");

        var exception = Record.Exception(() => engine.Execute(plan, variant));
        Assert.True(
            exception is IOException or UnauthorizedAccessException,
            $"Expected a platform file-system exception, but received {exception?.GetType().FullName ?? "no exception"}.");

        Assert.Equal("original", File.ReadAllText(firstPath));
        Assert.Empty(store.Load().Aircraft);
    }

    [Fact]
    public void Execute_WhenSourceChangedSincePlanning_DoesNotOverwriteEdit()
    {
        using var directory = new DeclarativePatchManifestTests.TemporaryDirectory();
        var targetPath = Path.Combine(directory.Path, "target.txt");
        File.WriteAllText(targetPath, "foreign edit");
        var store = TestToolStateStore.Create(Path.Combine(directory.Path, "state"));
        var engine = new ContentPatchEngine(store, () => false);
        var plan = CreatePlan(CreateDescriptor(ContentPatchActivation.Managed), directory.Path,
            ContentPatchMutation.Write("target.txt", Encoding.UTF8.GetBytes("planned output"), "test")) with
        { ExpectedSourceHashes = new Dictionary<string, string?> { ["target.txt"] = new string('0', 64) } };
        var result = engine.Execute(plan, CreateVariant(Path.Combine(directory.Path, "737_70NG.acf")));
        Assert.False(result.Succeeded);
        Assert.Equal("foreign edit", File.ReadAllText(targetPath));
        Assert.Empty(store.Load().ContentInstallations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SequentialPatches_RefreshThenMigration_RetainsHistoryAndCommitsOwnershipAtomically(bool failMigration)
    {
        using var directory = new DeclarativePatchManifestTests.TemporaryDirectory();
        var root = directory.Path;
        var path = Path.Combine(root, "shared.lua");
        File.WriteAllText(path, "stock");
        var variant = CreateVariant(Path.Combine(root, "737_70NG.acf"));
        var store = TestToolStateStore.Create(Path.Combine(root, "state"));
        var engine = new ContentPatchEngine(store, () => false);
        var first = CreateDescriptor(ContentPatchActivation.Managed) with { ComponentId = "first" };
        var second = first with { ComponentId = "second" };
        ContentPatchMutation Write(string text) => ContentPatchMutation.Write("shared.lua", Encoding.UTF8.GetBytes(text), "test");
        Assert.True(engine.Execute(CreatePlan(first, root, Write("one")), variant).Succeeded);
        var firstState = store.TryGetContentInstallation(root)!.ContentComponents["first"];
        // A legacy per-variant entry must not resurrect this source after transfer.
        store.UpdateTarget(variant, target => target.ContentComponents["first"] = firstState);
        Assert.True(engine.Execute(CreatePlan(second, root, Write("two")), variant).Succeeded);
        var refresh = engine.Execute(CreatePlan(first, root, Write("two")), variant);
        Assert.True(refresh.Succeeded);
        Assert.False(refresh.Changed);
        var installation = store.TryGetContentInstallation(root)!;
        Assert.Equal(firstState.Files[0].InstalledSha256, installation.ContentComponents["first"].Files[0].InstalledSha256);
        var manifest = new CompatibilityPackageManifest { PackageId = "group",
            Sources = [new() { PackageId = "first" }, new() { PackageId = "second" }],
            Modules = [new() { Targets = [new() { RelativePath = "shared.lua" }] }] };
        var migration = CatalogGroupMigration.Prepare(root, manifest, installation.ContentComponents, installation.Backups)!;
        var plan = CreatePlan(first with { ComponentId = "group" }, root, Write(failMigration ? "new" : "two")) with
            { MigratedState = migration, Sources = manifest.Sources };
        var stateBefore = File.ReadAllText(store.StatePath);
        if (failMigration)
        {
            Directory.CreateDirectory(Path.Combine(root, "write-failure"));
            plan = plan with { Mutations = [.. plan.Mutations, ContentPatchMutation.Write("write-failure", [1], "fail")] };
            Assert.NotNull(Record.Exception(() => engine.Execute(plan, variant)));
            Assert.Equal(stateBefore, File.ReadAllText(store.StatePath));
            Assert.Equal("two", File.ReadAllText(path));
        }
        else
        {
            Assert.True(engine.Execute(plan, variant).Succeeded);
            Assert.True(store.TryGetContentInstallation(root)!.HasAuthoritativeContentState);
            Assert.Equal("group", Assert.Single(store.TryGetContentInstallation(root)!.ContentComponents).Key);
            Assert.True(engine.Execute(plan with { MigratedState = null }, variant).Succeeded);
            Assert.True(engine.Restore(plan.Descriptor, variant).Succeeded);
            Assert.Empty(store.TryGetContentInstallation(root)!.ContentComponents);
            Assert.Equal("stock", File.ReadAllText(path));
        }
    }

    private static ContentPatchDescriptor CreateDescriptor(ContentPatchActivation activation) =>
        new(
            "test.optional",
            "Test patch",
            "https://github.com/example/test",
            new ContentPatchLifecyclePolicy(activation, new HashSet<ContentPatchTrigger> { ContentPatchTrigger.Manual }),
            RestartRequired: true);

    private static ContentPatchPlan CreatePlan(
        ContentPatchDescriptor descriptor,
        string aircraftRoot,
        params ContentPatchMutation[] mutations) =>
        new(
            descriptor,
            "1.0.0",
            ContentPatchAction.Install,
            aircraftRoot,
            mutations,
            [],
            IsSafe: true,
            "test");

    private static AircraftVariantViewAnalysis CreateVariant(string acfPath) =>
        new(
            AircraftId: "levelup-test",
            DisplayName: "LevelUp test",
            Family: "LevelUp",
            AcfPath: acfPath,
            PrefsPath: Path.ChangeExtension(acfPath, null) + "_prefs.txt",
            Source: "test",
            SourceRef: "test",
            SourceVersion: "1",
            LocalVersion: null,
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
            IdentityStatus: "test",
            QuickViewStatus: "test",
            DefaultViewStatus: "test");
}
