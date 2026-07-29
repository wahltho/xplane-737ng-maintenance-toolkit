using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Upstream;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class LevelUpAircraftUpdatePackageTests : IDisposable
{
    private const string SevenZipFixtureBase64 = "N3q8ryccAAQFoTOjpgAAAAAAAAAWAAAAAAAAAJSGbkoBAAl1cGRhdGVkbmV3AOAA4QCQXQAAgTMHrg/Ox1yBCQqQD15xueAptWQdvKDlwAdSAyjVeI4QoJGpyBONhd9YL41mkvCqxrmNHHidkmorJCPH1srFx69gKRWm3LN1Ownf+/SRtW9GtlT0LN5cMf3dN+j+sZn7XqN+zrDrx6IBzLNR0uROS6I7LkEBYErb5qp9Q96xW8KXg97gnZt4BaTP8dVAAAAAFwYOAQmAmAAHCwEAASEhARgMgOIAAA==";
    private const string ArchiveSha256 = "5d6dec93a14c20b30dc44cc9252d59bc9f8633547b27f116bb56b35c59840186";
    private const string UpdatedSha256 = "27eb5e51506c911f6fc4bb345c0d9db6f60415fceab7c18e1e9b862637415777";
    private const string NewSha256 = "11507a0e2f5e69d5dfa40a62a1bd7b6ee57e6bcd85c67c9b8431b36fff21c437";
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"levelup-aircraft-update-tests-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("B738.sound", "B738.sound.lua", "2.S1.0")]
    [InlineData("LU_737NG.sound", "LU_737NG.sound.lua", "2.S1.50")]
    public void ReadLevelUpVersion_ReadsKnownRuntimeMarkerLayouts(
        string scriptFolder,
        string scriptFile,
        string expectedVersion)
    {
        var aircraftPath = CreateAircraft();
        var scriptPath = Path.Combine(aircraftPath, "plugins", "xlua", "scripts", scriptFolder, scriptFile);
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        File.WriteAllText(scriptPath, $"B738DR_lvlup_rel = \"{expectedVersion}\" --release version\n");

        var version = AircraftFileParser.ReadLevelUpVersion(aircraftPath);

        Assert.Equal(expectedVersion, version);
    }

    [Fact]
    public void ReadLevelUpVersion_DoesNotReadWithdrawnIntermediateRuntimeLayout()
    {
        var aircraftPath = CreateAircraft();
        WriteRuntimeMarker(aircraftPath, "B738.LevelUp.sound", "B738.LevelUp.sound.lua", "2.S1.0");

        var version = AircraftFileParser.ReadLevelUpVersion(aircraftPath);

        Assert.Null(version);
    }

    [Fact]
    public void ReadLevelUpVersion_WhenCurrentAndHistoricalMarkersExistPrefersCurrentLayout()
    {
        var aircraftPath = CreateAircraft();
        WriteRuntimeMarker(aircraftPath, "B738.sound", "B738.sound.lua", "2.S1.0");
        WriteRuntimeMarker(aircraftPath, "LU_737NG.sound", "LU_737NG.sound.lua", "2.S1.50");

        var version = AircraftFileParser.ReadLevelUpVersion(aircraftPath);

        Assert.Equal("2.S1.50", version);
    }

    [Fact]
    public void ReadLevelUpVersion_ReadsPublicV2S1PackageMarker()
    {
        var aircraftPath = CreateAircraft();
        File.WriteAllText(
            Path.Combine(aircraftPath, "LU & Zibo Version.txt"),
            "\uFEFFLevelUp 737NG Series XP12:\r\nVersion 2.S1.0\r\n\r\nZiboMod Version:\r\nVersion 4.05.08\r\n");

        var version = AircraftFileParser.ReadLevelUpVersion(aircraftPath);

        Assert.Equal("2.S1.0", version);
    }

    [Fact]
    public void ReadLevelUpVersion_DoesNotUseZiboVersionFromCombinedFile()
    {
        var aircraftPath = CreateAircraft();
        File.WriteAllText(
            Path.Combine(aircraftPath, "LU & Zibo Version.txt"),
            "ZiboMod Version:\r\nVersion 4.05.08\r\n");

        var version = AircraftFileParser.ReadLevelUpVersion(aircraftPath);

        Assert.Null(version);
    }

    [Fact]
    public void Loader_WithMatchingBaselineAliasBuildsIncrementalPlan()
    {
        var fixture = CreatePackageFixture();
        var variant = BuildVariant(fixture.AircraftPath, "2.S1.0");

        var selection = new LevelUpAircraftUpdatePackageLoader().Load(fixture.ManifestPath, variant);

        Assert.Equal(AircraftUpdatePlanAction.ApplyCumulativePatch, selection.UpdateCheck.Action);
        Assert.Equal("main-a87f675", selection.UpdateCheck.AvailableVersionDisplay);
        Assert.Equal("2.S1.0", selection.UpdateCheck.LocalVersionDisplay);
        Assert.Equal(fixture.ArchivePath, selection.ArchivePath);
        var package = Assert.Single(selection.UpdateCheck.RequiredPackages);
        Assert.Equal(ArchiveSha256, package.ExpectedSha256);
        Assert.Equal(220, package.ExpectedSizeBytes);
        Assert.NotNull(package.Manifest);
    }

    [Fact]
    public void Loader_ManifestAndArchiveSelectionsProduceEquivalentLocalPlan()
    {
        var fixture = CreatePackageFixture();
        var variant = BuildVariant(fixture.AircraftPath, "2.S1.0");
        var loader = new LevelUpAircraftUpdatePackageLoader();

        var fromManifest = loader.Load(fixture.ManifestPath, variant);
        var fromArchive = loader.Load(fixture.ArchivePath, variant);

        Assert.Equal(fromManifest.ManifestPath, fromArchive.ManifestPath);
        Assert.Equal(fromManifest.ArchivePath, fromArchive.ArchivePath);
        Assert.Equal(fromManifest.UpdateCheck.StateLabel, fromArchive.UpdateCheck.StateLabel);
        Assert.Equal(fromManifest.UpdateCheck.SourceUrl, fromArchive.UpdateCheck.SourceUrl);
        Assert.Equal(fromManifest.UpdateCheck.LocalVersionDisplay, fromArchive.UpdateCheck.LocalVersionDisplay);
        Assert.Equal(fromManifest.UpdateCheck.AvailableVersionDisplay, fromArchive.UpdateCheck.AvailableVersionDisplay);
        Assert.Equal(fromManifest.UpdateCheck.Action, fromArchive.UpdateCheck.Action);
        Assert.Equal(
            Assert.Single(fromManifest.UpdateCheck.RequiredPackages).FileName,
            Assert.Single(fromArchive.UpdateCheck.RequiredPackages).FileName);
        Assert.Equal(fromManifest.Package?.FileName, fromArchive.Package?.FileName);
        Assert.Equal(fromManifest.Package?.ExpectedSha256, fromArchive.Package?.ExpectedSha256);
    }

    [Fact]
    public void Loader_WithPublicV2S1RuntimeLayoutBuildsIncrementalPlan()
    {
        var fixture = CreatePackageFixture();
        WriteRuntimeMarker(fixture.AircraftPath, "B738.sound", "B738.sound.lua", "2.S1.0");
        var localVersion = Assert.IsType<string>(
            AircraftFileParser.ReadLevelUpVersion(fixture.AircraftPath));

        var selection = new LevelUpAircraftUpdatePackageLoader().Load(
            fixture.ManifestPath,
            BuildVariant(fixture.AircraftPath, localVersion));

        Assert.Equal(AircraftUpdatePlanAction.ApplyCumulativePatch, selection.UpdateCheck.Action);
        Assert.Equal("2.S1.0", selection.UpdateCheck.LocalVersionDisplay);
        Assert.Single(selection.UpdateCheck.RequiredPackages);
    }

    private static void WriteRuntimeMarker(
        string aircraftPath,
        string scriptFolder,
        string scriptFile,
        string version)
    {
        var scriptPath = Path.Combine(aircraftPath, "plugins", "xlua", "scripts", scriptFolder, scriptFile);
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        File.WriteAllText(scriptPath, $"B738DR_lvlup_rel = \"{version}\" --release version\n");
    }

    [Fact]
    public void Loader_WithWrongBaselineBlocksPatch()
    {
        var fixture = CreatePackageFixture();
        var variant = BuildVariant(fixture.AircraftPath, "2.S1.50");

        var selection = new LevelUpAircraftUpdatePackageLoader().Load(fixture.ArchivePath, variant);

        Assert.Equal(AircraftUpdatePlanAction.BaselineMismatch, selection.UpdateCheck.Action);
        Assert.Empty(selection.UpdateCheck.RequiredPackages);
        Assert.Null(selection.Package);
    }

    [Fact]
    public void CacheAndDryRun_VerifySevenZipManifestAndClassifyDeletes()
    {
        var fixture = CreatePackageFixture();
        var variant = BuildVariant(fixture.AircraftPath, "2.S1.0");
        File.WriteAllText(Path.Combine(fixture.AircraftPath, "existing.txt"), "original");
        File.WriteAllText(Path.Combine(fixture.AircraftPath, "retired.txt"), "retired");
        var selection = new LevelUpAircraftUpdatePackageLoader().Load(fixture.ManifestPath, variant);
        var package = Assert.IsType<AircraftUpdatePackage>(selection.Package);
        var cache = new AircraftUpdatePackageCache(Path.Combine(_root, "cache"));

        var imported = cache.ImportPackage(fixture.ArchivePath, package);
        var dryRun = new AircraftUpdateDryRunAnalyzer().Analyze(fixture.AircraftPath, [imported]);

        Assert.True(dryRun.Succeeded);
        Assert.Equal(1, dryRun.AddCount);
        Assert.Equal(1, dryRun.ReplaceCount);
        Assert.Equal(1, dryRun.DeleteCount);
        Assert.Contains(dryRun.Findings, finding => finding.Contains("Verified 2 archive payload", StringComparison.Ordinal));
    }

    [Fact]
    public void DryRun_WhenCancelled_StopsBeforeReadingPackages()
    {
        var fixture = CreatePackageFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            new AircraftUpdateDryRunAnalyzer().Analyze(fixture.AircraftPath, [], cancellation.Token));
    }

    [Fact]
    public void ApplyAndRestore_HandlesManifestWritesAndDeletionTransactionally()
    {
        var fixture = CreatePackageFixture();
        var variant = BuildVariant(fixture.AircraftPath, "2.S1.0");
        var existingPath = Path.Combine(fixture.AircraftPath, "existing.txt");
        var retiredPath = Path.Combine(fixture.AircraftPath, "retired.txt");
        var newPath = Path.Combine(fixture.AircraftPath, "new-file.txt");
        File.WriteAllText(existingPath, "original");
        File.WriteAllText(retiredPath, "retired");
        var selection = new LevelUpAircraftUpdatePackageLoader().Load(fixture.ManifestPath, variant);
        var package = Assert.IsType<AircraftUpdatePackage>(selection.Package);
        var cache = new AircraftUpdatePackageCache(Path.Combine(_root, "cache"));
        var imported = cache.ImportPackage(fixture.ArchivePath, package);
        var store = TestToolStateStore.Create(_root);
        var operation = new AircraftUpdateOperation(store, isXPlaneRunning: () => false);

        var applied = operation.Apply(variant, selection.UpdateCheck, [imported]);

        Assert.True(applied.Succeeded);
        Assert.Equal("updated", File.ReadAllText(existingPath));
        Assert.Equal("new", File.ReadAllText(newPath));
        Assert.False(File.Exists(retiredPath));
        var state = Assert.Single(store.Load().Aircraft.Values);
        Assert.Equal("main-a87f675", state.InstalledAircraftUpdateVersion);
        Assert.Contains(state.Backups, record => record.Operation == "AircraftUpdateDeletedFile" && record.SourcePath == retiredPath);

        var restored = operation.RestoreLatest(variant);

        Assert.True(restored.Succeeded);
        Assert.Equal("original", File.ReadAllText(existingPath));
        Assert.False(File.Exists(newPath));
        Assert.Equal("retired", File.ReadAllText(retiredPath));
    }

    [Fact]
    public void Apply_WhenCancelledBeforeValidation_DoesNotChangeAircraftFiles()
    {
        var fixture = CreatePackageFixture();
        var variant = BuildVariant(fixture.AircraftPath, "2.S1.0");
        var existingPath = Path.Combine(fixture.AircraftPath, "existing.txt");
        var retiredPath = Path.Combine(fixture.AircraftPath, "retired.txt");
        File.WriteAllText(existingPath, "original");
        File.WriteAllText(retiredPath, "retired");
        var selection = new LevelUpAircraftUpdatePackageLoader().Load(fixture.ManifestPath, variant);
        var package = Assert.IsType<AircraftUpdatePackage>(selection.Package);
        var cache = new AircraftUpdatePackageCache(Path.Combine(_root, "cache"));
        var imported = cache.ImportPackage(fixture.ArchivePath, package);
        var store = TestToolStateStore.Create(_root);
        var operation = new AircraftUpdateOperation(store, isXPlaneRunning: () => false);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            operation.Apply(variant, selection.UpdateCheck, [imported], cancellation.Token));

        Assert.Equal("original", File.ReadAllText(existingPath));
        Assert.Equal("retired", File.ReadAllText(retiredPath));
        Assert.False(File.Exists(Path.Combine(fixture.AircraftPath, "new-file.txt")));
        Assert.Empty(store.Load().Aircraft);
    }

    [Fact]
    public void Apply_WhenCancellationArrivesAfterWriteBoundary_CompletesTransaction()
    {
        var fixture = CreatePackageFixture();
        var variant = BuildVariant(fixture.AircraftPath, "2.S1.0");
        var existingPath = Path.Combine(fixture.AircraftPath, "existing.txt");
        var retiredPath = Path.Combine(fixture.AircraftPath, "retired.txt");
        File.WriteAllText(existingPath, "original");
        File.WriteAllText(retiredPath, "retired");
        var selection = new LevelUpAircraftUpdatePackageLoader().Load(fixture.ManifestPath, variant);
        var package = Assert.IsType<AircraftUpdatePackage>(selection.Package);
        var cache = new AircraftUpdatePackageCache(Path.Combine(_root, "cache"));
        var imported = cache.ImportPackage(fixture.ArchivePath, package);
        var operation = new AircraftUpdateOperation(TestToolStateStore.Create(_root), isXPlaneRunning: () => false);
        using var cancellation = new CancellationTokenSource();

        var result = operation.Apply(
            variant,
            selection.UpdateCheck,
            [imported],
            cancellation.Token,
            cancellation.Cancel);

        Assert.True(result.Succeeded);
        Assert.True(result.Changed);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal("updated", File.ReadAllText(existingPath));
        Assert.False(File.Exists(retiredPath));
        Assert.Equal("new", File.ReadAllText(Path.Combine(fixture.AircraftPath, "new-file.txt")));
    }

    [Fact]
    public void Cache_WhenImportCancelled_DoesNotCreateCachedPackage()
    {
        var fixture = CreatePackageFixture();
        var selection = new LevelUpAircraftUpdatePackageLoader().Load(
            fixture.ManifestPath,
            BuildVariant(fixture.AircraftPath, "2.S1.0"));
        var package = Assert.IsType<AircraftUpdatePackage>(selection.Package);
        var cache = new AircraftUpdatePackageCache(Path.Combine(_root, "cache"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            cache.ImportPackage(fixture.ArchivePath, package, cancellation.Token));

        Assert.False(File.Exists(cache.GetPackagePath(package)));
    }

    [Fact]
    public void Cache_WhenArchiveHashDiffersFromManifestRejectsImport()
    {
        var fixture = CreatePackageFixture(archiveSha256: new string('0', 64));
        var selection = new LevelUpAircraftUpdatePackageLoader().Load(fixture.ManifestPath, BuildVariant(fixture.AircraftPath, "2.S1.0"));
        var package = Assert.IsType<AircraftUpdatePackage>(selection.Package);
        var cache = new AircraftUpdatePackageCache(Path.Combine(_root, "cache"));

        var error = Assert.Throws<InvalidDataException>(() => cache.ImportPackage(fixture.ArchivePath, package));

        Assert.Contains("SHA-256", error.Message);
        Assert.False(File.Exists(cache.GetPackagePath(package)));
    }

    [Fact]
    public void DryRun_WhenPayloadHashDiffersFromManifestBlocksApply()
    {
        var fixture = CreatePackageFixture(updatedSha256: new string('f', 64));
        var selection = new LevelUpAircraftUpdatePackageLoader().Load(fixture.ManifestPath, BuildVariant(fixture.AircraftPath, "2.S1.0"));
        var package = Assert.IsType<AircraftUpdatePackage>(selection.Package);
        var cache = new AircraftUpdatePackageCache(Path.Combine(_root, "cache"));
        var imported = cache.ImportPackage(fixture.ArchivePath, package);

        var dryRun = new AircraftUpdateDryRunAnalyzer().Analyze(fixture.AircraftPath, [imported]);

        Assert.False(dryRun.Succeeded);
        Assert.Contains(dryRun.Entries, entry => entry.RelativePath == "existing.txt"
            && entry.Action == AircraftUpdateDryRunEntryAction.BlockedInvalidPackage
            && entry.Detail.Contains("SHA-256", StringComparison.Ordinal));
    }

    [Fact]
    public void ManifestParser_WhenDeletedPathTraversesBlocksPackage()
    {
        var fixture = CreatePackageFixture(deletedPaths: ["../outside.txt"]);

        var error = Assert.Throws<InvalidDataException>(() => AircraftUpdatePackageManifestParser.Load(fixture.ManifestPath));

        Assert.Contains("unsafe deleted path", error.Message);
    }

    [Fact]
    public void DryRun_WhenTargetSymlinkEscapesAircraftFolderBlocksPackage()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var fixture = CreatePackageFixture();
        var outsidePath = Path.Combine(_root, "outside.txt");
        File.WriteAllText(outsidePath, "outside");
        File.CreateSymbolicLink(Path.Combine(fixture.AircraftPath, "new-file.txt"), outsidePath);
        var selection = new LevelUpAircraftUpdatePackageLoader().Load(fixture.ManifestPath, BuildVariant(fixture.AircraftPath, "2.S1.0"));
        var package = Assert.IsType<AircraftUpdatePackage>(selection.Package);
        var cache = new AircraftUpdatePackageCache(Path.Combine(_root, "cache"));
        var imported = cache.ImportPackage(fixture.ArchivePath, package);

        var dryRun = new AircraftUpdateDryRunAnalyzer().Analyze(fixture.AircraftPath, [imported]);

        Assert.False(dryRun.Succeeded);
        Assert.Contains(dryRun.Entries, entry => entry.RelativePath == "new-file.txt"
            && entry.Action == AircraftUpdateDryRunEntryAction.BlockedUnsafePath
            && entry.Detail.Contains("symbolic link", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("outside", File.ReadAllText(outsidePath));
    }

    [Fact]
    public void DryRun_WhenAircraftRootIsSymlinkKeepsPackageInsideResolvedRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var fixture = CreatePackageFixture();
        File.WriteAllText(Path.Combine(fixture.AircraftPath, "existing.txt"), "original");
        var linkedAircraftPath = Path.Combine(_root, "Linked LevelUp Aircraft");
        Directory.CreateSymbolicLink(linkedAircraftPath, fixture.AircraftPath);
        var selection = new LevelUpAircraftUpdatePackageLoader().Load(fixture.ManifestPath, BuildVariant(linkedAircraftPath, "2.S1.0"));
        var package = Assert.IsType<AircraftUpdatePackage>(selection.Package);
        var cache = new AircraftUpdatePackageCache(Path.Combine(_root, "cache"));
        var imported = cache.ImportPackage(fixture.ArchivePath, package);

        var dryRun = new AircraftUpdateDryRunAnalyzer().Analyze(linkedAircraftPath, [imported]);

        Assert.True(dryRun.Succeeded);
        Assert.Equal(1, dryRun.AddCount);
        Assert.Equal(1, dryRun.ReplaceCount);
    }

    private PackageFixture CreatePackageFixture(
        string archiveSha256 = ArchiveSha256,
        string updatedSha256 = UpdatedSha256,
        IReadOnlyList<string>? deletedPaths = null)
    {
        var aircraftPath = CreateAircraft();
        var packageRoot = Path.Combine(_root, "package");
        Directory.CreateDirectory(packageRoot);
        var archivePath = Path.Combine(packageRoot, "fixture.7z");
        File.WriteAllBytes(archivePath, Convert.FromBase64String(SevenZipFixtureBase64));
        var manifestPath = Path.Combine(packageRoot, "fixture.manifest.json");
        var manifest = new
        {
            schemaVersion = 1,
            productId = "levelup-737ng",
            packageType = "cumulativePatch",
            baselineVersion = "2.S1.01",
            baselineAliases = new[] { "V2.S1", "2.S1.0" },
            targetVersion = "main-a87f675",
            releaseSequence = 1,
            contentRoot = "737NG Series_2.S1.01",
            files = new object[]
            {
                new { path = "existing.txt", operation = "replace", size = 7, sha256 = updatedSha256 },
                new { path = "new-file.txt", operation = "add", size = 3, sha256 = NewSha256 }
            },
            deletedPaths = deletedPaths ?? ["retired.txt"],
            archive = new { fileName = "fixture.7z", size = 220, sha256 = archiveSha256 }
        };
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));
        return new PackageFixture(aircraftPath, archivePath, manifestPath);
    }

    private string CreateAircraft()
    {
        var aircraftPath = Path.Combine(_root, "Aircraft", "737NG Series");
        Directory.CreateDirectory(aircraftPath);
        File.WriteAllText(Path.Combine(aircraftPath, "737_80NG.acf"), "");
        return aircraftPath;
    }

    private static AircraftVariantViewAnalysis BuildVariant(string aircraftPath, string localVersion) =>
        new(
            AircraftId: "levelup-737-800",
            DisplayName: "LevelUp 737-800",
            Family: LevelUpAircraftUpdatePackageLoader.Family,
            AcfPath: Path.Combine(aircraftPath, "737_80NG.acf"),
            PrefsPath: Path.Combine(aircraftPath, "737_80NG_prefs.txt"),
            Source: "test",
            SourceRef: "test",
            SourceVersion: localVersion,
            LocalVersion: localVersion,
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
            IdentityStatus: "Expected metadata",
            QuickViewStatus: "test",
            DefaultViewStatus: "test");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed record PackageFixture(string AircraftPath, string ArchivePath, string ManifestPath);
}
