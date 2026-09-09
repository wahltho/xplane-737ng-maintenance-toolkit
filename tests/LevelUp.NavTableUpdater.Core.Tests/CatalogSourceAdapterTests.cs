using System.Security.Cryptography;
using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.Manifest;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class CatalogSourceAdapterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "catalog-source-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void LegacySchemaTwo_UsesExistingFamilyCompatibilityAndStructuralContract()
    {
        var (member, entry, release, archive) = Fixture("declarative");
        var result = CatalogSourceAdapter.Convert(member, entry, release, archive, _root);
        Assert.Equal(CompatibilityModulePolicy.Required, result.Policy);
        Assert.True(result.DefaultEnabled);
        Assert.Empty(result.SupportedUpstreamReleases);
        Assert.Equal(archive["patch.json"], File.ReadAllBytes(Path.Combine(_root, "patch.json")));
    }

    [Fact]
    public void ModuleSource_CatalogPolicyOverridesRecommendationWithoutInventingAcfGate()
    {
        var (member, entry, release, archive) = Fixture("moduleSource");
        var result = CatalogSourceAdapter.Convert(member, entry, release, archive, _root);
        Assert.Equal(CompatibilityModulePolicy.Required, result.Policy);
        Assert.True(result.DefaultEnabled);
        Assert.Empty(result.SupportedUpstreamReleases);
    }

    [Fact]
    public void TamperedSourcePayload_IsRejectedBeforeAircraftPlanning()
    {
        var (member, entry, release, archive) = Fixture("declarative");
        archive["patch.json"] = "tampered"u8.ToArray();
        Assert.Throws<InvalidDataException>(() => CatalogSourceAdapter.Convert(member, entry, release, archive, _root));
    }

    [Fact]
    public void SourceVersionMustMatchResolvedRelease()
    {
        var (member, entry, release, archive) = Fixture("declarative");
        Assert.Throws<InvalidDataException>(() => CatalogSourceAdapter.Convert(member, entry, release with { Tag = "v2.0.0" }, archive, _root));
    }

    private static (CatalogGroupMember, ContentPackageCatalogEntry, ContentPatchRelease, Dictionary<string,byte[]>) Fixture(string format)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { format = "exact-text-replacements-v1", replacements = new[] { new { oldLines = new[] { "original" }, newLines = new[] { "patched" } } } });
        var manifest = new Dictionary<string, object>
        {
            ["schemaVersion"] = format == "declarative" ? 2 : 1,
            ["packageId"] = "fixture", ["packageVersion"] = "1.0.0", ["repositoryUrl"] = "https://github.com/example/fixture",
            ["aircraftFamily"] = "LevelUp 737NG Series", ["restartRequired"] = true,
            ["supportedUpstreamReleases"] = new[] { "descriptive baseline label" },
            ["payloads"] = new[] { new { path = "patch.json", size = payload.Length, sha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant() } },
            ["targets"] = new[] { new { operation = "exact-text-replacements-v1", payload = "patch.json", relativePath = "plugins/xlua/scripts/shared.lua" } }
        };
        if (format == "moduleSource")
        {
            manifest["manifestType"] = "levelup-compatibility-module-source";
            manifest["moduleId"] = "module"; manifest["moduleVersion"] = "1.0.0";
            manifest["supportedProducts"] = new[] { "levelup-737ng" };
            manifest["policyHint"] = "recommended";
        }
        return (new() { ModuleId = "module", SourceFormat = format, ManifestPath = "manifest.json", Policy = CompatibilityModulePolicy.Required },
            new() { PackageId = "fixture", DisplayName = "Fixture", Description = "Test source", RepositoryUrl = "https://github.com/example/fixture", SupportedProducts = ["levelup-737ng"] },
            new("v1.0.0", "", "fixture.zip", "", 0, ""),
            new() { ["manifest.json"] = JsonSerializer.SerializeToUtf8Bytes(manifest), ["patch.json"] = payload });
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
