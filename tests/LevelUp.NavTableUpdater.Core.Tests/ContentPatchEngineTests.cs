using System.Text;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Content;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class ContentPatchEngineTests
{
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
