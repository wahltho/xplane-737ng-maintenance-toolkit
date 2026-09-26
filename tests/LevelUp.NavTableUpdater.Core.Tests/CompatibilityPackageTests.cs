using System.IO.Compression;
using LevelUp.NavTableUpdater.Core.Upstream;
using LevelUp.NavTableUpdater.Core.State;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LevelUp.NavTableUpdater.App.ViewModels;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.Manifest;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class CompatibilityPackageTests
{
    [Fact]
    public async Task Plan_WhenStandalonePatchOwnsSharedFms_BlocksBeforeAdoptingItsBytes()
    {
        using var fixture = Fixture.Create();
        var root = Path.GetDirectoryName(fixture.Variant.AcfPath)!;
        const string fms = "plugins/xlua/scripts/B738.a_fms/B738.a_fms.lua";
        var fmsPath = Path.Combine(root, fms.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fmsPath)!);
        File.WriteAllText(fmsPath, "before\r\n", new UTF8Encoding(false));
        var manifestPath = Path.Combine(fixture.PackageDirectory, "package-manifest.json");
        File.WriteAllText(manifestPath, File.ReadAllText(manifestPath)
            .Replace("plugins/xlua/scripts/shared.lua", fms, StringComparison.Ordinal));
        Directory.CreateDirectory(Path.Combine(root, ".zibo-auto-jetway-patch"));
        File.WriteAllText(Path.Combine(root, ".zibo-auto-jetway-patch", "state.json"), "{}");
        var operation = new CompatibilityPackageOperation(fixture.Store, isXPlaneRunning: () => false);

        var plan = await operation.PlanAsync(ContentPatchAction.Install, fixture.Variant,
            fixture.PackageDirectory, ["core", "standard"]);

        Assert.False(plan.IsSafe);
        Assert.Contains("AUTO JETWAY", plan.StatusMessage, StringComparison.Ordinal);
        Assert.Equal("before\r\n", File.ReadAllText(fmsPath));
        Assert.Empty(fixture.Store.Load().ContentInstallations);
    }

    [Fact]
    public void Parse_WithInvalidPolicyDefaults_RejectsManifest()
    {
        using var fixture = Fixture.Create();
        var json = File.ReadAllText(Path.Combine(fixture.PackageDirectory, "package-manifest.json"))
            .Replace("\"defaultEnabled\":true", "\"defaultEnabled\":false", StringComparison.Ordinal);

        var error = Assert.Throws<InvalidOperationException>(() => CompatibilityPackageManifestParser.Parse(json));

        Assert.Contains("Required module", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultSelection_IncludesRequiredAndRecommendedButNotOptional()
    {
        using var fixture = Fixture.Create();
        var package = CompatibilityPackageLoader.LoadDirectory(fixture.PackageDirectory);

        var selected = CompatibilityPackagePlanBuilder.DefaultSelection(package.Manifest);

        Assert.Equal(["core", "standard"], selected);
    }

    [Fact]
    public void CatalogSelection_UsesEveryModuleInInstallationOrder()
    {
        using var fixture = Fixture.Create();
        var package = CompatibilityPackageLoader.LoadDirectory(fixture.PackageDirectory);

        var selected = MainWindowViewModel.CatalogCompatibilityModuleIds(package);

        Assert.Equal(["core", "standard", "optional"], selected);
    }

    [Fact]
    public void Parse_WhenConditionalTargetUsesOlderSchema_RejectsManifest()
    {
        using var fixture = Fixture.Create();
        fixture.AddConditionalHardeningModule(schemaVersion: 4);
        var json = File.ReadAllText(Path.Combine(fixture.PackageDirectory, "package-manifest.json"));

        var error = Assert.Throws<InvalidOperationException>(() =>
            CompatibilityPackageManifestParser.Parse(json));

        Assert.Contains("Schema 5", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Plan_ConditionalTargetRunsOnlyWhenNamedFunctionalModuleIsSelected()
    {
        using var fixture = Fixture.Create();
        fixture.AddConditionalHardeningModule();
        var operation = new CompatibilityPackageOperation(fixture.Store, isXPlaneRunning: () => false);

        var withoutOptional = await operation.PlanAsync(
            ContentPatchAction.Install,
            fixture.Variant,
            fixture.PackageDirectory,
            ["core", "standard", "hardening"]);
        var withOptional = await operation.PlanAsync(
            ContentPatchAction.Install,
            fixture.Variant,
            fixture.PackageDirectory,
            ["core", "standard", "optional", "hardening"]);

        Assert.True(withoutOptional.IsSafe, withoutOptional.StatusMessage);
        Assert.Equal("standard\r\n", Encoding.UTF8.GetString(Assert.Single(withoutOptional.Mutations).DesiredBytes!));
        Assert.Contains(withoutOptional.Log, line => line.Contains("[CONDITION]", StringComparison.Ordinal));
        Assert.True(withOptional.IsSafe, withOptional.StatusMessage);
        Assert.Equal("hardened\r\n", Encoding.UTF8.GetString(Assert.Single(withOptional.Mutations).DesiredBytes!));
    }

    [Fact]
    public async Task Update_ConditionalTargetTracksFunctionalModuleSelectionAndPreservesRestore()
    {
        using var fixture = Fixture.Create();
        fixture.AddConditionalHardeningModule();
        var operation = new CompatibilityPackageOperation(fixture.Store, isXPlaneRunning: () => false);

        var installed = await operation.RunAsync(ContentPatchAction.Install, fixture.Variant,
            fixture.PackageDirectory, ["core", "standard", "optional", "hardening"]);
        Assert.True(installed.Succeeded, installed.Message);
        Assert.Equal("hardened\r\n", File.ReadAllText(fixture.TargetPath));

        var reduced = await operation.RunAsync(ContentPatchAction.Update, fixture.Variant,
            fixture.PackageDirectory, ["core", "standard", "hardening"]);
        Assert.True(reduced.Succeeded, reduced.Message);
        Assert.Equal("standard\r\n", File.ReadAllText(fixture.TargetPath));

        var restoredSelection = await operation.RunAsync(ContentPatchAction.Update, fixture.Variant,
            fixture.PackageDirectory, ["core", "standard", "optional", "hardening"]);
        Assert.True(restoredSelection.Succeeded, restoredSelection.Message);
        Assert.Equal("hardened\r\n", File.ReadAllText(fixture.TargetPath));

        var restored = operation.Restore(fixture.Variant, fixture.PackageDirectory);
        Assert.True(restored.Succeeded, restored.Message);
        Assert.Equal("before\r\n", File.ReadAllText(fixture.TargetPath));
    }

    [Fact]
    public void Parse_ResolvedGroupWithUnknownConditionalModule_RejectsManifest()
    {
        using var fixture = Fixture.Create();
        fixture.AddConditionalHardeningModule(requiredModuleId: "not-in-group");
        var manifestPath = Path.Combine(fixture.PackageDirectory, "package-manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        manifest["sources"] = JsonSerializer.SerializeToNode(new[]
        {
            new
            {
                packageId = "fixture.source", moduleId = "hardening", releaseTag = "v1.0.0",
                assetSha256 = new string('0', 64), repositoryUrl = "https://github.com/example/fixture"
            }
        });

        var error = Assert.Throws<InvalidOperationException>(() =>
            CompatibilityPackageManifestParser.Parse(manifest.ToJsonString()));

        Assert.Contains("unknown conditional module", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ConditionalTargetBeforeItsFunctionalModule_RejectsManifest()
    {
        using var fixture = Fixture.Create();
        fixture.AddConditionalHardeningModule();
        var manifestPath = Path.Combine(fixture.PackageDirectory, "package-manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        var hardening = manifest["modules"]!.AsArray().Single(module =>
            module!["moduleId"]!.GetValue<string>() == "hardening")!.AsObject();
        hardening["installationOrder"] = 25;

        var error = Assert.Throws<InvalidOperationException>(() =>
            CompatibilityPackageManifestParser.Parse(manifest.ToJsonString()));

        Assert.Contains("must run after module optional", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallUpdateAndRestore_RebuildsSharedTargetAsOneModulePipeline()
    {
        using var fixture = Fixture.Create();
        var operation = new CompatibilityPackageOperation(fixture.Store, isXPlaneRunning: () => false);

        var installed = await operation.RunAsync(
            ContentPatchAction.Install,
            fixture.Variant,
            fixture.PackageDirectory,
            ["core", "standard"]);

        Assert.True(installed.Succeeded);
        Assert.Equal("standard\r\n", File.ReadAllText(fixture.TargetPath));
        var state = Assert.Single(fixture.Store.Load().ContentInstallations.Values).ContentComponents["levelup.compatibility"];
        Assert.Equal(["core", "standard"], state.EnabledModules);
        Assert.Single(state.Files);
        Assert.Equal(Sha256(Encoding.UTF8.GetBytes("before\r\n")), state.Files[0].OriginalSha256);

        var updated = await operation.RunAsync(
            ContentPatchAction.Update,
            fixture.Variant,
            fixture.PackageDirectory,
            ["core", "standard", "optional"]);

        Assert.True(updated.Succeeded);
        Assert.Equal("optional\r\n", File.ReadAllText(fixture.TargetPath));
        state = Assert.Single(fixture.Store.Load().ContentInstallations.Values).ContentComponents["levelup.compatibility"];
        Assert.Equal(["core", "standard", "optional"], state.EnabledModules);
        Assert.Equal(Sha256(Encoding.UTF8.GetBytes("before\r\n")), state.Files[0].OriginalSha256);

        var reduced = await operation.RunAsync(
            ContentPatchAction.Update,
            fixture.Variant,
            fixture.PackageDirectory,
            ["core"]);

        Assert.True(reduced.Succeeded);
        Assert.Equal("core\r\n", File.ReadAllText(fixture.TargetPath));
        state = Assert.Single(fixture.Store.Load().ContentInstallations.Values).ContentComponents["levelup.compatibility"];
        Assert.Equal(["core"], state.EnabledModules);

        var restored = operation.Restore(fixture.Variant, fixture.PackageDirectory);

        Assert.True(restored.Succeeded);
        Assert.Equal("before\r\n", File.ReadAllText(fixture.TargetPath));
        Assert.Empty(Assert.Single(fixture.Store.Load().ContentInstallations.Values).ContentComponents);
    }

    [Fact]
    public async Task Plan_EnforcesRequiredModulesAndDependencies()
    {
        using var fixture = Fixture.Create(optionalRequiresStandard: true);
        var operation = new CompatibilityPackageOperation(fixture.Store, isXPlaneRunning: () => false);

        var plan = await operation.PlanAsync(
            ContentPatchAction.Update,
            fixture.Variant,
            fixture.PackageDirectory,
            ["optional"]);

        Assert.True(plan.IsSafe);
        Assert.Equal(["core", "standard", "optional"], plan.EnabledModules);
        Assert.Equal("optional\r\n", Encoding.UTF8.GetString(Assert.Single(plan.Mutations).DesiredBytes!));
        Assert.Equal("before\r\n", File.ReadAllText(fixture.TargetPath));
    }

    [Fact]
    public async Task Plan_WhenAircraftReleaseIsUnsupported_BlocksWithoutChanges()
    {
        using var fixture = Fixture.Create();
        var operation = new CompatibilityPackageOperation(fixture.Store, isXPlaneRunning: () => false);
        var unsupported = fixture.Variant with
        {
            LocalVersion = "V2.S2.00",
            SourceVersion = "V2.S2.00",
            SourceRef = "V2.S2.00"
        };

        var plan = await operation.PlanAsync(
            ContentPatchAction.Update,
            unsupported,
            fixture.PackageDirectory,
            ["core", "standard"]);

        Assert.False(plan.IsSafe);
        Assert.Contains("not supported", plan.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("before\r\n", File.ReadAllText(fixture.TargetPath));
    }

    [Fact]
    public async Task Plan_WhenKnownSourceHashDiffersButOwnedBlocksAreUnique_ComposesStructurally()
    {
        using var fixture = Fixture.Create(omitStructuralResultHashes: true);
        File.WriteAllText(fixture.TargetPath, "foreign\r\nbefore\r\n", new UTF8Encoding(false));
        var operation = new CompatibilityPackageOperation(fixture.Store, isXPlaneRunning: () => false);

        var plan = await operation.PlanAsync(
            ContentPatchAction.Update,
            fixture.Variant,
            fixture.PackageDirectory,
            ["core", "standard"]);

        Assert.True(plan.IsSafe);
        Assert.Equal("foreign\r\nstandard\r\n", Encoding.UTF8.GetString(Assert.Single(plan.Mutations).DesiredBytes!));
        Assert.Equal("foreign\r\nbefore\r\n", File.ReadAllText(fixture.TargetPath));
    }

    [Fact]
    public async Task Update_WhenAnotherCompatibilityPackageChangedSharedTarget_PreservesBothPackages()
    {
        using var fixture = Fixture.Create(singleComposableModule: true, omitStructuralResultHashes: true);
        var operation = new CompatibilityPackageOperation(fixture.Store, isXPlaneRunning: () => false);

        var firstInstall = await operation.RunAsync(
            ContentPatchAction.Install,
            fixture.Variant,
            fixture.PackageDirectory,
            ["core"]);
        var independentPackage = fixture.CreateIndependentMarkedBlockPackage();
        var secondInstall = await operation.RunAsync(
            ContentPatchAction.Install,
            fixture.Variant,
            independentPackage,
            ["independent"]);

        Assert.True(firstInstall.Succeeded);
        Assert.True(secondInstall.Succeeded);
        var composed = "core\r\n-- BEGIN INDEPENDENT\r\nindependent\r\n-- END INDEPENDENT\r\n";
        Assert.Equal(composed, File.ReadAllText(fixture.TargetPath));

        var beforeRefresh = fixture.Store.TryGetContentInstallation(Path.GetDirectoryName(fixture.Variant.AcfPath)!)!
            .ContentComponents["levelup.compatibility"].Files.Single().InstalledSha256;
        var updated = await operation.RunAsync(
            ContentPatchAction.Update,
            fixture.Variant,
            fixture.PackageDirectory,
            ["core"]);

        Assert.Equal(beforeRefresh, fixture.Store.TryGetContentInstallation(Path.GetDirectoryName(fixture.Variant.AcfPath)!)!
            .ContentComponents["levelup.compatibility"].Files.Single().InstalledSha256);
        Assert.True(updated.Succeeded);
        Assert.False(updated.Changed);
        Assert.Equal(composed, File.ReadAllText(fixture.TargetPath));
        Assert.Contains(updated.Log, line => line.StartsWith("[COMPOSE]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Update_WhenOwnedBlockWasModified_BlocksWithoutChangingIndependentPackage()
    {
        using var fixture = Fixture.Create(singleComposableModule: true, omitStructuralResultHashes: true);
        var operation = new CompatibilityPackageOperation(fixture.Store, isXPlaneRunning: () => false);
        await operation.RunAsync(ContentPatchAction.Install, fixture.Variant, fixture.PackageDirectory, ["core"]);
        await operation.RunAsync(
            ContentPatchAction.Install,
            fixture.Variant,
            fixture.CreateIndependentMarkedBlockPackage(),
            ["independent"]);
        var modified = "damaged\r\n-- BEGIN INDEPENDENT\r\nindependent\r\n-- END INDEPENDENT\r\n";
        File.WriteAllText(fixture.TargetPath, modified, new UTF8Encoding(false));

        var updated = await operation.RunAsync(
            ContentPatchAction.Update,
            fixture.Variant,
            fixture.PackageDirectory,
            ["core"]);

        Assert.False(updated.Succeeded);
        Assert.Contains("structurally incompatible", updated.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(modified, File.ReadAllText(fixture.TargetPath));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Update_AfterCleanReinstallAtManagedPath_ReappliesAndRestores(bool originalExisted)
    {
        using var fixture = Fixture.Create(singleComposableModule: true, includeCopyModule: true);
        var operation = new CompatibilityPackageOperation(fixture.Store, isXPlaneRunning: () => false);
        var copiedPath = Path.Combine(Path.GetDirectoryName(fixture.TargetPath)!, "table.lua");
        var original = new byte[] { 0, 255, 23, 42 };
        if (originalExisted) File.WriteAllBytes(copiedPath, original);
        Assert.True((await operation.RunAsync(ContentPatchAction.Install, fixture.Variant,
            fixture.PackageDirectory, ["core", "table-payload"])).Succeeded);
        var patched = File.ReadAllBytes(copiedPath);

        // A fresh aircraft replaces this folder; the external Toolkit state survives.
        File.WriteAllText(fixture.TargetPath, "before\r\n", new UTF8Encoding(false));
        if (originalExisted) File.WriteAllBytes(copiedPath, original);
        else File.Delete(copiedPath);
        var result = await operation.RunAsync(ContentPatchAction.Update, fixture.Variant,
            fixture.PackageDirectory, ["core", "table-payload"]);
        Assert.True(result.Succeeded, result.Message);
        Assert.True(result.Changed);
        Assert.Equal(patched, File.ReadAllBytes(copiedPath));
        var repeat = await operation.RunAsync(ContentPatchAction.Update, fixture.Variant,
            fixture.PackageDirectory, ["core", "table-payload"]);
        Assert.True(repeat.Succeeded, repeat.Message);
        Assert.False(repeat.Changed);
        Assert.True(operation.Restore(fixture.Variant, fixture.PackageDirectory).Succeeded);
        Assert.Equal("before\r\n", File.ReadAllText(fixture.TargetPath));
        if (originalExisted) Assert.Equal(original, File.ReadAllBytes(copiedPath));
        else Assert.False(File.Exists(copiedPath));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FullReplacement_WithDifferentRecordedOriginal_ReinstallsAndRestoresBothGenerations(bool oldOriginalExisted)
    {
        using var fixture = Fixture.Create(singleComposableModule: true, includeCopyModule: true);
        var patches = new CompatibilityPackageOperation(fixture.Store, isXPlaneRunning: () => false);
        var copyPath = Path.Combine(Path.GetDirectoryName(fixture.TargetPath)!, "table.lua");
        if (oldOriginalExisted) File.WriteAllText(copyPath, "historical original");
        Assert.True((await patches.RunAsync(ContentPatchAction.Install, fixture.Variant,
            fixture.PackageDirectory, ["core", "table-payload"])).Succeeded);
        var oldState = fixture.Store.TryGetContentInstallation(Path.GetDirectoryName(fixture.Variant.AcfPath)!)!;
        var oldComponent = oldState.ContentComponents["levelup.compatibility"];
        var patchedBytes = File.ReadAllBytes(copyPath);

        // A legacy per-variant record must not resurrect ownership after replacement.
        var document = fixture.Store.Load();
        document.Aircraft["legacy-variant"] = new AircraftToolState
        {
            AircraftFolder = Path.GetDirectoryName(fixture.Variant.AcfPath)!,
            ContentComponents = new() { ["levelup.compatibility"] = oldComponent }
        };
        fixture.Store.Save(document);
        var (check, entry) = CreateFullBaseline(fixture);
        var aircraft = new AircraftUpdateOperation(fixture.Store, isXPlaneRunning: () => false);
        var full = aircraft.Apply(fixture.Variant, check, [entry]);
        Assert.True(full.Succeeded, full.Message);
        Assert.Equal("official new baseline", File.ReadAllText(copyPath));
        var reset = fixture.Store.TryGetContentInstallation(Path.GetDirectoryName(fixture.Variant.AcfPath)!)!;
        Assert.Empty(reset.ContentComponents);
        Assert.Equal(["core", "table-payload"], reset.PendingContentModules["levelup.compatibility"]);
        Assert.False(patches.Restore(fixture.Variant, fixture.PackageDirectory).Succeeded);
        var package = CompatibilityPackageLoader.LoadDirectory(fixture.PackageDirectory);
        var selection = MainWindowViewModel.CompatibilitySelection(package.Manifest, reset);
        Assert.Equal(["core", "table-payload"], selection);
        var reapplied = await patches.RunAsync(ContentPatchAction.Update, fixture.Variant, fixture.PackageDirectory, selection);
        Assert.True(reapplied.Succeeded, reapplied.Message);
        var installed = fixture.Store.TryGetContentInstallation(Path.GetDirectoryName(fixture.Variant.AcfPath)!)!;
        Assert.Empty(installed.PendingContentModules);
        var copy = installed.ContentComponents["levelup.compatibility"].Files.Single(f => f.RelativePath.EndsWith("table.lua"));
        Assert.Equal("official new baseline", File.ReadAllText(copy.BackupPath));
        Assert.Equal(patchedBytes, File.ReadAllBytes(copyPath));
        var repeated = await patches.RunAsync(ContentPatchAction.Update, fixture.Variant, fixture.PackageDirectory, selection);
        Assert.True(repeated.Succeeded, repeated.Message);
        Assert.False(repeated.Changed);
        Assert.True(patches.Restore(fixture.Variant, fixture.PackageDirectory).Succeeded);
        Assert.Equal("official new baseline", File.ReadAllText(copyPath));
        Assert.Empty(fixture.Store.TryGetContentInstallation(Path.GetDirectoryName(fixture.Variant.AcfPath)!)!.ContentComponents);

        // Full restore brings the original ownership/backup chain back with the old patched files.
        var restore = aircraft.RestoreLatest(fixture.Variant);
        Assert.True(restore.Succeeded, restore.Message);
        Assert.Equal(patchedBytes, File.ReadAllBytes(copyPath));
        var restored = fixture.Store.TryGetContentInstallation(Path.GetDirectoryName(fixture.Variant.AcfPath)!)!;
        Assert.Equal(JsonSerializer.Serialize(oldComponent), JsonSerializer.Serialize(restored.ContentComponents["levelup.compatibility"]));
        Assert.True(patches.Restore(fixture.Variant, fixture.PackageDirectory).Succeeded);
        if (oldOriginalExisted) Assert.Equal("historical original", File.ReadAllText(copyPath));
        else Assert.False(File.Exists(copyPath));
    }

    [Fact]
    public async Task FullReplacement_ActivationFailurePreservesPatchStateAndFiles()
    {
        using var fixture = Fixture.Create(singleComposableModule: true, includeCopyModule: true);
        var patches = new CompatibilityPackageOperation(fixture.Store, isXPlaneRunning: () => false);
        Assert.True((await patches.RunAsync(ContentPatchAction.Install, fixture.Variant,
            fixture.PackageDirectory, ["core", "table-payload"])).Succeeded);
        var before = File.ReadAllText(fixture.Store.StatePath);
        var (check, entry) = CreateFullBaseline(fixture);
        var full = new AircraftFullBaselineReplacement(fixture.Store, afterTargetMoved: () => throw new IOException("injected"));
        Assert.Throws<IOException>(() => full.Apply(fixture.Variant, check, [entry], CancellationToken.None, null, [], new List<string>()));
        Assert.Equal(before, File.ReadAllText(fixture.Store.StatePath));
        Assert.Equal("core\r\n", File.ReadAllText(fixture.TargetPath));
        Assert.True(patches.Restore(fixture.Variant, fixture.PackageDirectory).Succeeded);
    }

    [Fact]
    public async Task FullReplacement_StandaloneSourceSelectionSurvivesWithoutMigratingOldFileOwnership()
    {
        using var fixture = Fixture.Create(singleComposableModule: true, includeCopyModule: true);
        var patches = new CompatibilityPackageOperation(fixture.Store, isXPlaneRunning: () => false);
        Assert.True((await patches.RunAsync(ContentPatchAction.Install, fixture.Variant,
            fixture.PackageDirectory, ["core", "table-payload"])).Succeeded);
        fixture.Store.UpdateContentAndProduct(fixture.Variant, (installation, product) =>
        {
            var old = installation.ContentComponents["levelup.compatibility"];
            installation.ContentComponents = new() { ["standalone-copy"] = old };
            product.ContentComponents = new() { ["standalone-copy"] = old };
            product.InstalledContentPackageId = "standalone-copy";
        });
        var (check, entry) = CreateFullBaseline(fixture);
        var full = new AircraftUpdateOperation(fixture.Store, isXPlaneRunning: () => false).Apply(fixture.Variant, check, [entry]);
        Assert.True(full.Succeeded, full.Message);
        var manifestPath = Path.Combine(fixture.PackageDirectory, "package-manifest.json");
        var json = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        json["sources"] = JsonSerializer.SerializeToNode(new[] { new { packageId = "standalone-copy", moduleId = "table-payload", releaseTag = "v1" } });
        File.WriteAllText(manifestPath, json.ToJsonString());
        var manifest = CompatibilityPackageLoader.LoadDirectory(fixture.PackageDirectory).Manifest;
        var installation = fixture.Store.TryGetContentInstallation(Path.GetDirectoryName(fixture.Variant.AcfPath)!)!;
        Assert.Empty(installation.ContentComponents);
        var selected = MainWindowViewModel.CompatibilitySelection(manifest, installation);
        Assert.Contains("table-payload", selected);
        var applied = await patches.RunAsync(ContentPatchAction.Update, fixture.Variant, fixture.PackageDirectory, selected);
        Assert.True(applied.Succeeded, applied.Message);
        var state = fixture.Store.TryGetContentInstallation(Path.GetDirectoryName(fixture.Variant.AcfPath)!)!;
        Assert.Empty(state.PendingContentModules);
        Assert.Equal("levelup.compatibility", Assert.Single(state.ContentComponents).Key);
        Assert.True(patches.Restore(fixture.Variant, fixture.PackageDirectory).Succeeded);
        Assert.Equal("official new baseline", File.ReadAllText(Path.Combine(Path.GetDirectoryName(fixture.TargetPath)!, "table.lua")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullRestore_InvalidBackupOrMissingSnapshotLeavesFilesAndStateUntouched(bool legacyBackup)
    {
        using var fixture = Fixture.Create(singleComposableModule: true, includeCopyModule: true);
        var patches = new CompatibilityPackageOperation(fixture.Store, isXPlaneRunning: () => false);
        Assert.True((await patches.RunAsync(ContentPatchAction.Install, fixture.Variant,
            fixture.PackageDirectory, ["core", "table-payload"])).Succeeded);
        var (check, entry) = CreateFullBaseline(fixture);
        var aircraft = new AircraftUpdateOperation(fixture.Store, isXPlaneRunning: () => false);
        Assert.True(aircraft.Apply(fixture.Variant, check, [entry]).Succeeded);
        var generation = Assert.Single(fixture.Store.TryGetProductTarget(fixture.Variant)!.Backups,
            b => b.Operation == "AircraftUpdateFullDirectory");
        if (legacyBackup) generation.AircraftContentGeneration = null;
        else File.Delete(Path.Combine(generation.BackupPath, Path.GetFileName(fixture.Variant.AcfPath)));
        var before = File.ReadAllText(fixture.Store.StatePath);
        Assert.Throws<InvalidDataException>(() => new AircraftFullBaselineReplacement(fixture.Store)
            .Restore(fixture.Variant, generation, new List<string>()));
        Assert.Equal(before, File.ReadAllText(fixture.Store.StatePath));
        Assert.Equal("before\r\n", File.ReadAllText(fixture.TargetPath));
        Assert.Equal("official new baseline", File.ReadAllText(Path.Combine(Path.GetDirectoryName(fixture.TargetPath)!, "table.lua")));
        Assert.True(Directory.Exists(generation.BackupPath));
    }

    private static (AircraftUpstreamUpdateCheckResult Check, AircraftUpdatePackageCacheEntry Entry) CreateFullBaseline(Fixture fixture)
    {
        var path = Path.Combine(fixture.PackageDirectory, "baseline.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var (name, value) in new[] {
                (Path.GetFileName(fixture.Variant.AcfPath), "1200 Version\n"),
                ("plugins/xlua/scripts/shared.lua", "before\r\n"),
                ("plugins/xlua/scripts/table.lua", "official new baseline") })
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write(value);
            }
        }
        var package = new AircraftUpdatePackage("levelup-737ng", AircraftUpdatePackageKind.FullBaseline,
            new AircraftUpstreamVersion(2, 1, 50), "baseline.zip", "https://example.invalid/baseline.zip");
        var entry = new AircraftUpdatePackageCache(Path.Combine(fixture.PackageDirectory, "cache")).ImportZip(path, package);
        var check = new AircraftUpstreamUpdateCheckResult("Available", "Full baseline", "levelup-737ng", "",
            "V2.S1.50", "V2.S1.50", AircraftUpdatePlanAction.InstallBaselineAndCumulativePatch, "Install", false, [package], []);
        return (check, entry);
    }

    [Theory]
    [InlineData("missing-target")]
    [InlineData("missing-backup")]
    [InlineData("corrupt-backup")]
    public async Task Update_RecoversBackupOnlyWhenCurrentBytesProveOriginal(string damage)
    {
        using var fixture = Fixture.Create(singleComposableModule: true, includeCopyModule: true);
        var operation = new CompatibilityPackageOperation(fixture.Store, isXPlaneRunning: () => false);
        var path = Path.Combine(Path.GetDirectoryName(fixture.TargetPath)!, "table.lua");
        File.WriteAllText(path, "original");
        Assert.True((await operation.RunAsync(ContentPatchAction.Install, fixture.Variant,
            fixture.PackageDirectory, ["core", "table-payload"])).Succeeded);
        var file = Assert.Single(fixture.Store.Load().ContentInstallations.Values)
            .ContentComponents["levelup.compatibility"].Files.Single(f => f.RelativePath.EndsWith("table.lua"));
        File.WriteAllText(path, "original");
        if (damage == "missing-target") File.Delete(path);
        else if (damage == "missing-backup") File.Delete(file.BackupPath);
        else File.WriteAllText(file.BackupPath, "tampered");
        var result = await operation.RunAsync(ContentPatchAction.Update, fixture.Variant,
            fixture.PackageDirectory, ["core", "table-payload"]);
        if (damage == "missing-target")
        {
            Assert.False(result.Succeeded);
            Assert.False(File.Exists(path));
        }
        else
        {
            Assert.True(result.Succeeded, result.Message);
            var recovered = fixture.Store.TryGetContentInstallation(Path.GetDirectoryName(fixture.Variant.AcfPath)!)!
                .ContentComponents["levelup.compatibility"].Files.Single(f => f.RelativePath.EndsWith("table.lua"));
            Assert.NotEqual(file.BackupPath, recovered.BackupPath);
            Assert.Equal("original", File.ReadAllText(recovered.BackupPath));
            Assert.True(operation.Restore(fixture.Variant, fixture.PackageDirectory).Succeeded);
            Assert.Equal("original", File.ReadAllText(path));
        }
    }

    [Fact]
    public async Task Update_WhenManagedCopyFileChanged_KeepsStrictHashBlock()
    {
        using var fixture = Fixture.Create(singleComposableModule: true, includeCopyModule: true);
        var operation = new CompatibilityPackageOperation(fixture.Store, isXPlaneRunning: () => false);
        await operation.RunAsync(
            ContentPatchAction.Install,
            fixture.Variant,
            fixture.PackageDirectory,
            ["core", "table-payload"]);
        var copiedPath = Path.Combine(
            Path.GetDirectoryName(fixture.Variant.AcfPath)!,
            "plugins",
            "xlua",
            "scripts",
            "table.lua");
        File.WriteAllText(copiedPath, "user change\n", new UTF8Encoding(false));

        var updated = await operation.RunAsync(
            ContentPatchAction.Update,
            fixture.Variant,
            fixture.PackageDirectory,
            ["core", "table-payload"]);

        Assert.False(updated.Succeeded);
        Assert.Contains("Managed target changed after installation", updated.Message, StringComparison.Ordinal);
        Assert.Equal("user change\n", File.ReadAllText(copiedPath));
    }

    [Fact]
    public async Task InstallAndRestore_CopyFileModule_CreatesAndRemovesVerifiedPayload()
    {
        using var fixture = Fixture.Create(includeCopyModule: true);
        var operation = new CompatibilityPackageOperation(fixture.Store, isXPlaneRunning: () => false);
        var createdPath = Path.Combine(Path.GetDirectoryName(fixture.Variant.AcfPath)!, "plugins", "xlua", "scripts", "table.lua");

        var installed = await operation.RunAsync(
            ContentPatchAction.Install,
            fixture.Variant,
            fixture.PackageDirectory,
            ["core", "standard", "table-payload"]);

        Assert.True(installed.Succeeded);
        Assert.Equal("return { value = 42 }\n", File.ReadAllText(createdPath));
        var state = Assert.Single(fixture.Store.Load().ContentInstallations.Values).ContentComponents["levelup.compatibility"];
        Assert.Contains(state.Files, file => file.RelativePath == "plugins/xlua/scripts/table.lua" && !file.OriginalExisted);

        var restored = operation.Restore(fixture.Variant, fixture.PackageDirectory);

        Assert.True(restored.Succeeded);
        Assert.False(File.Exists(createdPath));
        Assert.Equal("before\r\n", File.ReadAllText(fixture.TargetPath));
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    [Fact]
    public async Task CatalogSourceUpdate_RecomposesNewPayloadAndRetainsOriginalBackup()
    {
        using var fixture = Fixture.Create(singleComposableModule: true, omitStructuralResultHashes: true);
        var manifestPath = Path.Combine(fixture.PackageDirectory, "package-manifest.json");
        var document = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        document["sources"] = JsonSerializer.SerializeToNode(new[] { new { packageId = "source.core", moduleId = "core", releaseTag = "v1.0.0", assetSha256 = new string('a', 64), repositoryUrl = "https://github.com/example/core" } });
        File.WriteAllText(manifestPath, document.ToJsonString());
        var operation = new CompatibilityPackageOperation(fixture.Store, () => false);
        Assert.True((await operation.RunAsync(ContentPatchAction.Install, fixture.Variant, fixture.PackageDirectory, [])).Succeeded);
        Assert.Equal("core\r\n", File.ReadAllText(fixture.TargetPath));
        var payload = document["modules"]![0]!["payloads"]![0]!;
        var payloadPath = Path.Combine(fixture.PackageDirectory, "modules", "core", payload["path"]!.GetValue<string>());
        var bytes = Encoding.UTF8.GetBytes(File.ReadAllText(payloadPath).Replace("\"core\"", "\"core updated\"", StringComparison.Ordinal));
        File.WriteAllBytes(payloadPath, bytes);
        payload["size"] = bytes.Length;
        payload["sha256"] = Sha256(bytes);
        document["packageVersion"] = "catalog-new-source";
        document["sources"]![0]!["releaseTag"] = "v1.1.0";
        File.WriteAllText(manifestPath, document.ToJsonString());
        var update = await operation.RunAsync(ContentPatchAction.Update, fixture.Variant, fixture.PackageDirectory, []);
        Assert.True(update.Succeeded, update.Message);
        Assert.Equal("core updated\r\n", File.ReadAllText(fixture.TargetPath));
        var state = Assert.Single(fixture.Store.Load().ContentInstallations.Values).ContentComponents["levelup.compatibility"];
        Assert.Equal("v1.1.0", Assert.Single(state.Sources).ReleaseTag);
        Assert.True(operation.Restore(fixture.Variant, fixture.PackageDirectory).Succeeded);
        Assert.Equal("before\r\n", File.ReadAllText(fixture.TargetPath));
    }

    [Fact]
    public async Task CatalogMigration_AfterCleanReinstall_ReappliesGroupAndRestoresCleanFile()
    {
        using var fixture = Fixture.Create(singleComposableModule: true, omitStructuralResultHashes: true);
        var manifestPath = Path.Combine(fixture.PackageDirectory, "package-manifest.json");
        var document = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        document["sources"] = JsonSerializer.SerializeToNode(new[]
        {
            new
            {
                packageId = "source.core",
                moduleId = "core",
                releaseTag = "v1.0.0",
                assetSha256 = new string('a', 64),
                repositoryUrl = "https://github.com/example/core"
            }
        });
        File.WriteAllText(manifestPath, document.ToJsonString());
        var cleanBytes = File.ReadAllBytes(fixture.TargetPath);
        var cleanBackup = Path.Combine(Path.GetDirectoryName(fixture.Store.StatePath)!, "clean-shared.lua");
        Directory.CreateDirectory(Path.GetDirectoryName(cleanBackup)!);
        File.WriteAllBytes(cleanBackup, cleanBytes);
        fixture.Store.UpdateContentAndProduct(fixture.Variant, (installation, product) =>
        {
            var legacy = new ContentComponentState
            {
                ComponentId = "source.core",
                PackageVersion = "1.0.0",
                EnabledModules = ["core"],
                Files =
                [
                    new()
                    {
                        RelativePath = "plugins/xlua/scripts/shared.lua",
                        TargetPath = fixture.TargetPath,
                        BackupPath = cleanBackup,
                        OriginalExisted = true,
                        OriginalSizeBytes = cleanBytes.LongLength,
                        OriginalSha256 = Sha256(cleanBytes),
                        InstalledSizeBytes = Encoding.UTF8.GetByteCount("core\r\n"),
                        InstalledSha256 = Sha256(Encoding.UTF8.GetBytes("core\r\n"))
                    }
                ]
            };
            installation.ContentComponents[legacy.ComponentId] = legacy;
            product.ContentComponents[legacy.ComponentId] = legacy;
        });
        var operation = new CompatibilityPackageOperation(fixture.Store, () => false);

        var applied = await operation.RunAsync(
            ContentPatchAction.Update,
            fixture.Variant,
            fixture.PackageDirectory,
            []);

        Assert.True(applied.Succeeded, applied.Message);
        Assert.Equal("core\r\n", File.ReadAllText(fixture.TargetPath));
        var installation = fixture.Store.TryGetContentInstallation(
            Path.GetDirectoryName(fixture.Variant.AcfPath)!)!;
        Assert.DoesNotContain("source.core", installation.ContentComponents.Keys);
        Assert.Contains("levelup.compatibility", installation.ContentComponents.Keys);
        Assert.True(operation.Restore(fixture.Variant, fixture.PackageDirectory).Succeeded);
        Assert.Equal("before\r\n", File.ReadAllText(fixture.TargetPath));
    }

    [Theory]
    [InlineData(true, "intact", false)]
    [InlineData(true, "missing", false)]
    [InlineData(true, "corrupt", false)]
    [InlineData(false, "missing", false)]
    [InlineData(false, "corrupt", false)]
    [InlineData(true, "missing", true)]
    public async Task OfficialBaseline_RecoversOldOwnershipWithoutOldBackups_AndPreservesRestore(
        bool migrateStandalone, string backupCondition, bool failWrite)
    {
        using var fixture = Fixture.Create(omitStructuralResultHashes: true);
        var operation = new CompatibilityPackageOperation(fixture.Store, () => false);
        Assert.True((await operation.RunAsync(ContentPatchAction.Install, fixture.Variant,
            fixture.PackageDirectory, ["core", "standard"])).Succeeded);
        var root = Path.GetDirectoryName(fixture.Variant.AcfPath)!;
        var oldComponent = fixture.Store.TryGetContentInstallation(root)!.ContentComponents["levelup.compatibility"];
        var oldBackup = Assert.Single(oldComponent.Files).BackupPath;
        if (backupCondition == "missing") File.Delete(oldBackup);
        if (backupCondition == "corrupt") File.WriteAllText(oldBackup, "corrupt historical backup");
        var oldBackupBytes = File.Exists(oldBackup) ? File.ReadAllBytes(oldBackup) : null;
        var manifestPath = Path.Combine(fixture.PackageDirectory, "package-manifest.json");
        if (migrateStandalone)
        {
            var document = JsonNode.Parse(File.ReadAllText(manifestPath))!;
            document["packageId"] = "new.group";
            document["sources"] = JsonSerializer.SerializeToNode(new[] { new {
                packageId = "levelup.compatibility", moduleId = "core", releaseTag = "v1.0.0",
                assetSha256 = new string('a', 64), repositoryUrl = "https://github.com/example/core" } });
            File.WriteAllText(manifestPath, document.ToJsonString());
        }
        var baseline = Encoding.UTF8.GetBytes("official release change\r\nbefore\r\n");
        File.WriteAllBytes(fixture.TargetPath, baseline);
        var catalog = new KnownAircraftBaselines([new("levelup-737ng", "test-release",
            "plugins/xlua/scripts/shared.lua", baseline.Length, Sha256(baseline), "https://example.org/release-manifest", new string('a', 64))]);
        var builder = new CompatibilityPackagePlanBuilder(fixture.Store, null, catalog);
        var package = CompatibilityPackageLoader.LoadDirectory(fixture.PackageDirectory);
        var stateBefore = File.ReadAllBytes(fixture.Store.StatePath);
        // Without independent evidence this is the reporter's disconnected-history failure.
        var rejected = await new CompatibilityPackagePlanBuilder(fixture.Store).BuildAsync(
            ContentPatchAction.Install, fixture.Variant, package, ["core", "standard"]);
        if (migrateStandalone) Assert.False(rejected.IsSafe);
        var plan = await builder.BuildAsync(ContentPatchAction.Install, fixture.Variant, package, ["core", "standard"]);
        Assert.True(plan.IsSafe, plan.StatusMessage);
        Assert.Contains(plan.Log, line => line.StartsWith("[RECOVER]"));
        Assert.Equal(stateBefore, File.ReadAllBytes(fixture.Store.StatePath));
        Assert.Equal(baseline, File.ReadAllBytes(fixture.TargetPath));
        var engine = new ContentPatchEngine(fixture.Store, () => false);
        if (failWrite)
        {
            Directory.CreateDirectory(Path.Combine(root, "write-failure"));
            plan = plan with { Mutations = [.. plan.Mutations, ContentPatchMutation.Write("write-failure", [1], "fault injection")] };
            Assert.NotNull(Record.Exception(() => engine.Execute(plan, fixture.Variant)));
            Assert.Equal(baseline, File.ReadAllBytes(fixture.TargetPath));
            Assert.Equal(stateBefore, File.ReadAllBytes(fixture.Store.StatePath));
        }
        else
        {
            Assert.True(engine.Execute(plan, fixture.Variant).Succeeded);
            var newState = Assert.Single(fixture.Store.TryGetContentInstallation(root)!.ContentComponents).Value;
            Assert.Equal(Sha256(baseline), Assert.Single(newState.Files).OriginalSha256);
            Assert.Equal(baseline, File.ReadAllBytes(newState.Files[0].BackupPath));
            var repeat = await builder.BuildAsync(ContentPatchAction.Update, fixture.Variant, package, ["core", "standard"]);
            Assert.True(repeat.IsSafe, repeat.StatusMessage);
            var repeated = engine.Execute(repeat, fixture.Variant);
            Assert.True(repeated.Succeeded);
            Assert.False(repeated.Changed);
            var optional = await builder.BuildAsync(ContentPatchAction.Update, fixture.Variant, package, ["core", "standard", "optional"]);
            Assert.True(optional.IsSafe, optional.StatusMessage);
            Assert.True(engine.Execute(optional, fixture.Variant).Succeeded);
            Assert.Equal("official release change\r\noptional\r\n", File.ReadAllText(fixture.TargetPath));
            Assert.True(engine.Restore(plan.Descriptor, fixture.Variant).Succeeded);
            Assert.Equal(baseline, File.ReadAllBytes(fixture.TargetPath));
            // Recovery is not dependent on the action name or old ownership remaining.
            var repair = await builder.BuildAsync(ContentPatchAction.Repair, fixture.Variant, package, ["core"]);
            Assert.True(engine.Execute(repair, fixture.Variant).Succeeded);
            var uninstall = await builder.BuildAsync(ContentPatchAction.Uninstall, fixture.Variant, package, []);
            Assert.True(engine.Execute(uninstall, fixture.Variant).Succeeded);
            Assert.Equal(baseline, File.ReadAllBytes(fixture.TargetPath));
        }
        Assert.Equal(oldBackupBytes is not null, File.Exists(oldBackup));
        if (oldBackupBytes is not null) Assert.Equal(oldBackupBytes, File.ReadAllBytes(oldBackup));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly DeclarativePatchManifestTests.TemporaryDirectory _directory;

        private Fixture(
            DeclarativePatchManifestTests.TemporaryDirectory directory,
            string packageDirectory,
            string targetPath,
            AircraftVariantViewAnalysis variant,
            State.ToolStateStore store)
        {
            _directory = directory;
            PackageDirectory = packageDirectory;
            TargetPath = targetPath;
            Variant = variant;
            Store = store;
        }

        public string PackageDirectory { get; }

        public string TargetPath { get; }

        public AircraftVariantViewAnalysis Variant { get; }

        public State.ToolStateStore Store { get; }

        public static Fixture Create(
            bool optionalRequiresStandard = false,
            bool includeCopyModule = false,
            bool omitStructuralResultHashes = false,
            bool singleComposableModule = false)
        {
            var directory = new DeclarativePatchManifestTests.TemporaryDirectory();
            var aircraftRoot = Path.Combine(directory.Path, "aircraft");
            var packageRoot = Path.Combine(directory.Path, "package");
            Directory.CreateDirectory(aircraftRoot);
            Directory.CreateDirectory(packageRoot);
            var targetPath = Path.Combine(aircraftRoot, "plugins", "xlua", "scripts", "shared.lua");
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllText(targetPath, "before\r\n", new UTF8Encoding(false));
            var acfPath = Path.Combine(aircraftRoot, "737_70NG.acf");
            File.WriteAllText(acfPath, "1200 Version\n");

            var modules = singleComposableModule
                ? new List<Dictionary<string, object?>>
                {
                    BuildModule(packageRoot, "core", "Core module", "required", true, 10, "before", "core", omitResultHash: true)
                }
                :
                [
                    BuildModule(packageRoot, "core", "Core module", "required", true, 10, "before", "core", omitResultHash: omitStructuralResultHashes),
                    BuildModule(packageRoot, "standard", "Standard module", "recommended", true, 20, "core", "standard", omitResultHash: omitStructuralResultHashes),
                    BuildModule(
                        packageRoot,
                        "optional",
                        "Optional module",
                        "optional",
                        false,
                        30,
                        "standard",
                        "optional",
                        optionalRequiresStandard ? ["standard"] : [],
                        omitStructuralResultHashes)
                ];
            if (includeCopyModule)
            {
                modules.Add(BuildCopyModule(packageRoot));
            }
            var manifest = new Dictionary<string, object?>
            {
                ["schemaVersion"] = 3,
                ["packageType"] = "compatibilityPackage",
                ["packageId"] = "levelup.compatibility",
                ["packageVersion"] = "1.0.0",
                ["repositoryUrl"] = "https://github.com/example/levelup-compatibility",
                ["aircraftFamily"] = "LevelUp 737NG Series",
                ["supportedProducts"] = new[] { "levelup-737ng" },
                ["restartRequired"] = true,
                ["supportedUpstreamReleases"] = new[] { "V2.S1.50" },
                ["modules"] = modules
            };
            File.WriteAllText(
                Path.Combine(packageRoot, "package-manifest.json"),
                JsonSerializer.Serialize(manifest),
                new UTF8Encoding(false));

            var variant = new AircraftVariantViewAnalysis(
                "levelup-737-700",
                "LevelUp 737-700",
                "LevelUp",
                acfPath,
                Path.ChangeExtension(acfPath, null) + "_prefs.txt",
                "test",
                "test",
                "V2.S1.50",
                "V2.S1.50",
                null,
                null,
                null,
                null,
                0,
                0,
                null,
                null,
                null,
                null,
                "test",
                "test",
                "test",
                "test");
            var store = TestToolStateStore.Create(Path.Combine(directory.Path, "state"));
            return new Fixture(directory, packageRoot, targetPath, variant, store);
        }

        public void AddConditionalHardeningModule(int schemaVersion = 5, string requiredModuleId = "optional")
        {
            const string moduleId = "hardening";
            const string payloadName = "hardening.json";
            var moduleRoot = Path.Combine(PackageDirectory, "modules", moduleId);
            Directory.CreateDirectory(moduleRoot);
            var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                format = "exact-text-replacements-v1",
                replacements = new[]
                {
                    new { name = "optional hardening", oldLines = new[] { "optional" }, newLines = new[] { "hardened" } }
                }
            }));
            File.WriteAllBytes(Path.Combine(moduleRoot, payloadName), payload);

            var manifestPath = Path.Combine(PackageDirectory, "package-manifest.json");
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
            manifest["schemaVersion"] = schemaVersion;
            manifest["modules"]!.AsArray().Add(JsonSerializer.SerializeToNode(new
            {
                moduleId,
                displayName = "Intentional hardening",
                description = "Hardens an optional functional module without selecting it.",
                policy = "optional",
                defaultEnabled = false,
                installationOrder = 40,
                requires = Array.Empty<string>(),
                conflictsWith = Array.Empty<string>(),
                payloads = new[]
                {
                    new { path = payloadName, size = payload.LongLength, sha256 = Sha256(payload) }
                },
                targets = new[]
                {
                    new
                    {
                        operation = "exact-text-replacements-v1",
                        payload = payloadName,
                        relativePath = "plugins/xlua/scripts/shared.lua",
                        sourceSha256 = Array.Empty<string>(),
                        whenModulesSelected = new[] { requiredModuleId }
                    }
                }
            }));
            File.WriteAllText(manifestPath, manifest.ToJsonString(), new UTF8Encoding(false));
        }

        public string CreateIndependentMarkedBlockPackage()
        {
            const string packageId = "independent.compatibility";
            const string moduleId = "independent";
            const string payloadName = "insert.json";
            var packageRoot = Path.Combine(_directory.Path, "independent-package");
            var moduleRoot = Path.Combine(packageRoot, "modules", moduleId);
            Directory.CreateDirectory(moduleRoot);
            var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                format = "insert-marked-block-v1",
                name = "Independent block",
                beginMarker = "-- BEGIN INDEPENDENT",
                endMarker = "-- END INDEPENDENT",
                contentLines = new[] { "independent" },
                anchorLines = new[] { "core" },
                position = "after"
            }));
            File.WriteAllBytes(Path.Combine(moduleRoot, payloadName), payload);
            var manifest = new Dictionary<string, object?>
            {
                ["schemaVersion"] = 3,
                ["packageType"] = "compatibilityPackage",
                ["packageId"] = packageId,
                ["packageVersion"] = "1.0.0",
                ["repositoryUrl"] = "https://github.com/example/independent-compatibility",
                ["aircraftFamily"] = "LevelUp 737NG Series",
                ["supportedProducts"] = new[] { "levelup-737ng" },
                ["restartRequired"] = true,
                ["supportedUpstreamReleases"] = new[] { "V2.S1.50" },
                ["modules"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["moduleId"] = moduleId,
                        ["displayName"] = "Independent module",
                        ["description"] = "Test independent module.",
                        ["policy"] = "required",
                        ["defaultEnabled"] = true,
                        ["installationOrder"] = 10,
                        ["requires"] = Array.Empty<string>(),
                        ["conflictsWith"] = Array.Empty<string>(),
                        ["payloads"] = new[]
                        {
                            new { path = payloadName, size = payload.LongLength, sha256 = Sha256(payload) }
                        },
                        ["targets"] = new[]
                        {
                            new
                            {
                                operation = "insert-marked-block-v1",
                                payload = payloadName,
                                relativePath = "plugins/xlua/scripts/shared.lua",
                                sourceSha256 = Array.Empty<string>()
                            }
                        }
                    }
                }
            };
            File.WriteAllText(
                Path.Combine(packageRoot, "package-manifest.json"),
                JsonSerializer.Serialize(manifest),
                new UTF8Encoding(false));
            return packageRoot;
        }

        public void Dispose() => _directory.Dispose();

        private static Dictionary<string, object?> BuildModule(
            string packageRoot,
            string moduleId,
            string displayName,
            string policy,
            bool defaultEnabled,
            int order,
            string oldLine,
            string newLine,
            string[]? requires = null,
            bool omitResultHash = false)
        {
            var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                format = "exact-text-replacements-v1",
                replacements = new[]
                {
                    new { name = moduleId, oldLines = new[] { oldLine }, newLines = new[] { newLine } }
                }
            }));
            var relativePayload = "patch.json";
            var moduleDirectory = Path.Combine(packageRoot, "modules", moduleId);
            Directory.CreateDirectory(moduleDirectory);
            File.WriteAllBytes(Path.Combine(moduleDirectory, relativePayload), payload);
            var source = Encoding.UTF8.GetBytes(oldLine + "\r\n");
            var result = Encoding.UTF8.GetBytes(newLine + "\r\n");
            return new Dictionary<string, object?>
            {
                ["moduleId"] = moduleId,
                ["displayName"] = displayName,
                ["description"] = $"Test {displayName}.",
                ["policy"] = policy,
                ["defaultEnabled"] = defaultEnabled,
                ["installationOrder"] = order,
                ["requires"] = requires ?? [],
                ["conflictsWith"] = Array.Empty<string>(),
                ["payloads"] = new[]
                {
                    new { path = relativePayload, size = payload.LongLength, sha256 = Sha256(payload) }
                },
                ["targets"] = new[]
                {
                    new
                    {
                        operation = "exact-text-replacements-v1",
                        payload = relativePayload,
                        relativePath = "plugins/xlua/scripts/shared.lua",
                        sourceSha256 = new[] { Sha256(source) },
                        resultSha256 = omitResultHash ? null : Sha256(result)
                    }
                }
            };
        }

        private static Dictionary<string, object?> BuildCopyModule(string packageRoot)
        {
            const string moduleId = "table-payload";
            const string relativePayload = "table.lua";
            var payload = Encoding.UTF8.GetBytes("return { value = 42 }\n");
            var moduleDirectory = Path.Combine(packageRoot, "modules", moduleId);
            Directory.CreateDirectory(moduleDirectory);
            File.WriteAllBytes(Path.Combine(moduleDirectory, relativePayload), payload);
            return new Dictionary<string, object?>
            {
                ["moduleId"] = moduleId,
                ["displayName"] = "Table payload",
                ["description"] = "Creates one manifest-owned Lua payload.",
                ["policy"] = "optional",
                ["defaultEnabled"] = false,
                ["installationOrder"] = 40,
                ["requires"] = Array.Empty<string>(),
                ["conflictsWith"] = Array.Empty<string>(),
                ["payloads"] = new[]
                {
                    new { path = relativePayload, size = payload.LongLength, sha256 = Sha256(payload) }
                },
                ["targets"] = new[]
                {
                    new
                    {
                        operation = "copy-file-v1",
                        payload = relativePayload,
                        relativePath = "plugins/xlua/scripts/table.lua",
                        sourceSha256 = Array.Empty<string>(),
                        resultSha256 = Sha256(payload)
                    }
                }
            };
        }
    }
}
