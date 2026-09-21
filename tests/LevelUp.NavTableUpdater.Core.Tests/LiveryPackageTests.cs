using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.Manifest;
using LevelUp.NavTableUpdater.Core.Resources;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class LiveryPackageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mtk-livery-tests-{Guid.NewGuid():N}");

    [Fact]
    public void ManifestParser_AcceptsPublishedLiveryContract()
    {
        var payload = Encoding.UTF8.GetBytes("texture");
        var archive = BuildZip(("Lufthansa/objects/texture.png", payload));
        var json = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            packageType = "livery",
            packageId = "wahltho.levelup-737ng.livery.lufthansa",
            packageVersion = "1.0.0",
            releaseTag = "v1.0.0",
            channel = "stable",
            repository = "https://github.com/wahltho/X-Plane-LevelUp-737NG-Lufthansa-Livery",
            supportedProducts = new[] { "levelup-737ng" },
            supportedVariants = new[] { "737-700", "737-900ER" },
            installScope = "aircraftLivery",
            targetDirectory = "Lufthansa",
            archiveRoot = "Lufthansa",
            restartRequired = true,
            files = new[] { new { path = "objects/texture.png", size = payload.LongLength, sha256 = Hash(payload) } },
            archive = new { fileName = "Livery-v1.0.0.zip", size = archive.LongLength, sha256 = Hash(archive) },
            totals = new { fileCount = 1, uncompressedBytes = payload.LongLength }
        });

        var manifest = ResourcePackageManifestParser.Parse(json);

        Assert.Equal("livery", manifest.PackageType);
        Assert.Equal("aircraftLivery", manifest.InstallScope);
        Assert.Equal(["737-700", "737-900ER"], manifest.SupportedVariants);
        Assert.Equal(payload.LongLength, manifest.ExtractedSize);
        Assert.Equal("objects/texture.png", Assert.Single(manifest.Files).Path);
    }

    [Fact]
    public void InstallVerifyUpdateAndRemove_AreScopedToSelectedAircraft()
    {
        var fixture = CreateFixture("1.0.0", "first texture");
        var otherAircraft = CreateAircraft("LevelUp Other");

        var first = fixture.Manager.InstallLivery(fixture.Catalog, fixture.Package, fixture.AircraftRoot);
        var second = fixture.Manager.InstallLivery(fixture.Catalog, fixture.Package, otherAircraft);

        Assert.True(first.Changed);
        Assert.True(second.Changed);
        Assert.Equal("first texture", File.ReadAllText(Path.Combine(first.InstalledPath, "objects", "texture.png")));
        Assert.Equal(ResourcePackageState.Current,
            fixture.Manager.InspectLivery(fixture.Catalog, fixture.Package.Release, fixture.AircraftRoot, verifyHash: true).State);
        Assert.NotNull(fixture.Store.TryGetLiveryInstallation(fixture.AircraftRoot, fixture.Catalog.PackageId));
        Assert.NotNull(fixture.Store.TryGetLiveryInstallation(otherAircraft, fixture.Catalog.PackageId));

        var newer = BuildPackage("1.1.0", "new texture");
        var updated = fixture.Manager.InstallLivery(fixture.Catalog, newer, fixture.AircraftRoot);
        Assert.True(updated.Changed);
        Assert.Equal("new texture", File.ReadAllText(Path.Combine(updated.InstalledPath, "objects", "texture.png")));
        Assert.Equal("first texture", File.ReadAllText(Path.Combine(second.InstalledPath, "objects", "texture.png")));

        var removed = fixture.Manager.RemoveLivery(fixture.Catalog, fixture.AircraftRoot);
        Assert.True(removed.Changed);
        Assert.False(Directory.Exists(updated.InstalledPath));
        Assert.True(Directory.Exists(second.InstalledPath));
    }

    [Fact]
    public void ExistingUnmanagedLivery_IsNeverOverwritten()
    {
        var fixture = CreateFixture("1.0.0", "release texture");
        var target = Path.Combine(fixture.AircraftRoot, "liveries", "Lufthansa");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "user.txt"), "unmanaged");

        var error = Assert.Throws<InvalidOperationException>(() =>
            fixture.Manager.InstallLivery(fixture.Catalog, fixture.Package, fixture.AircraftRoot));

        Assert.Contains("unmanaged", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("unmanaged", File.ReadAllText(Path.Combine(target, "user.txt")));
    }

    [Fact]
    public void DestinationValidation_DoesNotCreateAircraftDirectoriesBeforeConfirmation()
    {
        var fixture = CreateFixture("1.0.0", "release texture");
        var aircraft = Path.Combine(_root, "Aircraft without liveries");
        Directory.CreateDirectory(aircraft);

        fixture.Manager.ValidateLiveryDestination(fixture.Catalog, fixture.Package.Release, aircraft);

        Assert.False(Directory.Exists(Path.Combine(aircraft, "liveries")));
    }

    [Fact]
    public void ChangedManagedLivery_BlocksUpdateAndRemove_ButExplicitRepairRestoresRelease()
    {
        var fixture = CreateFixture("1.0.0", "release texture");
        var installed = fixture.Manager.InstallLivery(fixture.Catalog, fixture.Package, fixture.AircraftRoot);
        var texture = Path.Combine(installed.InstalledPath, "objects", "texture.png");
        File.WriteAllText(texture, "local modification");

        Assert.Equal(ResourcePackageState.VerificationFailed,
            fixture.Manager.InspectLivery(fixture.Catalog, fixture.Package.Release, fixture.AircraftRoot, verifyHash: true).State);
        Assert.Throws<InvalidOperationException>(() =>
            fixture.Manager.InstallLivery(fixture.Catalog, fixture.Package, fixture.AircraftRoot));
        Assert.Throws<InvalidOperationException>(() =>
            fixture.Manager.RemoveLivery(fixture.Catalog, fixture.AircraftRoot));

        var repaired = fixture.Manager.InstallLivery(
            fixture.Catalog,
            fixture.Package,
            fixture.AircraftRoot,
            repair: true);

        Assert.True(repaired.Changed);
        Assert.Equal("release texture", File.ReadAllText(texture));
    }

    [Fact]
    public void ArchiveOutsideDeclaredRoot_IsRejectedWithoutAircraftChanges()
    {
        var fixture = CreateFixture("1.0.0", "release texture");
        var unsafePackage = BuildPackage("1.0.0", "release texture", extraArchiveEntry: ("outside.txt", "bad"));

        Assert.Throws<InvalidDataException>(() =>
            fixture.Manager.InstallLivery(fixture.Catalog, unsafePackage, fixture.AircraftRoot));
        Assert.False(Directory.Exists(Path.Combine(fixture.AircraftRoot, "liveries", "Lufthansa")));
    }

    [Fact]
    public void CatalogParser_AcceptsSafeLiveryEntryAndRejectsWrongScope()
    {
        var package = """
        {
          "packageId":"wahltho.levelup-737ng.livery.lufthansa",
          "displayName":"Lufthansa",
          "description":"Optional LevelUp livery.",
          "category":"livery",
          "activation":"explicitOptIn",
          "supportedProducts":["levelup-737ng"],
          "repositoryUrl":"https://github.com/wahltho/X-Plane-LevelUp-737NG-Lufthansa-Livery",
          "restartRequired":true,
          "installScope":"aircraftLivery",
          "supportedChannels":["stable"],
          "distribution":{
            "kind":"gitHubLiveryRelease",
            "assetNamePattern":"X-Plane-LevelUp-737NG-Lufthansa-Livery-v*.zip",
            "manifestAssetNamePattern":"X-Plane-LevelUp-737NG-Lufthansa-Livery-v*.manifest.json",
            "manifestSchemaVersion":1
          }
        }
        """;
        var json = $$"""{"schemaVersion":1,"catalogVersion":"1.9.0","minimumToolkitVersion":"0.16.0","packages":[{{package}}]}""";

        var catalog = ContentPackageCatalog.Parse(json, new Version(0, 16, 0));
        Assert.Equal(ContentPackageCategory.Livery, Assert.Single(catalog.Packages).Category);

        var invalid = json.Replace("aircraftLivery", "userSelectedDirectory", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => ContentPackageCatalog.Parse(invalid, new Version(0, 16, 0)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private Fixture CreateFixture(string version, string content)
    {
        Directory.CreateDirectory(_root);
        var store = new ToolStateStore(Path.Combine(_root, "state"), Path.Combine(_root, "backups"));
        return new Fixture(
            store,
            new ResourcePackageManager(store),
            BuildCatalog(),
            BuildPackage(version, content),
            CreateAircraft("LevelUp"));
    }

    private string CreateAircraft(string name)
    {
        var root = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(root, "liveries"));
        return root;
    }

    private ResourcePackageProvisionResult BuildPackage(
        string version,
        string content,
        (string Path, string Content)? extraArchiveEntry = null)
    {
        var payload = Encoding.UTF8.GetBytes(content);
        var entries = new List<(string, byte[])> { ("Lufthansa/objects/texture.png", payload) };
        if (extraArchiveEntry is { } extra) entries.Add((extra.Path, Encoding.UTF8.GetBytes(extra.Content)));
        var archive = BuildZip(entries.ToArray());
        var archivePath = Path.Combine(_root, $"Livery-{version}-{Guid.NewGuid():N}.zip");
        File.WriteAllBytes(archivePath, archive);
        var manifest = new ResourcePackageManifest
        {
            SchemaVersion = 1,
            PackageType = "livery",
            PackageId = "wahltho.levelup-737ng.livery.lufthansa",
            PackageVersion = version,
            ReleaseTag = "v" + version,
            Channel = "stable",
            Repository = "https://github.com/wahltho/X-Plane-LevelUp-737NG-Lufthansa-Livery",
            SupportedProducts = ["levelup-737ng"],
            SupportedVariants = ["737-700", "737-900ER"],
            InstallScope = "aircraftLivery",
            RestartRequired = true,
            DeliveryMode = "extract",
            ArchiveRoot = "Lufthansa",
            TargetDirectory = "Lufthansa",
            ExtractedSize = payload.LongLength,
            Files = [new ResourcePackageFile { Path = "objects/texture.png", Size = payload.LongLength, Sha256 = Hash(payload) }],
            Totals = new ResourcePackageTotals { FileCount = 1, UncompressedBytes = payload.LongLength },
            Archive = new ResourcePackageArchive
            {
                FileName = Path.GetFileName(archivePath),
                Size = archive.LongLength,
                Sha256 = Hash(archive)
            }
        };
        var release = new ResourcePackageRelease(
            ResourceReleaseChannel.Stable,
            manifest.ReleaseTag,
            manifest.Repository + "/releases/tag/" + manifest.ReleaseTag,
            $"Livery-{version}.manifest.json",
            manifest.Repository + "/releases/download/" + manifest.ReleaseTag + $"/Livery-{version}.manifest.json",
            1,
            new string('a', 64),
            manifest.Repository + "/releases/download/" + manifest.ReleaseTag + "/" + manifest.Archive.FileName,
            manifest);
        return new ResourcePackageProvisionResult(release, archivePath, Downloaded: true);
    }

    private static ContentPackageCatalogEntry BuildCatalog() => new()
    {
        PackageId = "wahltho.levelup-737ng.livery.lufthansa",
        DisplayName = "Lufthansa",
        Description = "Optional LevelUp livery.",
        Category = ContentPackageCategory.Livery,
        Activation = ContentPatchActivation.ExplicitOptIn,
        SupportedProducts = ["levelup-737ng"],
        RepositoryUrl = "https://github.com/wahltho/X-Plane-LevelUp-737NG-Lufthansa-Livery",
        RestartRequired = true,
        InstallScope = "aircraftLivery",
        SupportedChannels = ["stable"],
        Distribution = new ContentPackageDistribution
        {
            Kind = ContentPackageDistributionKind.GitHubLiveryRelease,
            AssetNamePattern = "X-Plane-LevelUp-737NG-Lufthansa-Livery-v*.zip",
            ManifestAssetNamePattern = "X-Plane-LevelUp-737NG-Lufthansa-Livery-v*.manifest.json",
            ManifestSchemaVersion = 1
        }
    };

    private static byte[] BuildZip(params (string Path, byte[] Content)[] entries)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in entries)
            {
                var entry = archive.CreateEntry(item.Path, CompressionLevel.NoCompression);
                using var stream = entry.Open();
                stream.Write(item.Content);
            }
        }
        return output.ToArray();
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record Fixture(
        ToolStateStore Store,
        ResourcePackageManager Manager,
        ContentPackageCatalogEntry Catalog,
        ResourcePackageProvisionResult Package,
        string AircraftRoot);
}
