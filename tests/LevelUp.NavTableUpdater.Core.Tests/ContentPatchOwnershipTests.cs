using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.State;
using LevelUp.NavTableUpdater.Core.Manifest;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class ContentPatchOwnershipTests
{
    public static IEnumerable<object[]> CatalogPolicies() => ContentPackageCatalog.LoadBundled().OwnershipPolicies
        .Select(policy => new object[] { policy.PackageId });

    [Theory]
    [MemberData(nameof(CatalogPolicies))]
    public void CatalogPolicy_UnmanagedPatchEvidenceBlocksEvenWhenFilesMatch(string packageId)
    {
        using var directory = new DeclarativePatchManifestTests.TemporaryDirectory();
        var catalog = ContentPackageCatalog.LoadBundled();
        var policy = catalog.OwnershipPolicies.Single(p => p.PackageId == packageId);
        var marker = policy.MarkerNamespaces.FirstOrDefault(m => m.Blocks.Count > 0);
        string target;
        if (marker is not null)
        {
            target = marker.RelativePath;
            Write(directory.Path, target, marker.Blocks[0].BeginMarker + "\nbody\n" + marker.Blocks[0].EndMarker + "\n");
        }
        else if (policy.Signatures.Count > 0)
        {
            target = policy.Signatures[0].RelativePath;
            Write(directory.Path, target, policy.Signatures[0].Text + "\n");
        }
        else if (policy.PayloadPaths.Count > 0)
        {
            target = policy.PayloadPaths[0].Replace("**", "foreign.obj").Replace("*", "foreign");
            Write(directory.Path, target, "unmanaged payload");
        }
        else
        {
            target = policy.TargetPaths.First(p => !p.Contains('*'));
            Write(directory.Path, policy.StandaloneEvidencePaths[0], "{}");
        }
        Assert.NotNull(StandalonePatchOwnershipGuard.FindConflict(directory.Path, [target], null,
            catalog, product: policy.SupportedProducts[0]));
        Assert.Null(StandalonePatchOwnershipGuard.FindConflict(directory.Path, ["unrelated.txt"], null,
            catalog, product: policy.SupportedProducts[0]));
    }

    public static IEnumerable<object[]> MarkerPolicies() => ContentPackageCatalog.LoadBundled().OwnershipPolicies
        .SelectMany(policy => policy.MarkerNamespaces.Where(marker => marker.Blocks.Count > 0)
            .Select(marker => new object[] { policy.PackageId, marker.RelativePath, marker.Namespace }));

    [Theory]
    [MemberData(nameof(MarkerPolicies))]
    public void CatalogPolicy_UnknownBrokenAndDuplicateBlocksBlockManagedOwners(string id, string target, string name)
    {
        var catalog = ContentPackageCatalog.LoadBundled();
        var policy = catalog.OwnershipPolicies.Single(p => p.PackageId == id);
        var pair = policy.MarkerNamespaces.Single(m => m.RelativePath == target && m.Namespace == name).Blocks[0];
        var valid = pair.BeginMarker + "\nbody\n" + pair.EndMarker + "\n";
        foreach (var content in new[] { valid + "-- BEGIN " + name + " UNKNOWN\n", pair.BeginMarker + "\n", valid + valid,
            pair.EndMarker + "\n" + pair.BeginMarker + "\n", valid.ToLowerInvariant() })
        {
            using var directory = new DeclarativePatchManifestTests.TemporaryDirectory();
            Write(directory.Path, target, content);
            Write(directory.Path, "original", "stock");
            var owner = new ContentComponentState { ComponentId = id, Files = [new()
            {
                RelativePath = target, TargetPath = Path.Combine(directory.Path, target), OriginalExisted = true,
                BackupPath = Path.Combine(directory.Path, "original"), OriginalSha256 = OwnershipTestCatalog.Hash("stock"),
                OriginalSizeBytes = 5, InstalledSha256 = OwnershipTestCatalog.Hash(content),
                InstalledSizeBytes = Encoding.UTF8.GetByteCount(content)
            }] };
            var conflict = StandalonePatchOwnershipGuard.FindConflict(directory.Path, [target],
                new Dictionary<string, ContentComponentState> { [id] = owner }, catalog,
                product: policy.SupportedProducts[0]);
            Assert.NotNull(conflict);
            Assert.Equal(content, File.ReadAllText(Path.Combine(directory.Path, target)));
        }
    }

    [Fact]
    public void ManagedLifecycle_PreservesOriginalAndOtherPatchAcrossUpdateAndRestore()
    {
        using var fixture = new Fixture();
        var original = "-- unrelated user's edit\r\nstock\r\n";
        Write(fixture.Root, "script.lua", original);
        var first = fixture.Plan("1", "-- unrelated user's edit\r\nstock\r\n" + Fixture.Block("one"));
        Assert.True(fixture.Engine.Execute(first, fixture.Variant).Succeeded);
        var originalBackup = fixture.Store.TryGetContentInstallation(fixture.Root)!.ContentComponents["fixture.patch"].Files
            .Single(file => file.RelativePath == "script.lua").BackupPath;
        var second = fixture.Plan("2", "-- unrelated user's edit\r\nstock\r\n" + Fixture.Block("two"));
        Assert.True(fixture.Engine.Execute(second, fixture.Variant).Succeeded);
        Assert.True(fixture.Engine.Execute(fixture.Plan("2", "-- unrelated user's edit\r\nstock\r\n" + Fixture.Block("two")), fixture.Variant).Succeeded);
        Assert.Contains("-- unrelated user's edit", File.ReadAllText(Path.Combine(fixture.Root, "script.lua")));
        Assert.Equal(original, File.ReadAllText(originalBackup));
        var result = fixture.Engine.Restore(fixture.Descriptor, fixture.Variant);
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(original, File.ReadAllText(Path.Combine(fixture.Root, "script.lua")));
        Assert.False(File.Exists(Path.Combine(fixture.Root, "payload.lua")));
        Assert.Empty(fixture.Store.TryGetContentInstallation(fixture.Root)!.ContentComponents);
    }

    [Theory]
    [InlineData("payload")]
    [InlineData("receipt")]
    [InlineData("backup")]
    [InlineData("state")]
    [InlineData("catalog")]
    public void EvidenceChangedAfterReview_BlocksBeforeAnyWrite(string change)
    {
        using var fixture = new Fixture();
        Write(fixture.Root, "script.lua", "stock\n");
        Assert.True(fixture.Engine.Execute(fixture.Plan("1", Fixture.Block("one")), fixture.Variant).Succeeded);
        var plan = fixture.Plan("2", Fixture.Block("two"));
        switch (change)
        {
            case "payload": Write(fixture.Root, "payload-foreign.lua", "foreign"); break;
            case "receipt": Write(fixture.Root, ".fixture/state.json", "{}"); break;
            case "backup": File.WriteAllText(fixture.Store.TryGetContentInstallation(fixture.Root)!
                .ContentComponents["fixture.patch"].Files.Single(f => f.OriginalExisted).BackupPath, "corrupt"); break;
            case "state": fixture.Store.UpdateContentAndProduct(fixture.Variant, (state, _) =>
                state.ContentComponents["fixture.patch"].PackageVersion = "external"); break;
            case "catalog": fixture.Policy.RecoveryInstruction += " changed";
                fixture.Catalog = OwnershipTestCatalog.Create(fixture.Policy); break;
        }
        var source = File.ReadAllBytes(Path.Combine(fixture.Root, "script.lua"));
        var stateBytes = File.ReadAllBytes(fixture.Store.StatePath);
        var result = fixture.Engine.Execute(plan, fixture.Variant);
        Assert.False(result.Succeeded);
        Assert.Equal(source, File.ReadAllBytes(Path.Combine(fixture.Root, "script.lua")));
        Assert.Equal(stateBytes, File.ReadAllBytes(fixture.Store.StatePath));
    }

    [Fact]
    public void FinalNamespaceFailure_RollsBackAllMutationsAndLeavesStateUnchanged()
    {
        using var fixture = new Fixture();
        Write(fixture.Root, "script.lua", "stock\n");
        var plan = fixture.Plan("1", "-- BEGIN FIXTURE_PATCH UNKNOWN\n");
        Assert.True(plan.IsSafe, plan.StatusMessage);
        Assert.Throws<InvalidOperationException>(() => fixture.Engine.Execute(plan, fixture.Variant));
        Assert.Equal("stock\n", File.ReadAllText(Path.Combine(fixture.Root, "script.lua")));
        Assert.False(File.Exists(Path.Combine(fixture.Root, "payload.lua")));
        Assert.Empty(fixture.Store.Load().ContentInstallations);
    }

    [Fact]
    public void SavedPolicy_RestoreStillChecksStandaloneStateWhenLiveRuleChanges()
    {
        using var fixture = new Fixture();
        Write(fixture.Root, "script.lua", "stock\n");
        Assert.True(fixture.Engine.Execute(fixture.Plan("1", Fixture.Block("one")), fixture.Variant).Succeeded);
        fixture.Policy.StandaloneEvidencePaths = [".new-receipt/state.json"];
        fixture.Catalog = OwnershipTestCatalog.Create(fixture.Policy);
        Write(fixture.Root, ".fixture/state.json", "{}");
        Assert.False(fixture.Engine.Restore(fixture.Descriptor, fixture.Variant).Succeeded);
        Assert.Contains("one", File.ReadAllText(Path.Combine(fixture.Root, "script.lua")));
    }

    [Fact]
    public void MissingBackupOrHashOnlyState_DoesNotProveOwnership()
    {
        using var fixture = new Fixture();
        Write(fixture.Root, "script.lua", "stock\n");
        Assert.True(fixture.Engine.Execute(fixture.Plan("1", Fixture.Block("one")), fixture.Variant).Succeeded);
        var file = fixture.Store.TryGetContentInstallation(fixture.Root)!.ContentComponents["fixture.patch"].Files
            .Single(f => f.OriginalExisted);
        File.Delete(file.BackupPath);
        Assert.False(fixture.Plan("2", Fixture.Block("two")).IsSafe);
        Assert.False(fixture.Engine.Restore(fixture.Descriptor, fixture.Variant).Succeeded);
    }

    [Fact]
    public void CatalogContract_RoundTripsAndRequiresNewSchemaAndClientVersion()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Content", "content-package-catalog.json"));
        var catalog = ContentPackageCatalog.Parse(json, new Version(0, 28, 0));
        Assert.Equal(2, catalog.SchemaVersion);
        Assert.Equal(11, catalog.OwnershipPolicies.Count);
        Assert.Throws<InvalidDataException>(() => ContentPackageCatalog.Parse(json, new Version(0, 27, 0)));
        Assert.Throws<InvalidDataException>(() => ContentPackageCatalog.Parse(json.Replace("\"schemaVersion\": 2", "\"schemaVersion\": 1")));
        var document = new ContentPackageCatalogDocument { SchemaVersion = 2, CatalogVersion = catalog.CatalogVersion,
            MinimumToolkitVersion = catalog.MinimumToolkitVersion, Packages = catalog.Packages.ToList(),
            OwnershipPolicies = catalog.OwnershipPolicies.ToList() };
        var roundTrip = OwnershipTestCatalog.Parse(document);
        Assert.Equal(JsonSerializer.Serialize(catalog.OwnershipPolicies), JsonSerializer.Serialize(roundTrip.OwnershipPolicies));
        document.OwnershipPolicies.RemoveAt(0);
        Assert.Throws<InvalidDataException>(() => OwnershipTestCatalog.Parse(document));
    }

    [Fact]
    public async Task Group_UnselectedStandaloneScopeIsPreserved_ButSelectingItBlocks()
    {
        using var fixture = new Fixture();
        var bundled = ContentPackageCatalog.LoadBundled();
        var document = new ContentPackageCatalogDocument { SchemaVersion = 2,
            CatalogVersion = bundled.CatalogVersion, MinimumToolkitVersion = bundled.MinimumToolkitVersion,
            Packages = bundled.Packages.ToList(), OwnershipPolicies = bundled.OwnershipPolicies.ToList() };
        var modules = new List<CompatibilityPackageModule>();
        var sources = new List<ResolvedCatalogSource>();
        var members = new List<CatalogGroupMember>();
        var package = Path.Combine(fixture.Root, "test-package");
        foreach (var name in new[] { "active", "inactive" })
        {
            var id = "fixture." + name;
            var target = "fixture-files/" + name + ".lua";
            var repository = "https://github.com/example/" + name;
            var policy = OwnershipTestCatalog.Policy(id, repository, [target], [name]);
            policy.StandaloneEvidencePaths = [".fixture-" + name + "/state.json"];
            policy.PayloadPaths = [target];
            document.OwnershipPolicies.Add(policy);
            document.Packages.Add(new() { PackageId = id, DisplayName = name, Description = name,
                Category = ContentPackageCategory.CompatibilityPackage, Activation = ContentPatchActivation.Managed,
                RepositoryUrl = repository, SupportedProducts = ["levelup-737ng"], RestartRequired = true,
                Distribution = new() { Kind = ContentPackageDistributionKind.GitHubReleaseArchive,
                    AssetNamePattern = "fixture-*.zip", ManifestSchemaVersion = 3 } });
            members.Add(new() { PackageId = id, ModuleId = name, SourceFormat = "compatibility",
                ManifestPath = "package-manifest.json", AssetNamePattern = "fixture-*.zip",
                InstallationOrder = name == "active" ? 10 : 20,
                Policy = name == "active" ? CompatibilityModulePolicy.Required : CompatibilityModulePolicy.Optional });
            sources.Add(new() { PackageId = id, ModuleId = name, RepositoryUrl = repository,
                ReleaseTag = "v1.0.0", AssetSha256 = new string('a', 64) });
            var bytes = Encoding.UTF8.GetBytes("return true\n");
            Write(package, "modules/" + name + "/payload.lua", "return true\n");
            modules.Add(new() { ModuleId = name, DisplayName = name, Description = name,
                Policy = members[^1].Policy, DefaultEnabled = name == "active", InstallationOrder = members[^1].InstallationOrder,
                Payloads = [new() { Path = "payload.lua", Size = bytes.Length, Sha256 = OwnershipTestCatalog.Hash("return true\n") }],
                Targets = [new() { Operation = "copy-file-v1", Payload = "payload.lua", RelativePath = target,
                    ResultSha256 = OwnershipTestCatalog.Hash("return true\n") }] });
        }
        document.Packages.Add(new() { PackageId = "fixture.group", DisplayName = "group", Description = "group",
            Category = ContentPackageCategory.CompatibilityPackage, Activation = ContentPatchActivation.Managed,
            SupportedProducts = ["levelup-737ng"], RepositoryUrl = "https://github.com/example/group", RestartRequired = true,
            Distribution = new() { Kind = ContentPackageDistributionKind.CatalogGroup }, Members = members });
        var catalog = OwnershipTestCatalog.Parse(document);
        var manifest = new CompatibilityPackageManifest { SchemaVersion = 3, PackageType = "compatibilityPackage",
            PackageId = "fixture.group", PackageVersion = "1.0.0", RepositoryUrl = "https://github.com/example/group",
            AircraftFamily = "LevelUp", SupportedProducts = ["levelup-737ng"], RestartRequired = true,
            Modules = modules, Sources = sources };
        File.WriteAllText(Path.Combine(package, "package-manifest.json"), JsonSerializer.Serialize(manifest,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } }));
        Write(fixture.Root, "fixture-files/inactive.lua", "standalone");
        Write(fixture.Root, ".fixture-inactive/state.json", "{}");
        var operation = new CompatibilityPackageOperation(fixture.Store, () => false, () => catalog);
        var install = await operation.RunAsync(ContentPatchAction.Install, fixture.Variant, package, ["active"]);
        Assert.True(install.Succeeded, install.Message);
        var priorState = File.ReadAllBytes(fixture.Store.StatePath);
        var blocked = await operation.RunAsync(ContentPatchAction.Update, fixture.Variant, package, ["active", "inactive"]);
        Assert.False(blocked.Succeeded);
        Assert.Equal(priorState, File.ReadAllBytes(fixture.Store.StatePath));
        var restored = operation.Restore(fixture.Variant, package);
        Assert.True(restored.Succeeded, restored.Message);
        Assert.False(File.Exists(Path.Combine(fixture.Root, "fixture-files/active.lua")));
        Assert.Equal("standalone", File.ReadAllText(Path.Combine(fixture.Root, "fixture-files/inactive.lua")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(fixture.Root, ".fixture-inactive/state.json")));
    }

    private static void Write(string root, string path, string content)
    {
        var full = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, new UTF8Encoding(false));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly DeclarativePatchManifestTests.TemporaryDirectory _directory = new();
        public string Root => Path.Combine(_directory.Path, "aircraft");
        public ToolStateStore Store { get; }
        public AircraftVariantViewAnalysis Variant { get; }
        public PatchOwnershipPolicy Policy { get; }
        public ContentPackageCatalog Catalog { get; set; }
        public ContentPatchEngine Engine { get; }
        public ContentPatchDescriptor Descriptor { get; } = new("fixture.patch", "Fixture patch", "https://github.com/example/patch",
            new(ContentPatchActivation.ExplicitOptIn, new HashSet<ContentPatchTrigger>()), true);
        public Fixture()
        {
            Directory.CreateDirectory(Root);
            Store = TestToolStateStore.Create(Path.Combine(_directory.Path, "state"));
            var acf = Path.Combine(Root, "737_70NG.acf");
            Variant = new("test", "test", "LevelUp", acf, Path.ChangeExtension(acf, "_prefs.txt"), "test", "test",
                "1", null, null, null, null, null, 0, 0, null, null, null, null, "test", "test", "test", "test");
            Policy = OwnershipTestCatalog.Policy("fixture.patch", "https://github.com/example/patch", ["script.lua", "payload.lua", "payload-foreign.lua"]);
            Policy.StandaloneEvidencePaths = [".fixture/state.json"];
            Policy.PayloadPaths = ["payload.lua", "payload-foreign.lua"];
            Policy.MarkerNamespaces = [new() { RelativePath = "script.lua", Namespace = "FIXTURE_PATCH",
                Blocks = [new() { BeginMarker = "-- BEGIN FIXTURE_PATCH", EndMarker = "-- END FIXTURE_PATCH" }] }];
            Catalog = OwnershipTestCatalog.Create(Policy);
            Engine = new ContentPatchEngine(Store, () => false, () => Catalog);
        }
        public static string Block(string body) => "-- BEGIN FIXTURE_PATCH\n" + body + "\n-- END FIXTURE_PATCH\n";
        public ContentPatchPlan Plan(string version, string contents) => OwnershipTestCatalog.Bind(new(Descriptor, version,
            ContentPatchAction.Install, Root,
            [ContentPatchMutation.Write("script.lua", Encoding.UTF8.GetBytes(contents), "script"),
             ContentPatchMutation.Write("payload.lua", Encoding.UTF8.GetBytes("return true\n"), "payload")], [], true, "fixture"),
            Store, Variant, Catalog);
        public void Dispose() => _directory.Dispose();
    }
}
