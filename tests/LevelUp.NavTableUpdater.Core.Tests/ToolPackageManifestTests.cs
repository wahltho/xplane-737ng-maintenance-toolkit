using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Tools;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class ToolPackageManifestTests
{
    [Fact]
    public void Parse_ValidManifest_NormalizesAndMatchesProtectedPaths()
    {
        var bytes = Encoding.UTF8.GetBytes("payload");

        var manifest = ToolPackageManifestParser.Parse(Encoding.UTF8.GetBytes(BuildManifest(bytes)));

        Assert.Equal("wahltho.yal", manifest.PackageId);
        Assert.Equal("Resources/plugins/YAL", manifest.TargetPath);
        Assert.True(ToolPackageManifestParser.IsProtectedPath(manifest, "data/modules/configuration/configuration.ini"));
        Assert.True(ToolPackageManifestParser.IsProtectedPath(manifest, "data/output/session/log.txt"));
        Assert.False(ToolPackageManifestParser.IsProtectedPath(manifest, "data/modules/main.lua"));
    }

    [Fact]
    public void Parse_TraversalPath_RejectsManifest()
    {
        var manifest = BuildManifest(Encoding.UTF8.GetBytes("payload"))
            .Replace("data/modules/main.lua", "../main.lua", StringComparison.Ordinal);

        var error = Assert.Throws<InvalidDataException>(() => ToolPackageManifestParser.Parse(Encoding.UTF8.GetBytes(manifest)));

        Assert.Contains("unsafe", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_AircraftInstallationScope_AcceptsManifest()
    {
        var json = BuildManifest(Encoding.UTF8.GetBytes("payload"))
            .Replace("xPlaneInstallation", "aircraftInstallation", StringComparison.Ordinal)
            .Replace("Resources/plugins/YAL", "plugins/xlua", StringComparison.Ordinal);

        var manifest = ToolPackageManifestParser.Parse(Encoding.UTF8.GetBytes(json));

        Assert.Equal("aircraftInstallation", manifest.InstallScope);
        Assert.Equal("plugins/xlua", manifest.TargetPath);
    }

    internal static string BuildManifest(byte[] packageFile, string channel = "stable", string version = "4.7")
    {
        var archiveHash = new string('a', 64);
        return JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            packageId = "wahltho.yal",
            packageVersion = version,
            releaseTag = "v" + version,
            channel,
            repository = "https://github.com/example/yal",
            installScope = "xPlaneInstallation",
            targetPath = "Resources/plugins/YAL",
            supportedProducts = new[] { "zibo-737ng", "levelup-737ng" },
            restartRequired = true,
            archive = new
            {
                fileName = $"YAL-{version}.zip",
                rootPath = "YAL",
                size = 10,
                sha256 = archiveHash
            },
            protectedPaths = new[]
            {
                "data/modules/configuration/configuration.ini",
                "data/modules/configuration/wprefs.ini",
                "data/output/**"
            },
            files = new[]
            {
                new
                {
                    path = "data/modules/main.lua",
                    size = packageFile.LongLength,
                    sha256 = Sha256(packageFile)
                }
            }
        });
    }

    internal static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
