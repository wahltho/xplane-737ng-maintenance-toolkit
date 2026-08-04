using System.Text;
using LevelUp.NavTableUpdater.Core.Resources;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class ResourcePackageManifestTests
{
    [Fact]
    public void Parse_ValidResourceManifest_NormalizesIdentity()
    {
        var manifest = ResourcePackageManifestParser.Parse(Encoding.UTF8.GetBytes(BuildManifest()));

        Assert.Equal("resource", manifest.PackageType);
        Assert.Equal("levelup.paintkit", manifest.PackageId);
        Assert.Equal("stable", manifest.Channel);
        Assert.Equal("extract", manifest.DeliveryMode);
        Assert.Equal("Paintkit", manifest.ArchiveRoot);
        Assert.Equal("LevelUp Paintkit", manifest.TargetDirectory);
        Assert.Equal("LevelUp-Paintkit-2.S1.7z", manifest.Archive.FileName);
        Assert.Single(manifest.Files);
    }

    [Fact]
    public void Parse_BetaResourceManifest_AcceptsBetaChannel()
    {
        var json = BuildManifest().Replace(
            "\"channel\": \"stable\"",
            "\"channel\": \"beta\"",
            StringComparison.Ordinal);

        var manifest = ResourcePackageManifestParser.Parse(Encoding.UTF8.GetBytes(json));

        Assert.Equal("beta", manifest.Channel);
    }

    [Theory]
    [InlineData("../Paintkit.7z")]
    [InlineData("folder/Paintkit.7z")]
    [InlineData("Paintkit.zip")]
    public void Parse_UnsafeArchiveName_RejectsManifest(string archiveName)
    {
        var json = BuildManifest().Replace("LevelUp-Paintkit-2.S1.7z", archiveName, StringComparison.Ordinal);

        var error = Assert.Throws<InvalidDataException>(
            () => ResourcePackageManifestParser.Parse(Encoding.UTF8.GetBytes(json)));

        Assert.Contains("archive metadata", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../readme.txt")]
    [InlineData("folder\\readme.txt")]
    [InlineData("C:/readme.txt")]
    public void Parse_UnsafeExtractedFilePath_RejectsManifest(string path)
    {
        var json = BuildManifest().Replace("readme.txt", path, StringComparison.Ordinal);

        var error = Assert.Throws<InvalidDataException>(
            () => ResourcePackageManifestParser.Parse(Encoding.UTF8.GetBytes(json)));

        Assert.Contains("extraction metadata", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildManifest() =>
        """
        {
          "schemaVersion": 1,
          "packageType": "resource",
          "packageId": "levelup.paintkit",
          "packageVersion": "2.S1",
          "releaseTag": "paintkit-v2.S1",
          "channel": "stable",
          "repository": "https://github.com/petrolpram/737NG-Updates",
          "supportedProducts": ["levelup-737ng"],
          "deliveryMode": "extract",
          "archiveRoot": "Paintkit",
          "targetDirectory": "LevelUp Paintkit",
          "extractedSize": 12,
          "files": [
            {
              "path": "readme.txt",
              "size": 12,
              "sha256": "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789"
            }
          ],
          "archive": {
            "fileName": "LevelUp-Paintkit-2.S1.7z",
            "size": 12,
            "sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
          }
        }
        """;
}
