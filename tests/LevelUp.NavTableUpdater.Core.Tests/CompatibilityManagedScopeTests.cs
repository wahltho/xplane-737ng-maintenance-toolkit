using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.Manifest;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class CompatibilityManagedScopeTests
{
    [Fact]
    public void Schema4Fields_RequireSchema4_WhileSchema3RemainsValid()
    {
        using var fixture = new Fixture();
        var json = File.ReadAllText(fixture.ManifestPath);
        Assert.Equal(4, CompatibilityPackageManifestParser.Parse(json).SchemaVersion);
        Assert.Contains("Schema 4", Assert.Throws<InvalidOperationException>(() =>
            CompatibilityPackageManifestParser.Parse(json.Replace("\"schemaVersion\":4", "\"schemaVersion\":3", StringComparison.Ordinal))).Message);

        var legacy = json.Replace("\"schemaVersion\":4", "\"schemaVersion\":3", StringComparison.Ordinal)
            .Replace($",\"retiredFiles\":[{{\"relativePath\":\"objects/GSE/old.obj\",\"sourceSha256\":[\"{fixture.OldHash}\"]}}]", "", StringComparison.Ordinal)
            .Replace(",\"managedScopes\":[{\"relativePath\":\"objects/GSE\",\"mode\":\"flatExclusive\"}]", "", StringComparison.Ordinal);
        Assert.Equal(3, CompatibilityPackageManifestParser.Parse(legacy).SchemaVersion);
    }

    [Fact]
    public async Task InstallRepeatRepairAndRestore_KeepExactScopeAndOriginalBytes()
    {
        using var fixture = new Fixture();
        var original = Encoding.UTF8.GetBytes("known legacy object");
        Directory.CreateDirectory(fixture.ScopePath);
        File.WriteAllBytes(fixture.OldPath, original);

        var installed = await fixture.Operation.RunAsync(ContentPatchAction.Install, fixture.Variant,
            fixture.PackagePath, ["gse"]);
        Assert.True(installed.Succeeded, installed.Message);
        Assert.False(File.Exists(fixture.OldPath));
        Assert.Equal("new object", File.ReadAllText(fixture.NewPath));
        var state = fixture.Component;
        Assert.True(Assert.Single(state.Scopes).OriginalDirectoryExisted);
        Assert.Equal(2, state.Files.Count);
        Assert.Null(state.Files.Single(file => file.RelativePath.EndsWith("old.obj", StringComparison.Ordinal)).InstalledSha256);

        var repeated = await fixture.Operation.RunAsync(ContentPatchAction.Update, fixture.Variant,
            fixture.PackagePath, ["gse"]);
        Assert.True(repeated.Succeeded, repeated.Message);
        Assert.False(repeated.Changed);

        File.WriteAllBytes(fixture.OldPath, original);
        var repaired = await fixture.Operation.RunAsync(ContentPatchAction.Repair, fixture.Variant,
            fixture.PackagePath, ["gse"]);
        Assert.True(repaired.Succeeded, repaired.Message);
        Assert.False(File.Exists(fixture.OldPath));

        var restored = fixture.Operation.Restore(fixture.Variant, fixture.PackagePath);
        Assert.True(restored.Succeeded, restored.Message);
        Assert.Equal(original, File.ReadAllBytes(fixture.OldPath));
        Assert.False(File.Exists(fixture.NewPath));
        Assert.True(Directory.Exists(fixture.ScopePath));
    }

    [Fact]
    public async Task FreshScope_DeselectionRemovesDirectoryAndRestoresState()
    {
        using var fixture = new Fixture(required: false);
        Assert.True((await fixture.Operation.RunAsync(ContentPatchAction.Install, fixture.Variant,
            fixture.PackagePath, ["gse"])).Succeeded);
        Assert.True(Directory.Exists(fixture.ScopePath));

        var deselected = await fixture.Operation.RunAsync(ContentPatchAction.Update, fixture.Variant,
            fixture.PackagePath, []);
        Assert.True(deselected.Succeeded, deselected.Message);
        Assert.False(Directory.Exists(fixture.ScopePath));
        Assert.False(fixture.Store.TryGetContentInstallation(fixture.AircraftPath)!
            .ContentComponents.ContainsKey("test.gse"));
    }

    [Fact]
    public async Task Update_RequiresExplicitOwnershipAndKnownCopySource()
    {
        using var fixture = new Fixture();
        Assert.True((await fixture.Operation.RunAsync(ContentPatchAction.Install, fixture.Variant,
            fixture.PackagePath, ["gse"])).Succeeded);

        fixture.WriteManifest("2.0.0", "updated object", [fixture.NewHash]);
        var update = await fixture.Operation.RunAsync(ContentPatchAction.Update, fixture.Variant,
            fixture.PackagePath, ["gse"]);
        Assert.True(update.Succeeded, update.Message);
        Assert.Equal("updated object", File.ReadAllText(fixture.NewPath));
        Assert.False(File.Exists(fixture.OldPath));

        fixture.WriteManifest("3.0.0", "third object", []);
        var unknownSource = await fixture.Operation.RunAsync(ContentPatchAction.Update, fixture.Variant,
            fixture.PackagePath, ["gse"]);
        Assert.False(unknownSource.Succeeded);
        Assert.Equal("updated object", File.ReadAllText(fixture.NewPath));

        fixture.WriteManifest("3.0.0", "third object", [Hash("updated object")], includeRetirement: false);
        var omission = await fixture.Operation.RunAsync(ContentPatchAction.Update, fixture.Variant,
            fixture.PackagePath, ["gse"]);
        Assert.False(omission.Succeeded);
        Assert.Contains("omitted without retirement", omission.Message);
    }

    [Theory]
    [InlineData("unknown-file")]
    [InlineData("unknown-retirement")]
    [InlineData("subdirectory")]
    public async Task UnknownScopeContent_BlocksBeforeWriting(string scenario)
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(fixture.ScopePath);
        switch (scenario)
        {
            case "unknown-file": File.WriteAllText(Path.Combine(fixture.ScopePath, "foreign.obj"), "foreign"); break;
            case "unknown-retirement": File.WriteAllText(fixture.OldPath, "modified old object"); break;
            case "subdirectory": Directory.CreateDirectory(Path.Combine(fixture.ScopePath, "nested")); break;
        }

        var result = await fixture.Operation.RunAsync(ContentPatchAction.Install, fixture.Variant,
            fixture.PackagePath, ["gse"]);
        Assert.False(result.Succeeded);
        Assert.False(File.Exists(fixture.NewPath));
    }

    [Fact]
    public async Task ScopeChangeAfterPlanningAndUnexpectedRetirementBeforeRestore_Block()
    {
        using var fixture = new Fixture();
        var plan = await fixture.Operation.PlanAsync(ContentPatchAction.Install, fixture.Variant,
            fixture.PackagePath, ["gse"]);
        Directory.CreateDirectory(fixture.ScopePath);
        File.WriteAllText(Path.Combine(fixture.ScopePath, "foreign.obj"), "foreign");
        var engine = new ContentPatchEngine(fixture.Store, () => false);
        var result = engine.Execute(plan, fixture.Variant);
        Assert.False(result.Succeeded);
        Assert.False(File.Exists(fixture.NewPath));

        File.Delete(Path.Combine(fixture.ScopePath, "foreign.obj"));
        Assert.True((await fixture.Operation.RunAsync(ContentPatchAction.Install, fixture.Variant,
            fixture.PackagePath, ["gse"])).Succeeded);
        File.WriteAllText(fixture.OldPath, "unknown reappearance");
        Assert.False(fixture.Operation.Restore(fixture.Variant, fixture.PackagePath).Succeeded);
        Assert.Equal("new object", File.ReadAllText(fixture.NewPath));
    }

    [Fact]
    public async Task FinalScopeValidationFailure_RollsBackFilesAndDirectory()
    {
        using var fixture = new Fixture();
        var plan = await fixture.Operation.PlanAsync(ContentPatchAction.Install, fixture.Variant,
            fixture.PackagePath, ["gse"]);
        Assert.True(plan.IsSafe);
        var badFinal = plan.FinalScopes.Select(scope => scope with
        {
            FileHashes = new Dictionary<string, string> { ["new.obj"] = new string('0', 64) }
        }).ToArray();
        var engine = new ContentPatchEngine(fixture.Store, () => false);

        Assert.Throws<InvalidOperationException>(() => engine.Execute(plan with { FinalScopes = badFinal }, fixture.Variant));
        Assert.False(Directory.Exists(fixture.ScopePath));
        Assert.Empty(fixture.Store.Load().ContentInstallations);
    }

    [Fact]
    public async Task ExistingIdenticalCopy_IsBackedUpAndRestorable()
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(fixture.ScopePath);
        File.WriteAllText(fixture.NewPath, "new object");
        var result = await fixture.Operation.RunAsync(ContentPatchAction.Install, fixture.Variant,
            fixture.PackagePath, ["gse"]);
        Assert.True(result.Succeeded, result.Message);
        var file = fixture.Component.Files.Single(state => state.RelativePath.EndsWith("new.obj", StringComparison.Ordinal));
        Assert.True(file.OriginalExisted);
        Assert.True(File.Exists(file.BackupPath));
        Assert.True(fixture.Operation.Restore(fixture.Variant, fixture.PackagePath).Succeeded);
        Assert.Equal("new object", File.ReadAllText(fixture.NewPath));
    }

    [Fact]
    public async Task ExistingCopyWithUnknownHash_Blocks()
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(fixture.ScopePath);
        File.WriteAllText(fixture.NewPath, "local edit");
        var result = await fixture.Operation.RunAsync(ContentPatchAction.Install, fixture.Variant,
            fixture.PackagePath, ["gse"]);
        Assert.False(result.Succeeded);
        Assert.Equal("local edit", File.ReadAllText(fixture.NewPath));
    }

    [Fact]
    public async Task ScopeSymlink_BlocksWithoutWriting()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture();
        var elsewhere = Path.Combine(Path.GetDirectoryName(fixture.AircraftPath)!, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.ScopePath)!);
        Directory.CreateSymbolicLink(fixture.ScopePath, elsewhere);
        var result = await fixture.Operation.RunAsync(ContentPatchAction.Install, fixture.Variant,
            fixture.PackagePath, ["gse"]);
        Assert.False(result.Succeeded);
        Assert.Empty(Directory.EnumerateFileSystemEntries(elsewhere));
    }

    private static string Hash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private sealed class Fixture : IDisposable
    {
        private readonly DeclarativePatchManifestTests.TemporaryDirectory _directory = new();
        private readonly bool _required;

        public Fixture(bool required = true)
        {
            _required = required;
            AircraftPath = Path.Combine(_directory.Path, "aircraft");
            PackagePath = Path.Combine(_directory.Path, "package");
            ScopePath = Path.Combine(AircraftPath, "objects", "GSE");
            OldPath = Path.Combine(ScopePath, "old.obj");
            NewPath = Path.Combine(ScopePath, "new.obj");
            ManifestPath = Path.Combine(PackagePath, "package-manifest.json");
            Directory.CreateDirectory(AircraftPath);
            Directory.CreateDirectory(PackagePath);
            var acf = Path.Combine(AircraftPath, "737_70NG.acf");
            File.WriteAllText(acf, "1200 Version\n");
            Variant = new AircraftVariantViewAnalysis(
                "levelup-737-700", "LevelUp 737-700", "LevelUp", acf,
                Path.ChangeExtension(acf, null) + "_prefs.txt", "test", "test",
                "V2.S1.50", "V2.S1.50", null, null, null, null, 0, 0,
                null, null, null, null, "test", "test", "test", "test");
            Store = TestToolStateStore.Create(Path.Combine(_directory.Path, "state"));
            Operation = new CompatibilityPackageOperation(Store, () => false);
            WriteManifest("1.0.0", "new object", []);
        }

        public string AircraftPath { get; }
        public string PackagePath { get; }
        public string ScopePath { get; }
        public string OldPath { get; }
        public string NewPath { get; }
        public string ManifestPath { get; }
        public string OldHash => Hash("known legacy object");
        public string NewHash => Hash("new object");
        public AircraftVariantViewAnalysis Variant { get; }
        public ToolStateStore Store { get; }
        public CompatibilityPackageOperation Operation { get; }
        public ContentComponentState Component => Store.TryGetContentInstallation(AircraftPath)!
            .ContentComponents["test.gse"];

        public void WriteManifest(string version, string content, string[] allowedCopySources,
            bool includeRetirement = true)
        {
            var modulePath = Path.Combine(PackagePath, "modules", "gse");
            Directory.CreateDirectory(modulePath);
            var payload = Encoding.UTF8.GetBytes(content);
            File.WriteAllBytes(Path.Combine(modulePath, "new.obj"), payload);
            var manifest = new
            {
                schemaVersion = 4,
                packageType = "compatibilityPackage",
                packageId = "test.gse",
                packageVersion = version,
                repositoryUrl = "https://github.com/example/gse",
                aircraftFamily = "LevelUp 737NG Series",
                supportedProducts = new[] { "levelup-737ng" },
                restartRequired = true,
                modules = new[]
                {
                    new
                    {
                        moduleId = "gse", displayName = "GSE", description = "GSE package",
                        policy = _required ? "required" : "optional", defaultEnabled = _required,
                        installationOrder = 10,
                        payloads = new[] { new { path = "new.obj", size = payload.Length,
                            sha256 = Hash(content) } },
                        targets = new[] { new { operation = "copy-file-v1", payload = "new.obj",
                            relativePath = "objects/GSE/new.obj", sourceSha256 = allowedCopySources,
                            resultSha256 = Hash(content) } },
                        retiredFiles = includeRetirement
                            ? new[] { new { relativePath = "objects/GSE/old.obj", sourceSha256 = new[] { OldHash } } }
                            : [],
                        managedScopes = new[] { new { relativePath = "objects/GSE", mode = "flatExclusive" } }
                    }
                }
            };
            File.WriteAllText(ManifestPath, JsonSerializer.Serialize(manifest));
        }

        public void Dispose() => _directory.Dispose();
    }
}
