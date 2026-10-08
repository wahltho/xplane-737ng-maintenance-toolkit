using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.State;
using LevelUp.NavTableUpdater.Core.Upstream;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class LevelUpCatchUpUpdateTests
{
    [Theory]
    [InlineData("2.S1.0")]
    [InlineData("2.S1.51B")]
    [InlineData(null)]
    public async Task PublicFeed_CatchUpAndRestorePreserveLocalContentAndPreviousOwnership(string? version)
    {
        using var fixture = new LevelUpCatchUpFixture(version);
        var ownedFile = "plugins/xlua/scripts/custom-patch.lua";
        fixture.Write(ownedFile, "old patched bytes");
        var oldComponent = new ContentComponentState
        {
            ComponentId = "test-patch", PackageVersion = "1.0", EnabledModules = ["required", "optional"],
            Files = [new ContentComponentFileState { RelativePath = ownedFile, TargetPath = Path.Combine(fixture.AircraftPath, ownedFile) }]
        };
        fixture.Store.UpdateContentAndProduct(fixture.Variant, (installation, product) =>
        {
            installation.ContentComponents["test-patch"] = oldComponent;
            product.ContentComponents["test-patch"] = oldComponent;
        });
        var before = fixture.Snapshot();
        var plan = await fixture.CheckAsync();
        var cache = fixture.Import(plan);
        var preserved = new AircraftUpdatePreservationPlan("test-runtime", "2.0",
            [new("plugins/xlua/mac_x64/xlua.xpl", [0, 255, 42], FileAttributes.Normal, null)]);
        var operation = new AircraftUpdateOperation(fixture.Store, isXPlaneRunning: () => false, catalogProvider: () => OwnershipTestCatalog.Published);

        var applied = operation.Apply(fixture.Variant, plan, cache, preservationPlans: [preserved]);

        Assert.True(applied.Succeeded, applied.Message);
        Assert.Equal("2.S1.51C", AircraftFileParser.ReadLevelUpVersion(fixture.AircraftPath));
        Assert.Equal("latest contents", File.ReadAllText(Path.Combine(fixture.AircraftPath, "new.txt")));
        Assert.False(File.Exists(Path.Combine(fixture.AircraftPath, "retired.txt")));
        Assert.False(File.Exists(Path.Combine(fixture.AircraftPath, ownedFile)));
        Assert.Equal(new byte[] { 0, 255, 42 }, File.ReadAllBytes(Path.Combine(fixture.AircraftPath, "plugins/xlua/mac_x64/xlua.xpl")));
        foreach (var path in new[] { "737_80NG_prefs.txt", "b738_config.txt", "liveries/My paint/objects/paint.bin" })
            Assert.Equal(before[path], fixture.Snapshot()[path]);
        var installation = fixture.Store.TryGetContentInstallation(fixture.AircraftPath)!;
        Assert.Empty(installation.ContentComponents);
        Assert.Equal(["required", "optional"], installation.PendingContentModules["test-patch"]);
        var generation = Assert.Single(fixture.Store.TryGetProductTarget(fixture.Variant)!.Backups,
            backup => backup.Operation == "AircraftUpdateFullDirectory");
        Assert.Equal(before, fixture.Snapshot(generation.BackupPath));
        Assert.Contains(applied.Log, line => line.Contains("full.7z", StringComparison.Ordinal));
        Assert.Contains(applied.Log, line => line.Contains("patch.7z", StringComparison.Ordinal));

        var restored = operation.RestoreLatest(fixture.Variant);

        Assert.True(restored.Succeeded, restored.Message);
        Assert.Equal(before, fixture.Snapshot());
        Assert.Equal(JsonSerializer.Serialize(oldComponent), JsonSerializer.Serialize(
            fixture.Store.TryGetContentInstallation(fixture.AircraftPath)!.ContentComponents["test-patch"]));
    }

    [Theory]
    [InlineData("newer-version", false)]
    [InlineData("newer-version", true)]
    [InlineData("custom-distribution", false)]
    [InlineData("custom-distribution", true)]
    [InlineData("metadata-version", false)]
    [InlineData("metadata-version", true)]
    [InlineData("invalid-metadata", true)]
    [InlineData("changed-product", true)]
    [InlineData("missing-acf", true)]
    [InlineData("xplane-started", true)]
    [InlineData("standalone", true)]
    public async Task CatchUp_SourceChangesBeforeStagingOrSwapBlockWithoutChangingAircraftOrState(string change, bool atSwap)
    {
        using var fixture = new LevelUpCatchUpFixture("2.S1.51B");
        if (change == "metadata-version")
            fixture.Write(AircraftMaintenanceMetadata.FileName, "{\"schemaVersion\":1,\"distributionVersion\":\"2.S1.51B\"}");
        var plan = await fixture.CheckAsync();
        var cache = fixture.Import(plan);
        fixture.Store.UpdateContentAndProduct(fixture.Variant, (_, _) => { });
        var stateBefore = File.ReadAllBytes(fixture.Store.StatePath);
        var running = false;
        SortedDictionary<string, string>? changedFiles = null;
        void ChangeSource()
        {
            switch (change)
            {
                case "newer-version": fixture.Write("version.txt", "2.S2"); break;
                case "custom-distribution": fixture.Write(AircraftMaintenanceMetadata.FileName, "{\"schemaVersion\":1,\"distribution\":\"private\"}"); break;
                case "metadata-version": fixture.Write(AircraftMaintenanceMetadata.FileName, "{\"schemaVersion\":1,\"distributionVersion\":\"2.S2\"}"); break;
                case "invalid-metadata": fixture.Write(AircraftMaintenanceMetadata.FileName, "invalid"); break;
                case "changed-product": fixture.Write(AircraftMaintenanceMetadata.FileName, "{\"schemaVersion\":1,\"aircraftFamily\":\"zibo-737ng\"}"); break;
                case "missing-acf": File.Delete(fixture.Variant.AcfPath); break;
                case "xplane-started": running = true; break;
                case "standalone": fixture.Write(".zibo-cpdlc-patch/state.json", "{}"); break;
            }
            changedFiles = fixture.Snapshot();
        }
        if (!atSwap) ChangeSource();
        var operation = new AircraftUpdateOperation(fixture.Store, isXPlaneRunning: () => running, catalogProvider: () => OwnershipTestCatalog.Published);

        var result = operation.Apply(fixture.Variant, plan, cache, writePhaseStarting: atSwap ? ChangeSource : null);

        Assert.False(result.Succeeded);
        Assert.Equal("Blocked", result.Status);
        Assert.Equal(changedFiles, fixture.Snapshot());
        Assert.Equal(stateBefore, File.ReadAllBytes(fixture.Store.StatePath));
        fixture.AssertNoTransactionFolders();
    }

    [Fact]
    public async Task CatchUp_UsesEffectiveMetadataVersionWhenRuntimeMarkerIsOlder()
    {
        using var fixture = new LevelUpCatchUpFixture("2.S1.0");
        fixture.Write(AircraftMaintenanceMetadata.FileName,
            "{\"schemaVersion\":1,\"aircraftFamily\":\"levelup-737ng\",\"distributionVersion\":\"2.S1.51B\"}");
        var variant = new AircraftViewAnalyzer().Analyze(fixture.AircraftPath).Variants
            .Single(v => v.AcfPath == fixture.Variant.AcfPath);
        Assert.Equal("2.S1.51B", variant.LocalVersion);
        var plan = await fixture.CheckAsync(variant);
        var before = fixture.Snapshot();
        var operation = new AircraftUpdateOperation(fixture.Store, isXPlaneRunning: () => false, catalogProvider: () => OwnershipTestCatalog.Published);
        var result = operation.Apply(variant, plan, fixture.Import(plan));
        Assert.True(result.Succeeded, result.Message);
        Assert.True(operation.RestoreLatest(variant).Succeeded);
        Assert.Equal(before, fixture.Snapshot());
    }

    [Fact]
    public async Task CatchUp_StateSaveFailureAfterActivationRestoresReadOnlyLocalFiles()
    {
        using var fixture = new LevelUpCatchUpFixture("2.S1.51B");
        fixture.Store.UpdateContentAndProduct(fixture.Variant, (_, _) => { });
        var readOnlyPath = Path.Combine(fixture.AircraftPath, "737_80NG_prefs.txt");
        File.SetAttributes(readOnlyPath, File.GetAttributes(readOnlyPath) | FileAttributes.ReadOnly);
        var before = fixture.Snapshot();
        var stateBefore = File.ReadAllBytes(fixture.Store.StatePath);
        var plan = await fixture.CheckAsync();
        var operation = new AircraftUpdateOperation(fixture.Store, isXPlaneRunning: () => false, catalogProvider: () => OwnershipTestCatalog.Published);
        var result = operation.Apply(fixture.Variant, plan, fixture.Import(plan), writePhaseStarting: () =>
            File.WriteAllText(Path.Combine(fixture.Store.RootPath, "aircraft-move.json"), "state writes now blocked"));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Log, line => line.Contains("[ROLLBACK] Previous aircraft directory restored", StringComparison.Ordinal));
        Assert.Equal(before, fixture.Snapshot());
        Assert.True(File.GetAttributes(readOnlyPath).HasFlag(FileAttributes.ReadOnly));
        Assert.Equal(stateBefore, File.ReadAllBytes(fixture.Store.StatePath));
        fixture.AssertNoTransactionFolders();
    }

    [Fact]
    public async Task CatchUp_CancellationBeforeSwapLeavesAircraftAndStateUntouched()
    {
        using var fixture = new LevelUpCatchUpFixture("2.S1.51B");
        var plan = await fixture.CheckAsync();
        var cache = fixture.Import(plan);
        var before = fixture.Snapshot();
        var runningChecks = 0;
        using var cancellation = new CancellationTokenSource();
        var operation = new AircraftUpdateOperation(fixture.Store, isXPlaneRunning: () =>
        {
            if (++runningChecks == 2) cancellation.Cancel();
            return false;
        }, catalogProvider: () => OwnershipTestCatalog.Published);
        Assert.Throws<OperationCanceledException>(() => operation.Apply(fixture.Variant, plan, cache, cancellation.Token));
        Assert.Equal(before, fixture.Snapshot());
        Assert.False(File.Exists(fixture.Store.StatePath));
        fixture.AssertNoTransactionFolders();
    }

    [Fact]
    public async Task CatchUp_ActivationFailureRestoresCompleteAircraftAndOwnership()
    {
        using var fixture = new LevelUpCatchUpFixture("2.S1.0");
        fixture.Store.UpdateContentAndProduct(fixture.Variant, (_, _) => { });
        var before = fixture.Snapshot();
        var stateBefore = File.ReadAllBytes(fixture.Store.StatePath);
        var plan = await fixture.CheckAsync();
        var cache = fixture.Import(plan);
        var operation = new AircraftFullBaselineReplacement(fixture.Store,
            afterTargetMoved: () => throw new IOException("Injected activation failure"), isXPlaneRunning: () => false, catalogProvider: () => OwnershipTestCatalog.Published);

        Assert.Throws<IOException>(() => operation.Apply(fixture.Variant, plan, cache, CancellationToken.None,
            null, [], new List<string>()));

        Assert.Equal(before, fixture.Snapshot());
        Assert.Equal(stateBefore, File.ReadAllBytes(fixture.Store.StatePath));
        fixture.AssertNoTransactionFolders();
    }

    [Fact]
    public async Task CatchUp_CustomDistributionIsReviewOnly()
    {
        using var fixture = new LevelUpCatchUpFixture("2.S1.0");
        fixture.Write(AircraftMaintenanceMetadata.FileName, "{\"schemaVersion\":1,\"distribution\":\"private\"}");
        var plan = await fixture.CheckAsync();
        Assert.True(plan.IsCustomDistribution);
        Assert.False(plan.HasUpdate);
        Assert.Empty(plan.RequiredPackages);
    }
}

// Small real archives plus the production release-index parser exercise planning,
// staging, activation and restore without downloading the public aircraft.
internal sealed class LevelUpCatchUpFixture : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lu-catchup-{Guid.NewGuid():N}");
    private readonly Dictionary<string, string> _archives = new(StringComparer.Ordinal);
    public string AircraftPath => Path.Combine(_root, "Aircraft", "737NG Series");
    public AircraftVariantViewAnalysis Variant { get; }
    public ToolStateStore Store { get; }
    public Dictionary<string, byte[]> Responses { get; } = new(StringComparer.Ordinal);

    public LevelUpCatchUpFixture(string? version)
    {
        Directory.CreateDirectory(AircraftPath);
        var references = AircraftReferenceCatalog.All.Where(r => r.Family == LevelUpAircraftUpdatePackageLoader.Family).ToArray();
        var files = references.ToDictionary(r => r.AcfFileName, r =>
            $"1200 Version\nP acf/_name {r.ExpectedName}\nP acf/_descrip {r.ExpectedDescription}\nP acf/_studio {r.ExpectedStudioContains}\nP acf/_cgY 0\nP acf/_cgZ 0\n");
        foreach (var file in files) Write(file.Key, file.Value);
        if (version is not null) Write("version.txt", version);
        Write("plugins/zibomod/plugin.xpl", "old plugin");
        Write("737_80NG_prefs.txt", "user views\r\n");
        Write("b738_config.txt", "user settings\r\n");
        Write("liveries/My paint/objects/paint.bin", "user paint\0\u00ff");
        Write("old-only.txt", "previous aircraft file");
        var selected = references.Single(r => r.AcfFileName == "737_80NG.acf");
        Variant = new(selected.AircraftId, selected.DisplayName, selected.Family,
            Path.Combine(AircraftPath, selected.AcfFileName), Path.Combine(AircraftPath, "737_80NG_prefs.txt"),
            "test", "test", version ?? "", version, null, null, null, null, 0, 0, null, null, null, null,
            "test", "Expected metadata", "test", "test");
        Store = TestToolStateStore.Create(_root);
        files["version.txt"] = "2.S1.50C";
        files["plugins/zibomod/plugin.xpl"] = "baseline plugin";
        files["737_80NG_prefs.txt"] = "default views";
        files["b738_config.txt"] = "default settings";
        files["retired.txt"] = "baseline file retired by delta";
        var full = AddPackage("full", "v2.S1.50C", 3, files);
        var patch = AddPackage("cumulativePatch", "v2.S1.51C", 4,
            new() { ["version.txt"] = "2.S1.51C", ["new.txt"] = "latest contents" }, ["retired.txt"]);
        Responses["https://github.com/petrolpram/737NG-Updates/releases/download/v2.S1.50C/release-index.json"] =
            Index("v2.S1.50C", 3, full, null);
        Responses[LevelUpGitHubReleaseIndexSource.DefaultIndexUrl] = Index("v2.S1.51C", 4, patch, "v2.S1.50C");
    }

    public async Task<AircraftUpstreamUpdateCheckResult> CheckAsync(AircraftVariantViewAnalysis? variant = null)
    {
        using var client = new HttpClient(new Handler(Responses));
        return await new LevelUpReleaseUpdateChecker(new LevelUpGitHubReleaseIndexSource(client, new Version(0, 23, 0)))
            .CheckAsync(variant ?? Variant);
    }

    public AircraftUpdatePackageCacheEntry[] Import(AircraftUpstreamUpdateCheckResult plan)
    {
        var cache = new AircraftUpdatePackageCache(Path.Combine(_root, "cache"));
        return plan.RequiredPackages.Select(p => cache.ImportPackage(_archives[p.FileName], p)).ToArray();
    }

    public void Write(string relativePath, string contents)
    {
        var path = Path.Combine(AircraftPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents, new UTF8Encoding(false));
    }

    public SortedDictionary<string, string> Snapshot(string? root = null) => new(
        Directory.EnumerateFiles(root ?? AircraftPath, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(root ?? AircraftPath, path).Replace('\\', '/'),
                path => Hash(File.ReadAllBytes(path))), StringComparer.Ordinal);

    public void AssertNoTransactionFolders() => Assert.DoesNotContain(
        Directory.EnumerateDirectories(Path.GetDirectoryName(AircraftPath)!),
        path => Path.GetFileName(path).Contains("toolkit-", StringComparison.Ordinal));

    private object AddPackage(string type, string version, int sequence, Dictionary<string, string> files,
        string[]? deleted = null)
    {
        var name = type == "full" ? "full.7z" : "patch.7z";
        var path = Path.Combine(_root, name);
        // Archive content is detected by its signature; this compact ZIP fixture
        // shares the same verified extraction path as the public 7z packages.
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            foreach (var file in files)
            {
                using var stream = archive.CreateEntry("737NG Series/" + file.Key).Open();
                stream.Write(Encoding.UTF8.GetBytes(file.Value));
            }
        _archives[name] = path;
        var bytes = File.ReadAllBytes(path);
        var prefix = $"https://github.com/petrolpram/737NG-Updates/releases/download/{version}/";
        Responses[prefix + name] = bytes;
        var manifestName = name.Replace(".7z", ".manifest.json");
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, productId = "levelup-737ng", packageType = type, releaseVersion = version,
            targetVersion = version, releaseSequence = sequence, contentRoot = "737NG Series",
            baselineVersion = type == "full" ? null : "v2.S1.50C", baselineAliases = Array.Empty<string>(),
            files = files.Select(file => new { path = file.Key, operation = "replace", size = Encoding.UTF8.GetByteCount(file.Value), sha256 = Hash(Encoding.UTF8.GetBytes(file.Value)) }).ToArray(),
            deletedPaths = deleted ?? [], archive = new { fileName = name, size = bytes.LongLength, sha256 = Hash(bytes) }
        });
        Responses[prefix + manifestName] = manifest;
        return new
        {
            packageType = type, releaseVersion = version, baselineVersion = type == "full" ? null : "v2.S1.50C",
            baselineAliases = Array.Empty<string>(), archiveFile = name, archiveSize = bytes.LongLength, archiveSha256 = Hash(bytes),
            manifestFile = manifestName, manifestSha256 = Hash(manifest)
        };
    }

    private static byte[] Index(string version, int sequence, object package, string? baselineTag) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, minimumToolkitVersion = "0.13.8", productId = "levelup-737ng",
            repository = "petrolpram/737NG-Updates", releaseVersion = version, releaseSequence = sequence,
            releaseTag = version, releaseChannel = "stable", baselineReleaseTag = baselineTag, packages = new[] { package }
        });

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    public void Dispose()
    {
        foreach (var path in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        Directory.Delete(_root, recursive: true);
    }
    private sealed class Handler(IReadOnlyDictionary<string, byte[]> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responses.TryGetValue(request.RequestUri!.AbsoluteUri, out var bytes)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
