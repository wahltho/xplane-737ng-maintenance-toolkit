using System.Text;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class DeclarativeContentPatchOperationTests
{
    [Fact]
    public async Task PlanAsync_WhenPackageIsValid_ProducesDryRunWithoutChangingTarget()
    {
        using var fixture = Fixture.Create();
        var operation = new DeclarativeContentPatchOperation(fixture.Store, isXPlaneRunning: () => false);

        var plan = await operation.PlanAsync(ContentPatchAction.Update, fixture.Variant, fixture.PackageDirectory);

        Assert.True(plan.IsSafe);
        Assert.Single(plan.Mutations);
        Assert.Equal("before\r\n", File.ReadAllText(fixture.TargetPath));
        Assert.Empty(fixture.Store.Load().ContentInstallations);
    }

    [Fact]
    public async Task PlanAsync_WhenLevelUpPackageTargetsZiboVariant_BlocksIt()
    {
        using var fixture = Fixture.Create();
        var operation = new DeclarativeContentPatchOperation(fixture.Store, isXPlaneRunning: () => false);

        var plan = await operation.PlanAsync(
            ContentPatchAction.Update,
            fixture.Variant with { Family = "Zibo" },
            fixture.PackageDirectory);

        Assert.False(plan.IsSafe);
        Assert.Contains("selected variant", plan.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("before\r\n", File.ReadAllText(fixture.TargetPath));
    }

    [Fact]
    public async Task InstallAndUninstall_HashlessExactTextPatch_PreservesUnrelatedContent()
    {
        using var fixture = Fixture.Create(includeSourceHash: false);
        var original = Encoding.UTF8.GetBytes("unrelated header\r\nbefore\r\nunrelated footer\r\n");
        File.WriteAllBytes(fixture.TargetPath, original);
        var operation = new DeclarativeContentPatchOperation(fixture.Store, isXPlaneRunning: () => false);

        var installed = await operation.RunAsync(ContentPatchAction.Install, fixture.Variant, fixture.PackageDirectory);

        Assert.True(installed.Succeeded);
        Assert.Equal(
            "unrelated header\r\ninstalled\r\nafter\r\nunrelated footer\r\n",
            File.ReadAllText(fixture.TargetPath));

        var uninstalled = await operation.RunAsync(ContentPatchAction.Uninstall, fixture.Variant, fixture.PackageDirectory);

        Assert.True(uninstalled.Succeeded);
        Assert.Equal(original, File.ReadAllBytes(fixture.TargetPath));
    }

    [Fact]
    public async Task PlanAsync_WhenHashlessExactTextPatchIsAlreadyPresent_ReportsNoChanges()
    {
        using var fixture = Fixture.Create(includeSourceHash: false);
        File.WriteAllText(fixture.TargetPath, "installed\r\nafter\r\n");
        var operation = new DeclarativeContentPatchOperation(fixture.Store, isXPlaneRunning: () => false);

        var plan = await operation.PlanAsync(ContentPatchAction.Update, fixture.Variant, fixture.PackageDirectory);

        Assert.True(plan.IsSafe);
        Assert.Empty(plan.Mutations);
        Assert.Contains("already installed", plan.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("unrelated\r\n")]
    [InlineData("before\r\nbefore\r\n")]
    public async Task PlanAsync_WhenHashlessExactTextStructureIsMissingOrAmbiguous_BlocksWithoutWrites(string content)
    {
        using var fixture = Fixture.Create(includeSourceHash: false);
        File.WriteAllText(fixture.TargetPath, content);
        var operation = new DeclarativeContentPatchOperation(fixture.Store, isXPlaneRunning: () => false);

        var plan = await operation.PlanAsync(ContentPatchAction.Update, fixture.Variant, fixture.PackageDirectory);

        Assert.False(plan.IsSafe);
        Assert.Contains("Structurally incompatible", plan.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(content, File.ReadAllText(fixture.TargetPath));
    }

    [Fact]
    public async Task PlanAsync_WhenHashlessOperationCannotValidateSourceStructurally_BlocksIt()
    {
        using var fixture = Fixture.Create(
            includeSourceHash: false,
            operation: "sparse-bytes-v1");
        var operation = new DeclarativeContentPatchOperation(fixture.Store, isXPlaneRunning: () => false);

        var plan = await operation.PlanAsync(ContentPatchAction.Update, fixture.Variant, fixture.PackageDirectory);

        Assert.False(plan.IsSafe);
        Assert.Contains("requires sourceSha256", plan.StatusMessage, StringComparison.Ordinal);
        Assert.Equal("before\r\n", File.ReadAllText(fixture.TargetPath));
    }

    [Fact]
    public async Task InstallAndUninstall_OptionalExactTextPatch_RoundTripsOriginalAndState()
    {
        using var fixture = Fixture.Create();
        var operation = new DeclarativeContentPatchOperation(fixture.Store, isXPlaneRunning: () => false);

        var installed = await operation.RunAsync(ContentPatchAction.Install, fixture.Variant, fixture.PackageDirectory);

        Assert.True(installed.Succeeded);
        Assert.True(installed.Changed);
        Assert.Equal("installed\r\nafter\r\n", File.ReadAllText(fixture.TargetPath));
        var document = fixture.Store.Load();
        Assert.Empty(document.Aircraft);
        var component = Assert.Single(Assert.Single(document.ContentInstallations.Values).ContentComponents.Values);
        Assert.Equal(ContentPatchCatalog.FansCdu.ComponentId, component.ComponentId);
        Assert.Single(component.Files);
        Assert.True(File.Exists(component.Files[0].BackupPath));

        var uninstalled = await operation.RunAsync(ContentPatchAction.Uninstall, fixture.Variant, fixture.PackageDirectory);

        Assert.True(uninstalled.Succeeded);
        Assert.True(uninstalled.Changed);
        Assert.Equal("before\r\n", File.ReadAllText(fixture.TargetPath));
        Assert.Empty(Assert.Single(fixture.Store.Load().ContentInstallations.Values).ContentComponents);
    }

    [Fact]
    public async Task Uninstall_WhenInstalledTargetWasChanged_BlocksWithoutOverwritingUserChange()
    {
        using var fixture = Fixture.Create();
        var operation = new DeclarativeContentPatchOperation(fixture.Store, isXPlaneRunning: () => false);
        await operation.RunAsync(ContentPatchAction.Install, fixture.Variant, fixture.PackageDirectory);
        File.WriteAllText(fixture.TargetPath, "user change\n");

        var result = await operation.RunAsync(ContentPatchAction.Uninstall, fixture.Variant, fixture.PackageDirectory);

        Assert.False(result.Succeeded);
        Assert.False(result.Changed);
        Assert.Equal("user change\n", File.ReadAllText(fixture.TargetPath));
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(
            DeclarativePatchManifestTests.TemporaryDirectory directory,
            string packageDirectory,
            string targetPath,
            AircraftVariantViewAnalysis variant,
            ToolStateStore store)
        {
            Directory = directory;
            PackageDirectory = packageDirectory;
            TargetPath = targetPath;
            Variant = variant;
            Store = store;
        }

        public DeclarativePatchManifestTests.TemporaryDirectory Directory { get; }

        public string PackageDirectory { get; }

        public string TargetPath { get; }

        public AircraftVariantViewAnalysis Variant { get; }

        public ToolStateStore Store { get; }

        public static Fixture Create(
            bool includeSourceHash = true,
            string operation = "exact-text-replacements-v1")
        {
            var directory = new DeclarativePatchManifestTests.TemporaryDirectory();
            var aircraftRoot = Path.Combine(directory.Path, "aircraft");
            var packageRoot = Path.Combine(directory.Path, "package");
            var relativeTarget = "plugins/xlua/scripts/B738.tablet/B738.tablet.lua";
            var targetPath = Path.Combine(aircraftRoot, relativeTarget.Replace('/', Path.DirectorySeparatorChar));
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            var source = Encoding.UTF8.GetBytes("before\r\n");
            var result = Encoding.UTF8.GetBytes("installed\r\nafter\r\n");
            File.WriteAllBytes(targetPath, source);

            var payload = Encoding.UTF8.GetBytes("""
                {
                  "format": "exact-text-replacements-v1",
                  "replacements": [
                    {
                      "name": "append line",
                      "oldLines": ["before"],
                      "newLines": ["installed", "after"]
                    }
                  ]
                }
                """);
            System.IO.Directory.CreateDirectory(Path.Combine(packageRoot, "patches"));
            File.WriteAllBytes(Path.Combine(packageRoot, "patches", "change.json"), payload);
            File.WriteAllText(
                Path.Combine(packageRoot, "package-manifest.json"),
                DeclarativePatchManifestTests.BuildManifest(
                    "patches/change.json",
                    payload,
                    relativeTarget,
                    DeclarativePatchManifestTests.Sha256(source),
                    DeclarativePatchManifestTests.Sha256(result),
                    includeSourceHash,
                    includeResultHash: includeSourceHash,
                    operation: operation));

            var acfPath = Path.Combine(aircraftRoot, "737_70NG.acf");
            File.WriteAllText(acfPath, "1200 Version\n");
            var variant = CreateVariant(acfPath);
            var store = TestToolStateStore.Create(Path.Combine(directory.Path, "state"));
            return new Fixture(directory, packageRoot, targetPath, variant, store);
        }

        public void Dispose() => Directory.Dispose();

        private static AircraftVariantViewAnalysis CreateVariant(string acfPath) =>
            new(
                AircraftId: "levelup-737-700",
                DisplayName: "LevelUp 737-700",
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
}
