using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.Manifest;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class CompatibilityPackageTests
{
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

        public static Fixture Create(bool optionalRequiresStandard = false, bool includeCopyModule = false)
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

            var modules = new List<Dictionary<string, object?>>
            {
                BuildModule(packageRoot, "core", "Core module", "required", true, 10, "before", "core"),
                BuildModule(packageRoot, "standard", "Standard module", "recommended", true, 20, "core", "standard"),
                BuildModule(
                    packageRoot,
                    "optional",
                    "Optional module",
                    "optional",
                    false,
                    30,
                    "standard",
                    "optional",
                    optionalRequiresStandard ? ["standard"] : [])
            };
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
            string[]? requires = null)
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
                        resultSha256 = Sha256(result)
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
