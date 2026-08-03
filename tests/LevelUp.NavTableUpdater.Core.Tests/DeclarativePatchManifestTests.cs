using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.Manifest;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class DeclarativePatchManifestTests
{
    [Fact]
    public void Parse_WhenSchemaTwoManifestIsValid_LoadsTargetsAndPayloads()
    {
        var payload = Encoding.UTF8.GetBytes("{}");
        var manifest = DeclarativePatchManifestParser.Parse(BuildManifest(
            "patches/change.json",
            payload,
            "objects/test.txt"));

        Assert.Equal(2, manifest.SchemaVersion);
        Assert.Equal(ContentPatchCatalog.FansCdu.ComponentId, manifest.PackageId);
        Assert.Single(manifest.Payloads);
        Assert.Single(manifest.Targets);
    }

    [Fact]
    public void Parse_WhenTargetEscapesRoot_RejectsManifest()
    {
        var payload = Encoding.UTF8.GetBytes("{}");
        var json = BuildManifest("patches/change.json", payload, "../outside.txt");

        var error = Assert.Throws<InvalidOperationException>(() => DeclarativePatchManifestParser.Parse(json));

        Assert.Contains("Unsafe target path", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WhenSourceHashesAreOmitted_DefersValidationToThePatchHandler()
    {
        var payload = Encoding.UTF8.GetBytes("{}");

        var manifest = DeclarativePatchManifestParser.Parse(BuildManifest(
            "patches/change.json",
            payload,
            "plugins/xlua/scripts/B738.tablet/B738.tablet.lua",
            includeSourceHash: false,
            includeResultHash: false));

        Assert.Empty(Assert.Single(manifest.Targets).SourceSha256);
    }

    [Fact]
    public void Parse_WithExplicitSupportedProduct_UsesStableProductId()
    {
        var payload = Encoding.UTF8.GetBytes("{}");

        var manifest = DeclarativePatchManifestParser.Parse(BuildManifest(
            "patches/change.json",
            payload,
            "objects/test.txt",
            supportedProducts: ["levelup-737ng"]));

        Assert.Equal(["levelup-737ng"], manifest.SupportedProducts);
        Assert.True(DeclarativePatchProductCompatibility.SupportsProduct(manifest, "levelup-737ng"));
        Assert.False(DeclarativePatchProductCompatibility.SupportsProduct(manifest, "zibo-737ng"));
    }

    [Fact]
    public void Parse_WithoutSupportedProducts_UsesNarrowLegacyFamilyFallback()
    {
        var payload = Encoding.UTF8.GetBytes("{}");

        var manifest = DeclarativePatchManifestParser.Parse(BuildManifest(
            "patches/change.json",
            payload,
            "objects/test.txt"));

        Assert.Equal(["levelup-737ng"], DeclarativePatchProductCompatibility.ResolveSupportedProducts(manifest));
    }

    [Fact]
    public void Parse_WithUnknownSupportedProduct_RejectsManifest()
    {
        var payload = Encoding.UTF8.GetBytes("{}");
        var json = BuildManifest(
            "patches/change.json",
            payload,
            "objects/test.txt",
            supportedProducts: ["unknown-aircraft"]);

        var error = Assert.Throws<InvalidOperationException>(() => DeclarativePatchManifestParser.Parse(json));

        Assert.Contains("Unsupported product ID", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WhenSourceHashIsMalformed_RejectsManifest()
    {
        var payload = Encoding.UTF8.GetBytes("{}");
        var json = BuildManifest("patches/change.json", payload, "objects/test.txt", sourceHash: "not-a-hash");

        var error = Assert.Throws<InvalidOperationException>(() => DeclarativePatchManifestParser.Parse(json));

        Assert.Contains("invalid source SHA-256", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadDirectory_WhenPayloadHashDiffers_RejectsPackage()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, "patches"));
        var declared = Encoding.UTF8.GetBytes("{}");
        File.WriteAllText(Path.Combine(directory.Path, "package-manifest.json"), BuildManifest(
            "patches/change.json",
            declared,
            "objects/test.txt"));
        File.WriteAllText(Path.Combine(directory.Path, "patches", "change.json"), "{\"changed\":true}");

        var error = Assert.Throws<InvalidOperationException>(() => DeclarativePatchPackageLoader.LoadDirectory(directory.Path));

        Assert.Contains("size/SHA-256", error.Message, StringComparison.Ordinal);
    }

    internal static string BuildManifest(
        string payloadPath,
        byte[] payload,
        string targetPath,
        string? sourceHash = null,
        string? resultHash = null,
        bool includeSourceHash = true,
        bool includeResultHash = true,
        string operation = "exact-text-replacements-v1",
        string[]? supportedProducts = null)
    {
        var payloadHash = Sha256(payload);
        sourceHash ??= new string('1', 64);
        resultHash ??= new string('2', 64);
        var target = new Dictionary<string, object>
        {
            ["operation"] = operation,
            ["payload"] = payloadPath,
            ["relativePath"] = targetPath
        };
        if (includeSourceHash)
        {
            target["sourceSha256"] = new[] { sourceHash };
        }

        if (includeResultHash)
        {
            target["resultSha256"] = resultHash;
        }

        var manifest = new Dictionary<string, object>
        {
            ["schemaVersion"] = 2,
            ["packageId"] = "wahltho.levelup-737ng.fans-cdu-3d",
            ["packageVersion"] = "0.1.0",
            ["repositoryUrl"] = "https://github.com/wahltho/X-Plane-LevelUp-737NG-FANS-CDU",
            ["aircraftFamily"] = "LevelUp 737NG Series v2 for X-Plane 12",
            ["restartRequired"] = true,
            ["supportedUpstreamReleases"] = new[] { "737NG Series V2.S1.50A" },
            ["payloads"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["path"] = payloadPath,
                    ["size"] = payload.LongLength,
                    ["sha256"] = payloadHash
                }
            },
            ["targets"] = new object[] { target }
        };
        if (supportedProducts is not null)
        {
            manifest["supportedProducts"] = supportedProducts;
        }

        return JsonSerializer.Serialize(manifest);
    }

    internal static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    internal sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"xplane-content-patch-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
