using System.IO.Compression;
using System.Text;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Upstream;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class AircraftFreshInstallOperationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"xplane-737ng-fresh-install-tests-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("zibo-737ng", "B737-800X")]
    [InlineData("levelup-737ng", "737NG Series")]
    public void Apply_InstallsStructurallyValidFullPackageIntoEmptyAircraftFolder(
        string productId,
        string targetName)
    {
        var xPlaneRoot = CreateXPlaneRoot();
        var product = AircraftFreshInstallProduct.All.Single(item => item.ProductId == productId);
        var package = CreatePackage(product, validStructure: true);
        var target = Path.Combine(xPlaneRoot, "Aircraft", targetName);
        var result = new AircraftFreshInstallOperation(isXPlaneRunning: () => false).Apply(
            xPlaneRoot,
            target,
            product,
            BuildPlan(product, package.Package),
            [package]);

        Assert.True(result.Succeeded);
        Assert.True(result.Changed);
        Assert.True(Directory.Exists(target));
        Assert.True(File.Exists(Path.Combine(target, "plugins", "test.txt")));
        Assert.True(File.Exists(Path.Combine(target, AircraftMaintenanceMetadata.FileName)));
        Assert.DoesNotContain(
            Directory.EnumerateDirectories(Path.Combine(xPlaneRoot, "Aircraft")),
            path => Path.GetFileName(path).Contains("toolkit-install", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_ZiboFullBaselineAndCumulativePatch_ActivatesPatchedImage()
    {
        var xPlaneRoot = CreateXPlaneRoot();
        var product = AircraftFreshInstallProduct.All.Single(item => item.ProductId == AircraftProductIds.Zibo737Ng);
        var fullPackage = CreatePackage(product, validStructure: true);
        var patchPackage = CreatePatchPackage(product);
        var target = Path.Combine(xPlaneRoot, "Aircraft", "Fresh B737-800X");

        var result = new AircraftFreshInstallOperation(isXPlaneRunning: () => false).Apply(
            xPlaneRoot,
            target,
            product,
            BuildPlan(product, fullPackage.Package, patchPackage.Package),
            [fullPackage, patchPackage]);

        Assert.True(result.Succeeded);
        Assert.Equal("patched", File.ReadAllText(Path.Combine(target, "plugins", "patch-state.txt")));
    }

    [Fact]
    public void Apply_WhenDestinationExists_BlocksWithoutChangingIt()
    {
        var xPlaneRoot = CreateXPlaneRoot();
        var product = AircraftFreshInstallProduct.All.Single(item => item.ProductId == AircraftProductIds.Zibo737Ng);
        var package = CreatePackage(product, validStructure: true);
        var target = Path.Combine(xPlaneRoot, "Aircraft", product.DefaultFolderName);
        Directory.CreateDirectory(target);
        var sentinel = Path.Combine(target, "keep.txt");
        File.WriteAllText(sentinel, "unchanged");

        var result = new AircraftFreshInstallOperation(isXPlaneRunning: () => false).Apply(
            xPlaneRoot,
            target,
            product,
            BuildPlan(product, package.Package),
            [package]);

        Assert.False(result.Succeeded);
        Assert.Equal("unchanged", File.ReadAllText(sentinel));
    }

    [Fact]
    public void Apply_WhenPackageStructureIsInvalid_RemovesStageAndLeavesNoTarget()
    {
        var xPlaneRoot = CreateXPlaneRoot();
        var product = AircraftFreshInstallProduct.All.Single(item => item.ProductId == AircraftProductIds.LevelUp737Ng);
        var package = CreatePackage(product, validStructure: false);
        var target = Path.Combine(xPlaneRoot, "Aircraft", product.DefaultFolderName);

        var result = new AircraftFreshInstallOperation(isXPlaneRunning: () => false).Apply(
            xPlaneRoot,
            target,
            product,
            BuildPlan(product, package.Package),
            [package]);

        Assert.False(result.Succeeded);
        Assert.False(Directory.Exists(target));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(xPlaneRoot, "Aircraft")));
    }

    [Fact]
    public void Apply_WhenAcfIdentityDoesNotMatchProduct_RemovesStageAndLeavesNoTarget()
    {
        var xPlaneRoot = CreateXPlaneRoot();
        var product = AircraftFreshInstallProduct.All.Single(item => item.ProductId == AircraftProductIds.LevelUp737Ng);
        var package = CreatePackage(product, validStructure: true, identityProductId: AircraftProductIds.Zibo737Ng);
        var target = Path.Combine(xPlaneRoot, "Aircraft", product.DefaultFolderName);

        var result = new AircraftFreshInstallOperation(isXPlaneRunning: () => false).Apply(
            xPlaneRoot,
            target,
            product,
            BuildPlan(product, package.Package),
            [package]);

        Assert.False(result.Succeeded);
        Assert.Contains("structural identity", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(target));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(xPlaneRoot, "Aircraft")));
    }

    [Fact]
    public void Apply_WhenTargetIsOutsideAircraftRoot_Blocks()
    {
        var xPlaneRoot = CreateXPlaneRoot();
        var product = AircraftFreshInstallProduct.All.Single(item => item.ProductId == AircraftProductIds.Zibo737Ng);
        var package = CreatePackage(product, validStructure: true);
        var target = Path.Combine(xPlaneRoot, product.DefaultFolderName);

        var result = new AircraftFreshInstallOperation(isXPlaneRunning: () => false).Apply(
            xPlaneRoot,
            target,
            product,
            BuildPlan(product, package.Package),
            [package]);

        Assert.False(result.Succeeded);
        Assert.Contains("direct child", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void DryRun_AllowsMissingFreshInstallTargetAndReportsAdds()
    {
        var xPlaneRoot = CreateXPlaneRoot();
        var product = AircraftFreshInstallProduct.All.Single(item => item.ProductId == AircraftProductIds.Zibo737Ng);
        var package = CreatePackage(product, validStructure: true);
        var target = Path.Combine(xPlaneRoot, "Aircraft", product.DefaultFolderName);

        var result = new AircraftUpdateDryRunAnalyzer().Analyze(
            target,
            [package],
            allowMissingAircraftFolder: true);

        Assert.True(result.Succeeded);
        Assert.Contains(result.Entries, entry => entry.Action == AircraftUpdateDryRunEntryAction.Add);
        Assert.Contains(result.Findings, finding => finding.Contains("Fresh-install target", StringComparison.Ordinal));
    }

    private string CreateXPlaneRoot()
    {
        var xPlaneRoot = Path.Combine(_root, "X-Plane 12");
        Directory.CreateDirectory(Path.Combine(xPlaneRoot, "Aircraft"));
        Directory.CreateDirectory(Path.Combine(xPlaneRoot, "Resources"));
        return xPlaneRoot;
    }

    private AircraftUpdatePackageCacheEntry CreatePackage(
        AircraftFreshInstallProduct product,
        bool validStructure,
        string? identityProductId = null)
    {
        Directory.CreateDirectory(_root);
        var archivePath = Path.Combine(_root, $"{product.ProductId}-{Guid.NewGuid():N}.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "Aircraft/plugins/test.txt", "plugin");
            if (validStructure)
            {
                foreach (var acf in RequiredAcfs(product.ProductId))
                {
                    WriteEntry(archive, $"Aircraft/{acf}", BuildAcf(identityProductId ?? product.ProductId, acf));
                }
            }
        }

        var package = new AircraftUpdatePackage(
            product.ProductId,
            AircraftUpdatePackageKind.FullBaseline,
            new AircraftUpstreamVersion(1, 0, 0),
            Path.GetFileName(archivePath),
            "https://example.invalid/package.zip",
            ReleaseVersion: "1.0.0");
        return new AircraftUpdatePackageCacheEntry(
            package,
            archivePath,
            AircraftUpdatePackageCacheState.Cached,
            new FileInfo(archivePath).Length,
            Sha256: null);
    }

    private AircraftUpdatePackageCacheEntry CreatePatchPackage(AircraftFreshInstallProduct product)
    {
        var archivePath = Path.Combine(_root, $"{product.ProductId}-patch-{Guid.NewGuid():N}.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "Aircraft/plugins/patch-state.txt", "patched");
        }

        var package = new AircraftUpdatePackage(
            product.ProductId,
            AircraftUpdatePackageKind.CumulativePatch,
            new AircraftUpstreamVersion(1, 0, 1),
            Path.GetFileName(archivePath),
            "https://example.invalid/patch.zip",
            ReleaseVersion: "1.0.1");
        return new AircraftUpdatePackageCacheEntry(
            package,
            archivePath,
            AircraftUpdatePackageCacheState.Cached,
            new FileInfo(archivePath).Length,
            Sha256: null);
    }

    private static AircraftUpstreamUpdateCheckResult BuildPlan(
        AircraftFreshInstallProduct product,
        params AircraftUpdatePackage[] packages) =>
        new(
            "Ready to install",
            $"Install {product.DisplayName}.",
            product.ProductId,
            "https://example.invalid/index",
            "Not installed",
            packages[^1].VersionDisplay,
            AircraftUpdatePlanAction.InstallBaselineAndCumulativePatch,
            "Install full package",
            IsCustomDistribution: false,
            packages,
            []);

    private static IReadOnlyList<string> RequiredAcfs(string productId) =>
        productId == AircraftProductIds.Zibo737Ng
            ? ["b738.acf", "b738_4k.acf"]
            : ["737_60NG.acf", "737_70NG.acf", "737_80NG.acf", "737_90NG.acf", "737_9ENG.acf"];

    private static string BuildAcf(string productId, string acfName)
    {
        var isZibo = productId == AircraftProductIds.Zibo737Ng;
        var name = isZibo
            ? acfName == "b738_4k.acf" ? "Boeing 737-800X (4k)" : "Boeing 737-800X"
            : acfName switch
            {
                "737_60NG.acf" => "Boeing 737-600NG",
                "737_70NG.acf" => "Boeing 737-700NG",
                "737_80NG.acf" => "Boeing 737-800NG",
                "737_90NG.acf" => "Boeing 737-900NG",
                _ => "Boeing 737-900ER"
            };
        var studio = isZibo ? "Laminar Research modified by Zibo" : "LevelUp, Laminar Research, ZiboMod";
        return $"""
            1200 Version
            P acf/_descrip {name}
            P acf/_file_writer_version 124311
            P acf/_name {name}
            P acf/_studio {studio}
            P acf/_version test
            P acf/_cgY -2.000000000
            P acf/_cgZ 60.000000000
            """;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
