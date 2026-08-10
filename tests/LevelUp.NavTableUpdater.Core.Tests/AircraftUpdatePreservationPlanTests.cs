using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.State;
using LevelUp.NavTableUpdater.Core.Tools;
using LevelUp.NavTableUpdater.Core.Upstream;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class AircraftUpdatePreservationPlanTests
{
    [Fact]
    public void Capture_VerifiedAircraftComponent_ReturnsOnlyRecordedOwnedFiles()
    {
        using var fixture = new Fixture();

        var plan = AircraftUpdatePreservationPlan.Capture(fixture.Catalog, fixture.AircraftRoot, fixture.StateStore);

        Assert.NotNull(plan);
        Assert.Equal("1.3.7r3", plan.PackageVersion);
        Assert.Collection(
            plan.Files,
            file => Assert.Equal(
                Path.Combine("plugins", "xlua", "init.lua"),
                file.RelativePath));
        Assert.DoesNotContain(plan.Files, file => file.RelativePath.Contains("scripts", StringComparison.Ordinal));
    }

    [Fact]
    public void Capture_ChangedManagedFile_BlocksPreservation()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.InitPath, "locally changed");

        var error = Assert.Throws<InvalidDataException>(() =>
            AircraftUpdatePreservationPlan.Capture(fixture.Catalog, fixture.AircraftRoot, fixture.StateStore));

        Assert.Contains("Repair it before updating", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Capture_MissingManagedFile_BlocksWithRepairInstruction()
    {
        using var fixture = new Fixture();
        File.Delete(fixture.InitPath);

        var error = Assert.Throws<InvalidDataException>(() =>
            AircraftUpdatePreservationPlan.Capture(fixture.Catalog, fixture.AircraftRoot, fixture.StateStore));

        Assert.Contains("Repair it before updating", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DryRun_PackageOwnedXluaFile_IsClassifiedAsManagedPreservation()
    {
        using var fixture = new Fixture();
        var plan = Assert.IsType<AircraftUpdatePreservationPlan>(
            AircraftUpdatePreservationPlan.Capture(fixture.Catalog, fixture.AircraftRoot, fixture.StateStore));
        var package = fixture.CreatePackage(AircraftUpdatePackageKind.CumulativePatch, "stock runtime");

        var result = new AircraftUpdateDryRunAnalyzer().Analyze(
            fixture.AircraftRoot,
            [package],
            managedComponentPaths: plan.RelativePaths);

        Assert.True(result.Succeeded);
        Assert.Contains(
            result.Entries,
            entry => entry.RelativePath.EndsWith(Path.Combine("plugins", "xlua", "init.lua"), StringComparison.Ordinal)
                && entry.Action is AircraftUpdateDryRunEntryAction.PreserveManagedComponent);
    }

    [Fact]
    public void FullBaselineUpdate_ReappliesManagedComponentBeforeDirectoryActivation()
    {
        using var fixture = new Fixture();
        var plan = Assert.IsType<AircraftUpdatePreservationPlan>(
            AircraftUpdatePreservationPlan.Capture(fixture.Catalog, fixture.AircraftRoot, fixture.StateStore));
        var cacheEntry = fixture.CreatePackage(AircraftUpdatePackageKind.FullBaseline, "stock runtime");
        var check = new AircraftUpstreamUpdateCheckResult(
            "Update available",
            "Install full baseline",
            "zibo-737ng",
            "https://example.invalid/feed",
            "4.03.0",
            "4.05.35",
            AircraftUpdatePlanAction.InstallBaselineAndCumulativePatch,
            "Install baseline",
            false,
            [cacheEntry.Package],
            []);

        var result = new AircraftUpdateOperation(fixture.StateStore, isXPlaneRunning: () => false).Apply(
            fixture.Variant,
            check,
            [cacheEntry],
            preservationPlans: [plan]);

        Assert.True(result.Succeeded);
        Assert.Equal("optimized runtime", File.ReadAllText(fixture.InitPath));
        Assert.Equal("new acf", File.ReadAllText(fixture.AcfPath));
    }

    private sealed class Fixture : IDisposable
    {
        private const string OptimizedRuntime = "optimized runtime";

        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"aircraft-component-preservation-{Guid.NewGuid():N}");
            AircraftRoot = Path.Combine(Root, "B737-800X");
            AcfPath = Path.Combine(AircraftRoot, "b738_4k.acf");
            InitPath = Path.Combine(AircraftRoot, "plugins", "xlua", "init.lua");
            Directory.CreateDirectory(Path.Combine(AircraftRoot, "plugins", "xlua", "scripts", "B738.test"));
            File.WriteAllText(AcfPath, "old acf");
            File.WriteAllText(InitPath, OptimizedRuntime);
            File.WriteAllText(
                Path.Combine(AircraftRoot, "plugins", "xlua", "scripts", "B738.test", "B738.test.lua"),
                "aircraft script");
            StateStore = new ToolStateStore(Path.Combine(Root, "state"), Path.Combine(Root, "backups"));
            var bytes = Encoding.UTF8.GetBytes(OptimizedRuntime);
            StateStore.UpdateToolInstallation(AircraftRoot, Catalog.PackageId, state =>
            {
                state.TargetPath = Path.Combine(AircraftRoot, "plugins", "xlua");
                state.InstalledVersion = "1.3.7r3";
                state.Channel = "stable";
                state.InstalledFiles =
                [
                    new ToolInstalledFileState
                    {
                        RelativePath = "init.lua",
                        Size = bytes.LongLength,
                        Sha256 = Hash(bytes)
                    }
                ];
            });
            Variant = new AircraftVariantViewAnalysis(
                "zibo-737-800x-4k",
                "Zibo 737-800 4K",
                "zibo-737ng",
                AcfPath,
                Path.Combine(AircraftRoot, "b738_4k_prefs.txt"),
                "test",
                "test",
                "4.03.0",
                "4.03.0",
                "4.03.0",
                "120000",
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                "Reference CG",
                "Recognized",
                "Reference CG",
                "Reference CG");
        }

        public string Root { get; }

        public string AircraftRoot { get; }

        public string AcfPath { get; }

        public string InitPath { get; }

        public ToolStateStore StateStore { get; }

        public AircraftVariantViewAnalysis Variant { get; }

        public ContentPackageCatalogEntry Catalog { get; } = new()
        {
            PackageId = "wahltho.optimized-xlua",
            DisplayName = "Optimized XLua",
            Description = "Test component",
            Category = ContentPackageCategory.AircraftComponent,
            Activation = ContentPatchActivation.ExplicitOptIn,
            SupportedProducts = ["zibo-737ng", "levelup-737ng"],
            RepositoryUrl = "https://github.com/wahltho/XLua",
            RestartRequired = true,
            InstallScope = "aircraftInstallation",
            TargetPath = "plugins/xlua",
            SupportedChannels = ["stable", "beta"],
            Distribution = new ContentPackageDistribution
            {
                Kind = ContentPackageDistributionKind.GitHubToolRelease,
                ManifestAssetNamePattern = "Xlua.*-manifest.json",
                ManifestSchemaVersion = 1
            }
        };

        public AircraftUpdatePackageCacheEntry CreatePackage(AircraftUpdatePackageKind kind, string runtime)
        {
            var path = Path.Combine(Root, kind + ".zip");
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "B737-800X/b738_4k.acf", "new acf");
                WriteEntry(archive, "B737-800X/plugins/xlua/init.lua", runtime);
            }

            var version = new AircraftUpstreamVersion(4, 5, 35);
            var package = new AircraftUpdatePackage(
                "zibo-737ng",
                kind,
                version,
                Path.GetFileName(path),
                "https://example.invalid/package.zip");
            return new AircraftUpdatePackageCacheEntry(
                package,
                path,
                AircraftUpdatePackageCacheState.Imported,
                new FileInfo(path).Length,
                Hash(File.ReadAllBytes(path)));
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static void WriteEntry(ZipArchive archive, string path, string content)
        {
            var entry = archive.CreateEntry(path);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }

        private static string Hash(byte[] bytes) =>
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
