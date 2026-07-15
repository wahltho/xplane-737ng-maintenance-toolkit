using LevelUp.NavTableUpdater.Core.Manifest;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class ManifestParserTests
{
    [Fact]
    public void ParsePipeManifest_ReadsPackagePayloadsAndPatchOperations()
    {
        var manifest = ManifestParser.ParsePipeManifest(TestFixtures.ManifestText);

        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Equal("x-plane-levelup-737ng-vnav-descent-tables", manifest.PackageId);
        Assert.Equal("v0.2.0", manifest.PackageVersion);
        Assert.Equal("plugins/xlua/scripts/B738.a_fms/B738.a_fms.lua", manifest.TargetRelativePath);
        Assert.Equal(4, manifest.Payloads.Count);
        Assert.Equal(3, manifest.PatchOperations.Count);
        Assert.Contains(manifest.PatchOperations, operation => operation.Id == "kias" && operation.LegacySignatures.Count == 1);
    }
}
