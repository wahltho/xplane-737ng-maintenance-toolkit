using System.Text.Json;
using System.Text.Json.Serialization;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.Manifest;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class CatalogGroupPolicyTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public void GroupReferencingAnotherProduct_IsRejected()
    {
        var document = Document();
        document.Packages[0].SupportedProducts = ["zibo-737ng"];
        Assert.Throws<InvalidDataException>(() => ContentPackageCatalog.Parse(JsonSerializer.Serialize(document, Json)));
    }

    [Fact]
    public void GroupWithMissingSource_IsRejected()
    {
        var document = Document();
        document.Packages[1].Members[0].PackageId = "missing";
        Assert.Throws<InvalidDataException>(() => ContentPackageCatalog.Parse(JsonSerializer.Serialize(document, Json)));
    }

    [Fact]
    public void SourceReleaseVersionIsNotRequiredInCatalog()
    {
        var parsed = ContentPackageCatalog.Parse(JsonSerializer.Serialize(Document(), Json), new Version(0, 13, 0));
        var member = Assert.Single(parsed.Packages[1].Members);
        Assert.Equal(CompatibilityModulePolicy.Required, member.Policy);
        Assert.Equal("upstream", member.PackageId);
    }

    [Fact]
    public void OlderApplicationCannotLoadNewCatalog()
    {
        Assert.Throws<InvalidDataException>(() => ContentPackageCatalog.Parse(
            JsonSerializer.Serialize(Document(), Json), new Version(0, 12, 6)));
    }

    private static ContentPackageCatalogDocument Document() => new()
    {
        SchemaVersion = 1, CatalogVersion = "1.6.0", MinimumToolkitVersion = "0.13.0",
        Packages =
        [
            new() { PackageId = "upstream", DisplayName = "Source", Description = "Source patch", Category = ContentPackageCategory.CompatibilityPackage,
                Activation = ContentPatchActivation.Managed, SupportedProducts = ["levelup-737ng"], RepositoryUrl = "https://github.com/example/source",
                Distribution = new() { Kind = ContentPackageDistributionKind.GitHubModuleSource } },
            new() { PackageId = "group", DisplayName = "Group", Description = "Required patches", Category = ContentPackageCategory.CompatibilityPackage,
                Activation = ContentPatchActivation.Managed, SupportedProducts = ["levelup-737ng"], RepositoryUrl = "https://github.com/example/toolkit",
                Distribution = new() { Kind = ContentPackageDistributionKind.CatalogGroup },
                Members = [new() { PackageId = "upstream", ModuleId = "module", Policy = CompatibilityModulePolicy.Required,
                    SourceFormat = "moduleSource", ManifestPath = "toolkit/module.json", AssetNamePattern = "source-*.zip" }] }
        ]
    };
}
