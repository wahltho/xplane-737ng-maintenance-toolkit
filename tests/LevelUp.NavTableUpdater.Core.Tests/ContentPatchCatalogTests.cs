using LevelUp.NavTableUpdater.Core.Content;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class ContentPatchCatalogTests
{
    [Fact]
    public void PackageCatalog_FiltersManagedAndOptionalPackagesByProduct()
    {
        var catalog = ContentPackageCatalog.Parse(BuildCatalog());

        var levelUp = catalog.ForProduct("levelup-737ng");
        var zibo = catalog.ForProduct("zibo-737ng");

        Assert.Equal("1.0.0", catalog.CatalogVersion);
        Assert.Collection(
            levelUp,
            package => Assert.Equal("levelup.vnav", package.PackageId),
            package => Assert.Equal("levelup.fans", package.PackageId),
            package => Assert.Equal("wahltho.yal", package.PackageId),
            package => Assert.Equal("wahltho.yal-hoppiehelper", package.PackageId),
            package => Assert.Equal("levelup.paintkit", package.PackageId));
        Assert.Collection(
            zibo,
            package => Assert.Equal("zibo.vnav", package.PackageId),
            package => Assert.Equal("wahltho.yal", package.PackageId),
            package => Assert.Equal("wahltho.yal-hoppiehelper", package.PackageId));

        Assert.Equal(
            "data/modules/configuration/version.ini",
            zibo.Single(package => package.PackageId == "wahltho.yal").VersionMarkerPath);
        Assert.Empty(zibo.Single(package => package.PackageId == "wahltho.yal-hoppiehelper").VersionMarkerPath);
        Assert.Contains(
            levelUp,
            package => package.PackageId == "wahltho.yal-hoppiehelper"
                && package.SupportedProducts.SequenceEqual(["zibo-737ng", "levelup-737ng"]));
        Assert.Contains(
            levelUp,
            package => package.PackageId == "levelup.paintkit"
                && package.Category is ContentPackageCategory.Resource
                && package.Distribution.Kind is ContentPackageDistributionKind.GitHubResourceRelease);
    }

    [Fact]
    public void PackageCatalog_WithUnknownProductId_RejectsCatalog()
    {
        var json = BuildCatalog().Replace("zibo-737ng", "unknown-aircraft", StringComparison.Ordinal);

        var error = Assert.Throws<InvalidDataException>(() => ContentPackageCatalog.Parse(json));

        Assert.Contains("invalid category or product compatibility", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PackageCatalog_WithUnsafeAssetPattern_RejectsCatalog()
    {
        var json = BuildCatalog().Replace("LevelUp-FANS-v*.zip", "../LevelUp-FANS-v*.zip", StringComparison.Ordinal);

        var error = Assert.Throws<InvalidDataException>(() => ContentPackageCatalog.Parse(json));

        Assert.Contains("unsafe GitHub release archive", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Vnav_IsManagedAndMayBeOfferedAfterAircraftUpdate()
    {
        var descriptor = ContentPatchCatalog.Vnav("test.vnav", "https://github.com/example/vnav");

        Assert.Equal(ContentPatchActivation.Managed, descriptor.Lifecycle.Activation);
        Assert.True(ContentPatchCatalog.MayOfferAfterAircraftUpdate(descriptor));
    }

    [Fact]
    public void FansCdu_IsExplicitOptInAndNeverOfferedAfterAircraftUpdate()
    {
        var descriptor = ContentPatchCatalog.FansCdu;

        Assert.Equal(ContentPatchActivation.ExplicitOptIn, descriptor.Lifecycle.Activation);
        Assert.Contains(ContentPatchTrigger.Manual, descriptor.Lifecycle.Triggers);
        Assert.False(ContentPatchCatalog.MayOfferAfterAircraftUpdate(descriptor));
    }

    [Fact]
    public void BundledCatalog_AdvertisesVerifiedFansCduReleaseContract()
    {
        var catalogPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "LevelUp.NavTableUpdater.App", "Content", "content-package-catalog.json"));
        var catalog = ContentPackageCatalog.Parse(File.ReadAllText(catalogPath));

        var fans = Assert.Single(
            catalog.ForProduct("levelup-737ng"),
            package => package.PackageId == ContentPatchCatalog.FansCdu.ComponentId);
        var descriptor = ContentPatchCatalog.OptionalPatch(fans);

        Assert.Equal("1.3.0", catalog.CatalogVersion);
        Assert.Equal(ContentPackageCategory.OptionalPatch, fans.Category);
        Assert.Equal(ContentPatchActivation.ExplicitOptIn, fans.Activation);
        Assert.Equal("LevelUp-737NG-FANS-CDU-v*.zip", fans.Distribution.AssetNamePattern);
        Assert.Equal(2, fans.Distribution.ManifestSchemaVersion);
        Assert.Equal(ContentPatchCatalog.FansCdu.ComponentId, descriptor.ComponentId);
        Assert.False(ContentPatchCatalog.MayOfferAfterAircraftUpdate(descriptor));
    }

    [Fact]
    public void BundledCatalog_AdvertisesVerifiedLevelUpPaintkitReleaseContract()
    {
        var catalogPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "LevelUp.NavTableUpdater.App", "Content", "content-package-catalog.json"));
        var catalog = ContentPackageCatalog.Parse(File.ReadAllText(catalogPath));

        var paintkit = Assert.Single(
            catalog.ForProduct("levelup-737ng"),
            package => package.PackageId == "levelup.paintkit");

        Assert.Equal("1.3.0", catalog.CatalogVersion);
        Assert.Equal(ContentPackageCategory.Resource, paintkit.Category);
        Assert.Equal(ContentPatchActivation.ExplicitOptIn, paintkit.Activation);
        Assert.Equal(["levelup-737ng"], paintkit.SupportedProducts);
        Assert.Equal("https://github.com/petrolpram/737NG-Updates", paintkit.RepositoryUrl);
        Assert.Equal("userSelectedDirectory", paintkit.InstallScope);
        Assert.Equal(["stable"], paintkit.SupportedChannels);
        Assert.Equal(ContentPackageDistributionKind.GitHubResourceRelease, paintkit.Distribution.Kind);
        Assert.Equal("LevelUp-737NG-Paintkit-*.7z", paintkit.Distribution.AssetNamePattern);
        Assert.Equal(
            "LevelUp-737NG-Paintkit-*-manifest.json",
            paintkit.Distribution.ManifestAssetNamePattern);
        Assert.Equal(1, paintkit.Distribution.ManifestSchemaVersion);
    }

    [Fact]
    public void BundledCatalog_AdvertisesAircraftScopedOptimizedXluaContract()
    {
        var catalogPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "LevelUp.NavTableUpdater.App", "Content", "content-package-catalog.json"));
        var catalog = ContentPackageCatalog.Parse(File.ReadAllText(catalogPath));

        var xlua = Assert.Single(
            catalog.ForProduct("zibo-737ng"),
            package => package.PackageId == "wahltho.optimized-xlua");

        Assert.Equal(ContentPackageCategory.AircraftComponent, xlua.Category);
        Assert.Equal(ContentPatchActivation.ExplicitOptIn, xlua.Activation);
        Assert.Equal(["zibo-737ng", "levelup-737ng"], xlua.SupportedProducts);
        Assert.Equal("aircraftInstallation", xlua.InstallScope);
        Assert.Equal("plugins/xlua", xlua.TargetPath);
        Assert.Equal(ContentPackageDistributionKind.GitHubToolRelease, xlua.Distribution.Kind);
        Assert.Equal("Xlua.*-manifest.json", xlua.Distribution.ManifestAssetNamePattern);
    }

    [Fact]
    public void BundledCatalog_AdvertisesRealbenchLoggerAsProductNeutralXPlaneOverlay()
    {
        var catalogPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "LevelUp.NavTableUpdater.App", "Content", "content-package-catalog.json"));
        var catalog = ContentPackageCatalog.Parse(File.ReadAllText(catalogPath));

        var logger = Assert.Single(
            catalog.ForProduct("zibo-737ng"),
            package => package.PackageId == "wahltho.737ng-realbench-logger");

        Assert.Contains(logger, catalog.ForProduct("levelup-737ng"));
        Assert.Equal(ContentPackageCategory.Tool, logger.Category);
        Assert.Equal(ContentPatchActivation.ExplicitOptIn, logger.Activation);
        Assert.Equal(["zibo-737ng", "levelup-737ng"], logger.SupportedProducts);
        Assert.Equal("xPlaneInstallation", logger.InstallScope);
        Assert.Empty(logger.TargetPath);
        Assert.Equal(["stable"], logger.SupportedChannels);
        Assert.Equal(ContentPackageDistributionKind.GitHubXPlaneOverlayRelease, logger.Distribution.Kind);
        Assert.Equal("737NGRealbenchLogger-*-manifest.json", logger.Distribution.ManifestAssetNamePattern);
        Assert.Equal(2, logger.Distribution.ManifestSchemaVersion);
    }

    private static string BuildCatalog() =>
        """
        {
          "schemaVersion": 1,
          "catalogVersion": "1.0.0",
          "packages": [
            {
              "packageId": "levelup.vnav",
              "displayName": "LevelUp VNAV",
              "description": "Managed tables.",
              "category": "managedContent",
              "activation": "managed",
              "supportedProducts": ["levelup-737ng"],
              "repositoryUrl": "https://github.com/example/levelup-vnav",
              "restartRequired": true,
              "distribution": { "kind": "existingVnav" }
            },
            {
              "packageId": "levelup.fans",
              "displayName": "LevelUp FANS",
              "description": "Optional FANS patch.",
              "category": "optionalPatch",
              "activation": "explicitOptIn",
              "supportedProducts": ["levelup-737ng"],
              "repositoryUrl": "https://github.com/example/levelup-fans",
              "restartRequired": true,
              "distribution": {
                "kind": "gitHubReleaseArchive",
                "assetNamePattern": "LevelUp-FANS-v*.zip",
                "manifestSchemaVersion": 2
              }
            },
            {
              "packageId": "zibo.vnav",
              "displayName": "Zibo VNAV",
              "description": "Managed tables.",
              "category": "managedContent",
              "activation": "managed",
              "supportedProducts": ["zibo-737ng"],
              "repositoryUrl": "https://github.com/example/zibo-vnav",
              "restartRequired": true,
              "distribution": { "kind": "existingVnav" }
            },
            {
              "packageId": "wahltho.yal",
              "displayName": "Yet Another Linda",
              "description": "Optional tool.",
              "category": "tool",
              "activation": "explicitOptIn",
              "supportedProducts": ["zibo-737ng", "levelup-737ng"],
              "repositoryUrl": "https://github.com/example/yal",
              "restartRequired": true,
              "installScope": "xPlaneInstallation",
              "targetPath": "Resources/plugins/YAL",
              "versionMarkerPath": "data/modules/configuration/version.ini",
              "supportedChannels": ["stable", "beta"],
              "distribution": {
                "kind": "gitHubToolRelease",
                "manifestAssetNamePattern": "YAL-*-manifest.json",
                "manifestSchemaVersion": 1
              }
            },
            {
              "packageId": "wahltho.yal-hoppiehelper",
              "displayName": "YAL HoppieHelper",
              "description": "Optional connectivity tool.",
              "category": "tool",
              "activation": "explicitOptIn",
              "supportedProducts": ["zibo-737ng", "levelup-737ng"],
              "repositoryUrl": "https://github.com/example/hoppiehelper",
              "restartRequired": true,
              "installScope": "xPlaneInstallation",
              "targetPath": "Resources/plugins/YAL_HoppieHelper",
              "supportedChannels": ["stable", "beta"],
              "distribution": {
                "kind": "gitHubToolRelease",
                "manifestAssetNamePattern": "YAL-HoppieHelper-*-manifest.json",
                "manifestSchemaVersion": 1
              }
            },
            {
              "packageId": "levelup.paintkit",
              "displayName": "LevelUp Paintkit",
              "description": "Optional paint resource.",
              "category": "resource",
              "activation": "explicitOptIn",
              "supportedProducts": ["levelup-737ng"],
              "repositoryUrl": "https://github.com/example/levelup-updates",
              "restartRequired": false,
              "installScope": "userSelectedDirectory",
              "supportedChannels": ["stable"],
              "distribution": {
                "kind": "gitHubResourceRelease",
                "assetNamePattern": "LevelUp-Paintkit-*.7z",
                "manifestAssetNamePattern": "LevelUp-Paintkit-*-manifest.json",
                "manifestSchemaVersion": 1
              }
            }
          ]
        }
        """;
}
