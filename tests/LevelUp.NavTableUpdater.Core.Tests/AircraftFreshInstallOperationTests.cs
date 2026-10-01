using System.IO.Compression;
using System.Text;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Detection;
using LevelUp.NavTableUpdater.Core.Tools;
using LevelUp.NavTableUpdater.Core.Upstream;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class AircraftFreshInstallOperationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"xplane-737ng-fresh-install-tests-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("zibo-737ng", "Boeing")]
    [InlineData("levelup-737ng", "Boeing")]
    [InlineData("zibo-737ng", "Boeing/737")]
    [InlineData("levelup-737ng", "Boeing/737")]
    public void Apply_NestedDestination_StagesBesideTargetAndInstallsCompletePatchedAircraft(
        string productId, string groupingFolder)
    {
        var xp = CreateXPlaneRoot();
        var product = AircraftFreshInstallProduct.All.Single(p => p.ProductId == productId);
        var full = CreatePackage(product, validStructure: true);
        var patch = CreatePatchPackage(product);
        var parent = Path.Combine(xp, "Aircraft", groupingFolder.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(parent);
        var sentinel = Path.Combine(parent, "other.txt");
        File.WriteAllText(sentinel, "unrelated grouping-folder content");
        var target = Path.Combine(parent, product.DefaultFolderName);

        var result = new AircraftFreshInstallOperation(() => false).Apply(
            xp, target, product, BuildPlan(product, full.Package, patch.Package), [full, patch],
            writePhaseStarting: () =>
            {
                var stage = Assert.Single(Directory.GetDirectories(parent, ".*.toolkit-install-*"));
                Assert.True(File.Exists(Path.Combine(stage, "plugins", "patch-state.txt")));
                Assert.False(Directory.Exists(target));
            });

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("patched", File.ReadAllText(Path.Combine(target, "plugins", "patch-state.txt")));
        foreach (var acf in RequiredAcfs(productId)) Assert.True(File.Exists(Path.Combine(target, acf)));
        Assert.Equal("unrelated grouping-folder content", File.ReadAllText(sentinel));
        Assert.Empty(Directory.GetDirectories(parent, ".*.toolkit-install-*"));
        Assert.Equal(xp, XPlaneInstallationLocator.Resolve(target));
        Assert.Contains(new AircraftDetector(_root).FindCandidates([xp]), c => c.Path == target);
        var analysis = new AircraftViewAnalyzer().Analyze(target);
        Assert.Contains(analysis.Variants, v => v.Family == productId);
    }

    [Theory]
    [InlineData("aircraft-root", "under X-Plane")]
    [InlineData("outside", "under X-Plane")]
    [InlineData("sibling-prefix", "under X-Plane")]
    [InlineData("traversal", "under X-Plane")]
    [InlineData("missing-parent", "parent folder must already exist")]
    [InlineData("existing-directory", "already exists")]
    [InlineData("existing-file", "already exists")]
    [InlineData("inside-aircraft", "inside an existing aircraft")]
    public void Apply_InvalidNestedDestination_BlocksWithoutChangingExistingFiles(string scenario, string message)
    {
        var xp = CreateXPlaneRoot();
        var product = AircraftFreshInstallProduct.All[0];
        var full = CreatePackage(product, validStructure: true);
        var parent = Path.Combine(xp, "Aircraft", "Boeing");
        Directory.CreateDirectory(parent);
        File.WriteAllText(Path.Combine(parent, "keep.txt"), "unchanged");
        var target = scenario switch
        {
            "aircraft-root" => Path.Combine(xp, "Aircraft"),
            "outside" => Path.Combine(xp, "Resources", "plane"),
            "sibling-prefix" => Path.Combine(xp, "Aircraft-other", "plane"),
            "traversal" => Path.Combine(xp, "Aircraft", "..", "Resources", "plane"),
            "missing-parent" => Path.Combine(parent, "missing", "plane"),
            "existing-directory" => parent,
            "existing-file" => Path.Combine(parent, "keep.txt"),
            _ => Path.Combine(parent, "plane")
        };
        if (scenario == "inside-aircraft") File.WriteAllText(Path.Combine(parent, "b738.acf"), "existing aircraft");
        var before = Directory.GetFiles(xp, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);

        var result = new AircraftFreshInstallOperation(() => false).Apply(
            xp, target, product, BuildPlan(product, full.Package), [full]);

        Assert.False(result.Succeeded);
        Assert.Contains(message, result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before.Count, Directory.GetFiles(xp, "*", SearchOption.AllDirectories).Length);
        foreach (var file in before) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
        Assert.Empty(Directory.GetDirectories(xp, "*.toolkit-install-*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Apply_LinkedGroupingFolder_BlocksEvenWhenLinkStaysInsideAircraft(bool outside)
    {
        var xp = CreateXPlaneRoot();
        var product = AircraftFreshInstallProduct.All[0];
        var full = CreatePackage(product, validStructure: true);
        var real = Path.Combine(xp, outside ? "Resources" : "Aircraft", "real");
        Directory.CreateDirectory(real);
        File.WriteAllText(Path.Combine(real, "keep.txt"), "unchanged");
        var link = Path.Combine(xp, "Aircraft", "Boeing");
        Directory.CreateSymbolicLink(link, real);
        try
        {
            var result = new AircraftFreshInstallOperation(() => false).Apply(
                xp, Path.Combine(link, product.DefaultFolderName), product, BuildPlan(product, full.Package), [full]);
            Assert.False(result.Succeeded);
            Assert.Contains("symbolic links or junctions", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("unchanged", File.ReadAllText(Path.Combine(real, "keep.txt")));
            Assert.Empty(Directory.GetDirectories(real));
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public void Apply_DanglingDestinationLink_BlocksWithoutFollowingIt()
    {
        var xp = CreateXPlaneRoot();
        var product = AircraftFreshInstallProduct.All[0];
        var full = CreatePackage(product, validStructure: true);
        var target = Path.Combine(xp, "Aircraft", "dangling");
        var missing = Path.Combine(xp, "Resources", "missing");
        Directory.CreateSymbolicLink(target, missing);
        try
        {
            var result = new AircraftFreshInstallOperation(() => false).Apply(
                xp, target, product, BuildPlan(product, full.Package), [full]);
            Assert.False(result.Succeeded);
            Assert.Contains("symbolic link", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(missing));
        }
        finally
        {
            if (OperatingSystem.IsWindows()) Directory.Delete(target);
            else File.Delete(target);
        }
    }

    [Theory]
    [InlineData("callback-failure")]
    [InlineData("simulator-started")]
    [InlineData("destination-appeared")]
    [InlineData("damaged-staging")]
    public void Apply_NestedActivationFailure_CleansOwnedFilesAndPreservesUnrelatedContent(string scenario)
    {
        var xp = CreateXPlaneRoot();
        var product = AircraftFreshInstallProduct.All[0];
        var full = CreatePackage(product, validStructure: true);
        var parent = Path.Combine(xp, "Aircraft", "Boeing");
        Directory.CreateDirectory(parent);
        File.WriteAllText(Path.Combine(parent, "keep.txt"), "unchanged");
        var target = Path.Combine(parent, product.DefaultFolderName);
        var running = false;
        var result = new AircraftFreshInstallOperation(() => running).Apply(
            xp, target, product, BuildPlan(product, full.Package), [full], writePhaseStarting: () =>
            {
                if (scenario == "callback-failure") throw new IOException("injected activation failure");
                if (scenario == "simulator-started") running = true;
                if (scenario == "destination-appeared")
                {
                    Directory.CreateDirectory(target);
                    File.WriteAllText(Path.Combine(target, "new-owner.txt"), "preserve");
                }
                if (scenario == "damaged-staging")
                {
                    var stage = Assert.Single(Directory.GetDirectories(parent, ".*.toolkit-install-*"));
                    File.Delete(Path.Combine(stage, RequiredAcfs(product.ProductId)[0]));
                }
            });

        Assert.False(result.Succeeded);
        Assert.Equal("unchanged", File.ReadAllText(Path.Combine(parent, "keep.txt")));
        Assert.Empty(Directory.GetDirectories(parent, ".*.toolkit-install-*"));
        if (scenario == "destination-appeared") Assert.Equal("preserve", File.ReadAllText(Path.Combine(target, "new-owner.txt")));
        else Assert.False(Directory.Exists(target));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Apply_AncestorReplacedByLinkDuringActivation_DoesNotWriteOrCleanThroughLink(bool aircraftRootChanged)
    {
        var xp = CreateXPlaneRoot();
        var product = AircraftFreshInstallProduct.All[0];
        var full = CreatePackage(product, validStructure: true);
        var parent = Path.Combine(xp, "Aircraft", "Boeing");
        var changedPath = aircraftRootChanged ? Path.Combine(xp, "Aircraft") : parent;
        var savedPath = aircraftRootChanged ? Path.Combine(xp, "saved-Aircraft") : Path.Combine(xp, "Aircraft", "saved-Boeing");
        var savedParent = aircraftRootChanged ? Path.Combine(savedPath, "Boeing") : savedPath;
        var outsideRoot = Path.Combine(xp, "Resources", "outside");
        var outside = aircraftRootChanged ? Path.Combine(outsideRoot, "Boeing") : outsideRoot;
        Directory.CreateDirectory(parent);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "keep.txt"), "unchanged");
        var target = Path.Combine(parent, product.DefaultFolderName);
        string? stageName = null;
        try
        {
            var result = new AircraftFreshInstallOperation(() => false).Apply(
                xp, target, product, BuildPlan(product, full.Package), [full], writePhaseStarting: () =>
                {
                    stageName = Path.GetFileName(Assert.Single(Directory.GetDirectories(parent, ".*.toolkit-install-*")));
                    Directory.Move(changedPath, savedPath);
                    Directory.CreateSymbolicLink(changedPath, outsideRoot);
                    // A different directory at the same name must not be removed by cleanup.
                    Directory.CreateDirectory(Path.Combine(outside, stageName));
                    File.WriteAllText(Path.Combine(outside, stageName, "keep.txt"), "unrelated stage-name collision");
                });
            Assert.False(result.Succeeded);
            Assert.Contains(aircraftRootChanged ? "changed during staging" : "symbolic links or junctions",
                result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("unchanged", File.ReadAllText(Path.Combine(outside, "keep.txt")));
            Assert.Equal("unrelated stage-name collision", File.ReadAllText(Path.Combine(outside, stageName!, "keep.txt")));
            Assert.False(Directory.Exists(Path.Combine(outside, product.DefaultFolderName)));
            Assert.True(Directory.Exists(Path.Combine(savedParent, stageName!)));
            Assert.Contains(result.Log, line => line.Contains("Staging retained", StringComparison.Ordinal));
        }
        finally { if (new DirectoryInfo(changedPath).LinkTarget is not null) Directory.Delete(changedPath); }
    }

    [Fact]
    public void Apply_NestedCanceledStaging_LeavesParentAndExistingFilesUnchanged()
    {
        var xp = CreateXPlaneRoot();
        var product = AircraftFreshInstallProduct.All[0];
        var full = CreatePackage(product, validStructure: true);
        var parent = Path.Combine(xp, "Aircraft", "Boeing");
        Directory.CreateDirectory(parent);
        File.WriteAllText(Path.Combine(parent, "keep.txt"), "unchanged");
        using var cancel = new CancellationTokenSource();
        var operation = new AircraftFreshInstallOperation(() => { cancel.Cancel(); return false; });

        Assert.Throws<OperationCanceledException>(() => operation.Apply(
            xp, Path.Combine(parent, product.DefaultFolderName), product,
            BuildPlan(product, full.Package), [full], cancel.Token));
        Assert.Empty(Directory.GetDirectories(parent));
        Assert.Equal("unchanged", File.ReadAllText(Path.Combine(parent, "keep.txt")));
    }

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

    [Theory]
    [InlineData("zibo-737ng")]
    [InlineData("levelup-737ng")]
    public void Apply_FullBaselineAndCumulativePatch_ActivatesPatchedImage(string productId)
    {
        var xPlaneRoot = CreateXPlaneRoot();
        var product = AircraftFreshInstallProduct.All.Single(item => item.ProductId == productId);
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
    public void Apply_ZiboFeedFamily_MatchesSelectedZiboProduct()
    {
        var xPlaneRoot = CreateXPlaneRoot();
        var product = AircraftFreshInstallProduct.All.Single(item => item.ProductId == AircraftProductIds.Zibo737Ng);
        var package = CreatePackage(
            product,
            validStructure: true,
            packageFamily: ZiboUpstreamFeedParser.Family);
        var target = Path.Combine(xPlaneRoot, "Aircraft", product.DefaultFolderName);

        var result = new AircraftFreshInstallOperation(isXPlaneRunning: () => false).Apply(
            xPlaneRoot,
            target,
            product,
            BuildPlan(product, ZiboUpstreamFeedParser.Family, package.Package),
            [package]);

        Assert.True(result.Succeeded);
        Assert.True(Directory.Exists(target));
    }

    [Fact]
    public void Apply_WhenFeedFamilyDoesNotMatchSelectedProduct_Blocks()
    {
        var xPlaneRoot = CreateXPlaneRoot();
        var product = AircraftFreshInstallProduct.All.Single(item => item.ProductId == AircraftProductIds.LevelUp737Ng);
        var package = CreatePackage(
            product,
            validStructure: true,
            packageFamily: ZiboUpstreamFeedParser.Family);
        var target = Path.Combine(xPlaneRoot, "Aircraft", product.DefaultFolderName);

        var result = new AircraftFreshInstallOperation(isXPlaneRunning: () => false).Apply(
            xPlaneRoot,
            target,
            product,
            BuildPlan(product, ZiboUpstreamFeedParser.Family, package.Package),
            [package]);

        Assert.False(result.Succeeded);
        Assert.Contains("does not match", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(target));
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
        Assert.Contains("under X-Plane 12/Aircraft", result.Message, StringComparison.OrdinalIgnoreCase);
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
        string? identityProductId = null,
        string? packageFamily = null)
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
            packageFamily ?? product.ProductId,
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
        BuildPlan(product, product.ProductId, packages);

    private static AircraftUpstreamUpdateCheckResult BuildPlan(
        AircraftFreshInstallProduct product,
        string family,
        params AircraftUpdatePackage[] packages) =>
        new(
            "Ready to install",
            $"Install {product.DisplayName}.",
            family,
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
