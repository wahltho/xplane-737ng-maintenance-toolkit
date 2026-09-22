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

    [Fact]
    public void Schema4CompatibilitySource_PreservesScopeAndRetirementMetadata()
    {
        var payload = "new object"u8.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var oldHash = Convert.ToHexString(SHA256.HashData("old object"u8)).ToLowerInvariant();
        var source = new
        {
            schemaVersion = 4, packageType = "compatibilityPackage",
            packageId = "fixture", packageVersion = "1.0.0",
            repositoryUrl = "https://github.com/example/fixture",
            aircraftFamily = "LevelUp 737NG Series", supportedProducts = new[] { "levelup-737ng" },
            restartRequired = true,
            modules = new[] { new
            {
                moduleId = "module", displayName = "Module", description = "Managed objects",
                policy = "required", defaultEnabled = true, installationOrder = 10,
                payloads = new[] { new { path = "new.obj", size = payload.Length, sha256 = hash } },
                targets = new[] { new { operation = "copy-file-v1", payload = "new.obj",
                    relativePath = "objects/GSE/new.obj", resultSha256 = hash } },
                retiredFiles = new[] { new { relativePath = "objects/GSE/old.obj",
                    sourceSha256 = new[] { oldHash } } },
                managedScopes = new[] { new { relativePath = "objects/GSE", mode = "flatExclusive" } }
            } }
        };
        var member = new CatalogGroupMember { ModuleId = "module", SourceFormat = "compatibility",
            ManifestPath = "package-manifest.json", Policy = CompatibilityModulePolicy.Required };
        var entry = new ContentPackageCatalogEntry { PackageId = "fixture", DisplayName = "Fixture",
            Description = "Test source", RepositoryUrl = "https://github.com/example/fixture",
            SupportedProducts = ["levelup-737ng"] };
        var archive = new Dictionary<string, byte[]>
        {
            ["package-manifest.json"] = JsonSerializer.SerializeToUtf8Bytes(source),
            ["modules/module/new.obj"] = payload
        };

        var module = CatalogSourceAdapter.Convert(member, entry,
            new ContentPatchRelease("v1.0.0", "", "fixture.zip", "", 0, ""), archive, _root);
        Assert.Equal(4, module.SourceSchemaVersion);
        Assert.Equal("objects/GSE", Assert.Single(module.ManagedScopes).RelativePath);
        Assert.Equal(oldHash, Assert.Single(Assert.Single(module.RetiredFiles).SourceSha256));
        Assert.Equal(payload, File.ReadAllBytes(Path.Combine(_root, "new.obj")));
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
