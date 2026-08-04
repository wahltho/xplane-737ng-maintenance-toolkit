using System.Security.Cryptography;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.Resources;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class ResourcePackageManagerTests : IDisposable
{
    private const string ArchiveBase64 =
        "N3q8ryccAASpaacNvwAAAAAAAAAWAAAAAAAAACQlXM8BABZ3aW5nIHRlbXBsYXRlCnBhaW50a2l0CgDgAQ0AnF0AAIEzB64Pz7XvEA/Ual595dfdm+avUngQmcP0+WBirEOSNi99oMGOD552vQRCwiv8YD7KS3pK9nSSHqcJALi4Fw8Q87TswIX+DSOlZXgm/y9TBBsOGVHaW8VwnhimzOrv2rghUg8F9l+gQtLkjf4YwQfJumJqBQZ0J1Ea4uLAgbhNnHhU3AGy78I2fC4cSr0y4Hi/5zk09uLcAAAAABcGGwEJgKQABwsBAAEhIQEYDIEOAAA=";
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"xplane-resource-manager-tests-{Guid.NewGuid():N}");

    [Fact]
    public void InstallInspectAndRemove_TracksOnlyVerifiedExtractedResource()
    {
        var fixture = CreateFixture("2.S1");

        var installed = fixture.Manager.InstallToDirectory(fixture.Catalog, fixture.Package, fixture.Destination);
        var inspection = fixture.Manager.Inspect(fixture.Catalog, fixture.Package.Release, verifyHash: true);

        Assert.True(installed.Succeeded);
        Assert.True(installed.Changed);
        Assert.Equal("paintkit\n", File.ReadAllText(Path.Combine(installed.InstalledPath, "readme.txt")));
        Assert.Equal("wing template\n", File.ReadAllText(Path.Combine(installed.InstalledPath, "Templates", "wing.txt")));
        Assert.Equal(ResourcePackageState.Current, inspection.State);
        var removed = fixture.Manager.Remove(fixture.Catalog);
        Assert.True(removed.Succeeded);
        Assert.False(Directory.Exists(installed.InstalledPath));
        Assert.Null(fixture.Store.TryGetResourceInstallation(fixture.Catalog.PackageId));
    }

    [Fact]
    public void Inspect_WhenNewReleaseIsAvailable_ReportsUpdateAvailable()
    {
        var fixture = CreateFixture("2.S1");
        fixture.Manager.InstallToDirectory(fixture.Catalog, fixture.Package, fixture.Destination);
        var newer = fixture.Package with
        {
            Release = BuildRelease("2.S2", fixture.ArchivePath, fixture.Archive)
        };

        var inspection = fixture.Manager.Inspect(fixture.Catalog, newer.Release);

        Assert.Equal(ResourcePackageState.UpdateAvailable, inspection.State);
        Assert.Equal("2.S1", inspection.InstalledVersion);
        Assert.Equal("2.S2", inspection.AvailableVersion);
    }

    [Fact]
    public void InspectAndRemove_WhenExtractedFileWasModified_DoesNotDeleteIt()
    {
        var fixture = CreateFixture("2.S1");
        var installed = fixture.Manager.InstallToDirectory(fixture.Catalog, fixture.Package, fixture.Destination);
        File.WriteAllText(Path.Combine(installed.InstalledPath, "readme.txt"), "user modification");

        var inspection = fixture.Manager.Inspect(fixture.Catalog, fixture.Package.Release, verifyHash: true);
        var error = Assert.Throws<InvalidOperationException>(() => fixture.Manager.Remove(fixture.Catalog));

        Assert.Equal(ResourcePackageState.VerificationFailed, inspection.State);
        Assert.Contains("changed", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(installed.InstalledPath));
    }

    [Fact]
    public void Verify_WhenExtractedFileHasSameSizeButDifferentContent_DetectsCorruption()
    {
        var fixture = CreateFixture("2.S1");
        var installed = fixture.Manager.InstallToDirectory(fixture.Catalog, fixture.Package, fixture.Destination);
        var filePath = Path.Combine(installed.InstalledPath, "readme.txt");
        var bytes = File.ReadAllBytes(filePath);
        bytes[0] ^= 0xff;
        File.WriteAllBytes(filePath, bytes);

        var quickInspection = fixture.Manager.Inspect(fixture.Catalog, fixture.Package.Release);
        var verifiedInspection = fixture.Manager.Inspect(
            fixture.Catalog,
            fixture.Package.Release,
            verifyHash: true);

        Assert.Equal(ResourcePackageState.Current, quickInspection.State);
        Assert.Equal(ResourcePackageState.VerificationFailed, verifiedInspection.State);
    }

    [Fact]
    public void Remove_WhenAdditionalEmptyDirectoryExists_DoesNotDeleteIt()
    {
        var fixture = CreateFixture("2.S1");
        var installed = fixture.Manager.InstallToDirectory(fixture.Catalog, fixture.Package, fixture.Destination);
        var additionalDirectory = Path.Combine(installed.InstalledPath, "My Livery Work");
        Directory.CreateDirectory(additionalDirectory);

        var error = Assert.Throws<InvalidOperationException>(() => fixture.Manager.Remove(fixture.Catalog));

        Assert.Contains("additional directories", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(additionalDirectory));
    }

    [Fact]
    public void Install_WhenUnownedDestinationDirectoryExists_DoesNotOverwriteIt()
    {
        var fixture = CreateFixture("2.S1");
        var targetPath = Path.Combine(fixture.Destination, fixture.Package.Release.Manifest.TargetDirectory);
        Directory.CreateDirectory(targetPath);
        File.WriteAllText(Path.Combine(targetPath, "user.txt"), "unowned");

        var error = Assert.Throws<InvalidOperationException>(() =>
            fixture.Manager.InstallToDirectory(fixture.Catalog, fixture.Package, fixture.Destination));

        Assert.Contains("unowned", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("unowned", File.ReadAllText(Path.Combine(targetPath, "user.txt")));
    }

    [Fact]
    public void ValidateDestination_WhenUnownedDestinationDirectoryExists_BlocksBeforeDownload()
    {
        var fixture = CreateFixture("2.S1");
        var targetPath = Path.Combine(fixture.Destination, fixture.Package.Release.Manifest.TargetDirectory);
        Directory.CreateDirectory(targetPath);

        var error = Assert.Throws<InvalidOperationException>(() =>
            fixture.Manager.ValidateDestination(
                fixture.Catalog,
                fixture.Package.Release,
                fixture.Destination));

        Assert.Contains("unowned", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(targetPath));
    }

    [Fact]
    public void InstallTemporaryDownload_ExtractsAndRemovesArchiveAndStagingFiles()
    {
        var fixture = CreateFixture("2.S1");
        Directory.CreateDirectory(fixture.Destination);
        var temporaryPath = Path.Combine(
            fixture.Destination,
            $".{fixture.Package.Release.Manifest.Archive.FileName}.{Guid.NewGuid():N}.download");
        File.WriteAllBytes(temporaryPath, fixture.Archive);
        var temporaryPackage = fixture.Package with
        {
            ArchivePath = temporaryPath,
            Temporary = true
        };

        var installed = fixture.Manager.InstallToDirectory(
            fixture.Catalog,
            temporaryPackage,
            fixture.Destination);

        Assert.True(installed.Succeeded);
        Assert.True(installed.Changed);
        Assert.False(File.Exists(temporaryPath));
        Assert.Empty(Directory.EnumerateFiles(fixture.Destination));
        Assert.Single(Directory.EnumerateDirectories(fixture.Destination));
    }

    [Fact]
    public void InstallNewVersion_ReplacesOnlyVerifiedPreviousInstallation()
    {
        var fixture = CreateFixture("2.S1");
        fixture.Manager.InstallToDirectory(fixture.Catalog, fixture.Package, fixture.Destination);
        var newerRelease = BuildRelease("2.S2", fixture.ArchivePath, fixture.Archive);

        var result = fixture.Manager.InstallToDirectory(
            fixture.Catalog,
            new ResourcePackageProvisionResult(newerRelease, fixture.ArchivePath, Downloaded: true),
            fixture.Destination);

        Assert.True(result.Changed);
        Assert.Equal("2.S2", fixture.Store.TryGetResourceInstallation(fixture.Catalog.PackageId)?.PackageVersion);
        Assert.Single(Directory.EnumerateDirectories(fixture.Destination));
    }

    [Fact]
    public void Install_WhenManifestOmitsArchiveFile_LeavesDestinationUntouched()
    {
        var fixture = CreateFixture("2.S1");
        fixture.Package.Release.Manifest.Files.RemoveAt(1);
        fixture.Package.Release.Manifest.ExtractedSize = 9;

        var error = Assert.Throws<InvalidDataException>(() =>
            fixture.Manager.InstallToDirectory(fixture.Catalog, fixture.Package, fixture.Destination));

        Assert.Contains("manifest", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.Destination));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private Fixture CreateFixture(string version)
    {
        Directory.CreateDirectory(_root);
        var stateRoot = Path.Combine(_root, "state");
        var cacheRoot = Path.Combine(_root, "cache");
        var destination = Path.Combine(_root, "downloads");
        Directory.CreateDirectory(cacheRoot);
        Directory.CreateDirectory(destination);
        var archive = Convert.FromBase64String(ArchiveBase64);
        var archivePath = Path.Combine(cacheRoot, $"LevelUp-Paintkit-{version}.7z");
        File.WriteAllBytes(archivePath, archive);
        var store = new ToolStateStore(stateRoot, Path.Combine(_root, "backups"));
        var manager = new ResourcePackageManager(store);
        var catalog = BuildCatalog();
        var release = BuildRelease(version, archivePath, archive);
        return new Fixture(
            store,
            manager,
            catalog,
            new ResourcePackageProvisionResult(release, archivePath, Downloaded: true),
            archivePath,
            archive,
            destination);
    }

    private static ContentPackageCatalogEntry BuildCatalog() => new()
    {
        PackageId = "levelup.paintkit",
        DisplayName = "LevelUp Paintkit",
        Description = "Test resource",
        Category = ContentPackageCategory.Resource,
        Activation = ContentPatchActivation.ExplicitOptIn,
        SupportedProducts = ["levelup-737ng"],
        RepositoryUrl = "https://github.com/petrolpram/737NG-Updates",
        InstallScope = "userSelectedDirectory",
        SupportedChannels = ["stable"],
        Distribution = new ContentPackageDistribution
        {
            Kind = ContentPackageDistributionKind.GitHubResourceRelease,
            AssetNamePattern = "LevelUp-Paintkit-*.7z",
            ManifestAssetNamePattern = "LevelUp-Paintkit-*-manifest.json",
            ManifestSchemaVersion = 1
        }
    };

    private static ResourcePackageRelease BuildRelease(string version, string archivePath, byte[] archive)
    {
        var archiveName = Path.GetFileName(archivePath);
        var tag = "paintkit-v" + version;
        var hash = Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant();
        var manifest = new ResourcePackageManifest
        {
            SchemaVersion = 1,
            PackageType = "resource",
            PackageId = "levelup.paintkit",
            PackageVersion = version,
            ReleaseTag = tag,
            Channel = "stable",
            Repository = "https://github.com/petrolpram/737NG-Updates",
            SupportedProducts = ["levelup-737ng"],
            DeliveryMode = "extract",
            ArchiveRoot = "Paintkit",
            TargetDirectory = "Paintkit",
            ExtractedSize = 23,
            Files =
            [
                new ResourcePackageFile
                {
                    Path = "readme.txt",
                    Size = 9,
                    Sha256 = "4715a5374413389bdb81ae61908e43c0f838d08627d0aa0235aea2677b413db1"
                },
                new ResourcePackageFile
                {
                    Path = "Templates/wing.txt",
                    Size = 14,
                    Sha256 = "1435e3176c97627b9bfa90a856a0d10968cd18bdc8384a09ed3aad612f9fbd4d"
                }
            ],
            Archive = new ResourcePackageArchive
            {
                FileName = archiveName,
                Size = archive.LongLength,
                Sha256 = hash
            }
        };
        return new ResourcePackageRelease(
            ResourceReleaseChannel.Stable,
            tag,
            manifest.Repository + "/releases/tag/" + tag,
            $"LevelUp-Paintkit-{version}-manifest.json",
            manifest.Repository + "/releases/download/manifest.json",
            1,
            new string('a', 64),
            manifest.Repository + "/releases/download/" + archiveName,
            manifest);
    }

    private sealed record Fixture(
        ToolStateStore Store,
        ResourcePackageManager Manager,
        ContentPackageCatalogEntry Catalog,
        ResourcePackageProvisionResult Package,
        string ArchivePath,
        byte[] Archive,
        string Destination);
}
