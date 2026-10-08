using LevelUp.NavTableUpdater.Core.Content;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class ContentPatchCatalogTests
{
    [Theory]
    [InlineData("linux-x64", true)]
    [InlineData("linux-arm64", false)]
    [InlineData("win-x64", false)]
    [InlineData("win-arm64", false)]
    [InlineData("osx-x64", false)]
    [InlineData("osx-arm64", false)]
    public void PublishedCatalog_OffersXLinSpeakOnlyOnSupportedPlatform(string platform, bool available)
    {
        var catalog = ContentPackageCatalog.Parse(File.ReadAllText(PublishedCatalogPath()), new Version(0, 28, 2));
        foreach (var product in new[] { "zibo-737ng", "levelup-737ng" })
        {
            var entries = catalog.ForProduct(product, platform);
            Assert.Equal(available, entries.Any(p => p.PackageId == "wahltho.xlinspeak"));
            Assert.Contains(entries, p => p.PackageId == "wahltho.yal");
        }
        var entry = Assert.Single(catalog.Packages, p => p.PackageId == "wahltho.xlinspeak");
        Assert.Equal(ContentPatchActivation.ExplicitOptIn, entry.Activation);
        Assert.Equal("Resources/plugins/XLinSpeak", entry.TargetPath);
        Assert.Contains("Piper", entry.Description);
    }

    [Fact]
    public void PublishedCatalog_PlatformSupportRequiresNewToolkit()
    {
        Assert.Throws<InvalidDataException>(() => ContentPackageCatalog.Parse(
            File.ReadAllText(PublishedCatalogPath()), new Version(0, 21, 0)));
    }

    [Theory]
    [InlineData("win-x64", true)]
    [InlineData("linux-x64", false)]
    [InlineData("osx-arm64", false)]
    public void PublishedCatalog_AutoUnicomHelperMatchesReleaseContract(string platform, bool available)
    {
        var catalog = ContentPackageCatalog.Parse(File.ReadAllText(PublishedCatalogPath()), new Version(0, 28, 2));
        const string id = "wahltho.yal-autounicomhelper";

        foreach (var product in new[] { "zibo-737ng", "levelup-737ng" })
            Assert.Equal(available, catalog.ForProduct(product, platform).Any(package => package.PackageId == id));

        var entry = Assert.Single(catalog.Packages, package => package.PackageId == id);
        Assert.Equal(ContentPackageCategory.Tool, entry.Category);
        Assert.Equal(ContentPatchActivation.ExplicitOptIn, entry.Activation);
        Assert.Equal("Resources/plugins/YAL_AutoUnicomHelper", entry.TargetPath);
        Assert.Equal(["win-x64"], entry.SupportedPlatforms);
        Assert.Equal(["beta"], entry.SupportedChannels);
        Assert.Equal("YAL-AutoUnicomHelper-*-manifest.json", entry.Distribution.ManifestAssetNamePattern);
    }

    [Fact]
    public void PublishedCatalog_LufthansaLiveryMatchesReleaseContract()
    {
        var catalog = ContentPackageCatalog.Parse(File.ReadAllText(PublishedCatalogPath()), new Version(0, 28, 2));
        const string id = "wahltho.levelup-737ng.livery.lufthansa";

        Assert.DoesNotContain(catalog.ForProduct("zibo-737ng"), package => package.PackageId == id);
        var entry = Assert.Single(catalog.ForProduct("levelup-737ng"), package => package.PackageId == id);
        Assert.Equal(ContentPackageCategory.Livery, entry.Category);
        Assert.Equal(ContentPatchActivation.ExplicitOptIn, entry.Activation);
        Assert.Equal("aircraftLivery", entry.InstallScope);
        Assert.Equal(ContentPackageDistributionKind.GitHubLiveryRelease, entry.Distribution.Kind);
        Assert.Equal("X-Plane-LevelUp-737NG-Lufthansa-Livery-v*.zip", entry.Distribution.AssetNamePattern);
        Assert.Equal("X-Plane-LevelUp-737NG-Lufthansa-Livery-v*.manifest.json", entry.Distribution.ManifestAssetNamePattern);
    }

    [Theory]
    [InlineData("linux")]
    [InlineData("Linux-x64")]
    [InlineData("linux-x64\", \"linux-x64")]
    public void Catalog_InvalidPlatformsAreRejected(string platforms)
    {
        var json = File.ReadAllText(PublishedCatalogPath()).Replace("\"linux-x64\"", "\"" + platforms + "\"");
        Assert.Throws<InvalidDataException>(() => ContentPackageCatalog.Parse(json));
    }

    [Fact]
    public void PublishedCatalog_CpdlcIsAvailableForBothProductsAndOptionalInLevelUpGroup()
    {
        var catalog = ContentPackageCatalog.Parse(File.ReadAllText(PublishedCatalogPath()));
        const string id = "wahltho.zibo-40535.cpdlc";
        var cpdlc = Assert.Single(catalog.ForProduct("zibo-737ng"), p => p.PackageId == id);
        Assert.Contains(cpdlc, catalog.ForProduct("levelup-737ng"));
        Assert.Equal(ContentPackageCategory.CompatibilityPackage, cpdlc.Category);
        Assert.Equal(3, cpdlc.Distribution.ManifestSchemaVersion);
        Assert.Equal("X-Plane-Zibo-LevelUp-737NG-CPDLC-v*.zip", cpdlc.Distribution.AssetNamePattern);
        var group = Assert.Single(catalog.ForProduct("levelup-737ng"),
            p => p.Distribution.Kind == ContentPackageDistributionKind.CatalogGroup);
        var member = Assert.Single(group.Members, m => m.PackageId == id);
        Assert.Equal("cpdlc", member.ModuleId);
        Assert.Equal(LevelUp.NavTableUpdater.Core.Manifest.CompatibilityModulePolicy.Optional, member.Policy);
        Assert.Equal(60, member.InstallationOrder);
        Assert.Equal("compatibility", member.SourceFormat);
        Assert.Equal("package-manifest.json", member.ManifestPath);
    }

    [Fact]
    public void PackageCatalog_AcceptsManagedSchemaThreeCompatibilityPackage()
    {
        var catalog = ContentPackageCatalog.Parse(
            """
            {
              "schemaVersion": 1,
              "catalogVersion": "2.0.0",
              "packages": [
                {
                  "packageId": "levelup.compatibility",
                  "displayName": "LevelUp compatibility package",
                  "description": "Versioned product module set.",
                  "category": "compatibilityPackage",
                  "activation": "managed",
                  "supportedProducts": ["levelup-737ng"],
                  "repositoryUrl": "https://github.com/example/levelup-compatibility",
                  "restartRequired": true,
                  "distribution": {
                    "kind": "gitHubReleaseArchive",
                    "assetNamePattern": "LevelUp-Compatibility-v*.zip",
                    "manifestSchemaVersion": 3
                  }
                }
              ]
            }
            """);

        var package = Assert.Single(catalog.ForProduct("levelup-737ng"));
        Assert.Equal(ContentPackageCategory.CompatibilityPackage, package.Category);
        Assert.Equal(ContentPatchActivation.Managed, package.Activation);
        Assert.Equal(3, package.Distribution.ManifestSchemaVersion);
        Assert.True(ContentPatchCatalog.MayOfferAfterAircraftUpdate(
            ContentPatchCatalog.CompatibilityPackage(package)));
    }

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
    public void PublishedCatalog_AdvertisesVerifiedFansCduReleaseContract()
    {
        var catalog = ContentPackageCatalog.Parse(File.ReadAllText(PublishedCatalogPath()));

        var fans = Assert.Single(
            catalog.ForProduct("levelup-737ng"),
            package => package.PackageId == ContentPatchCatalog.FansCdu.ComponentId);
        var descriptor = ContentPatchCatalog.OptionalPatch(fans);

        Assert.Equal("1.19.0", catalog.CatalogVersion);
        Assert.Equal("0.28.2", catalog.MinimumToolkitVersion);
        Assert.Equal(ContentPackageCategory.OptionalPatch, fans.Category);
        Assert.Equal(ContentPatchActivation.ExplicitOptIn, fans.Activation);
        Assert.Equal("LevelUp-737NG-FANS-CDU-v*.zip", fans.Distribution.AssetNamePattern);
        Assert.Equal(2, fans.Distribution.ManifestSchemaVersion);
        Assert.Equal(ContentPatchCatalog.FansCdu.ComponentId, descriptor.ComponentId);
        Assert.False(ContentPatchCatalog.MayOfferAfterAircraftUpdate(descriptor));
    }

    [Fact]
    public void PublishedCatalog_DeclaresRequiredLevelUpGroupAndOptionalSources()
    {
        var catalog = ContentPackageCatalog.Parse(File.ReadAllText(PublishedCatalogPath()));

        var performance = Assert.Single(
            catalog.ForProduct("levelup-737ng"),
            package => package.PackageId == "x-plane-zibo-40535-tablet-performance-calculator");
        Assert.Contains(performance, catalog.ForProduct("zibo-737ng"));
        Assert.Equal(["zibo-737ng", "levelup-737ng"], performance.SupportedProducts);
        Assert.Equal(ContentPackageCategory.CompatibilityPackage, performance.Category);
        Assert.Equal(ContentPatchActivation.Managed, performance.Activation);
        Assert.Equal(3, performance.Distribution.ManifestSchemaVersion);
        Assert.Equal(
            "zibo-40535-tablet-performance-calculator-v*.zip",
            performance.Distribution.AssetNamePattern);

        var autoJetway = Assert.Single(
            catalog.ForProduct("zibo-737ng"),
            package => package.PackageId == "wahltho.zibo-40535.auto-jetway");
        Assert.Contains(autoJetway, catalog.ForProduct("levelup-737ng"));
        Assert.Equal(["zibo-737ng", "levelup-737ng"], autoJetway.SupportedProducts);
        Assert.Equal(ContentPackageCategory.CompatibilityPackage, autoJetway.Category);
        Assert.Equal(3, autoJetway.Distribution.ManifestSchemaVersion);
        Assert.Equal("X-Plane-Zibo-Auto-Jetway-v*.zip", autoJetway.Distribution.AssetNamePattern);

        var group = Assert.Single(catalog.ForProduct("levelup-737ng"),
            package => package.Distribution.Kind is ContentPackageDistributionKind.CatalogGroup);
        Assert.Equal(new[] { "vnav", "fans-cdu", "weight-and-balance" },
            group.Members.Where(m => m.Policy is LevelUp.NavTableUpdater.Core.Manifest.CompatibilityModulePolicy.Required)
                .OrderBy(m => m.InstallationOrder).Select(m => m.ModuleId));
        Assert.Equal(new[] { "tablet-performance-calculator", "auto-jetway", "cpdlc", "vref", "27k-sfp", "intentional-fixes-levelup", "gse" },
            group.Members.Where(m => m.Policy is LevelUp.NavTableUpdater.Core.Manifest.CompatibilityModulePolicy.Optional)
                .OrderBy(m => m.InstallationOrder).Select(m => m.ModuleId));
        Assert.DoesNotContain(catalog.ForProduct("zibo-737ng"), package => package.PackageId == group.PackageId);

        var ziboGroup = Assert.Single(catalog.ForProduct("zibo-737ng"),
            package => package.Distribution.Kind is ContentPackageDistributionKind.CatalogGroup);
        Assert.DoesNotContain(ziboGroup.Members,
            member => member.Policy is LevelUp.NavTableUpdater.Core.Manifest.CompatibilityModulePolicy.Required);
        Assert.Equal(new[] { "vnav", "tablet-performance-calculator", "auto-jetway", "cpdlc", "vref", "27k-sfp", "intentional-fixes-zibo" },
            ziboGroup.Members.OrderBy(m => m.InstallationOrder).Select(m => m.ModuleId));
        Assert.All(ziboGroup.Members, member => Assert.Equal(
            LevelUp.NavTableUpdater.Core.Manifest.CompatibilityModulePolicy.Optional, member.Policy));

        var intentional = Assert.Single(catalog.ForProduct("zibo-737ng"),
            package => package.PackageId == "wahltho.zibo-40535.intentional-fixes");
        Assert.Contains(intentional, catalog.ForProduct("levelup-737ng"));
        Assert.Equal(5, intentional.Distribution.ManifestSchemaVersion);
        Assert.Equal("X-Plane-Zibo-LevelUp-737NG-Intentional-Fixes-MTK-v*.zip",
            intentional.Distribution.AssetNamePattern);

        var gse = Assert.Single(catalog.ForProduct("levelup-737ng"),
            package => package.PackageId == "jt8d17.levelup-737ng.gse");
        Assert.DoesNotContain(catalog.ForProduct("zibo-737ng"), package => package.PackageId == gse.PackageId);
        Assert.Equal(ContentPackageCategory.CompatibilityPackage, gse.Category);
        Assert.Equal(4, gse.Distribution.ManifestSchemaVersion);
        Assert.Equal("levelup-gse-*.zip", gse.Distribution.AssetNamePattern);
        var gseMember = Assert.Single(group.Members, member => member.PackageId == gse.PackageId);
        Assert.Equal("gse", gseMember.ModuleId);
        Assert.Equal(LevelUp.NavTableUpdater.Core.Manifest.CompatibilityModulePolicy.Optional, gseMember.Policy);
        Assert.Equal("compatibility", gseMember.SourceFormat);
        Assert.Equal("package-manifest.json", gseMember.ManifestPath);
    }

    [Fact]
    public void PublishedCatalog_VrefBetaIsOptionalForBothProductsAndRequiresLoaderMigrationSupport()
    {
        var json = File.ReadAllText(PublishedCatalogPath());
        Assert.Throws<InvalidDataException>(() => ContentPackageCatalog.Parse(json, new Version(0, 21, 1)));
        var catalog = ContentPackageCatalog.Parse(json, new Version(0, 28, 2));
        const string id = "wahltho.levelup-737ng.vref";
        var entry = Assert.Single(catalog.Packages, package => package.PackageId == id);
        Assert.Equal("VREF tables (Beta)", entry.DisplayName);
        Assert.Contains("simulator validation remains open", entry.Description);
        Assert.Equal(ContentPackageCategory.CompatibilityPackage, entry.Category);
        Assert.Equal(ContentPatchActivation.Managed, entry.Activation);
        Assert.Equal(3, entry.Distribution.ManifestSchemaVersion);
        Assert.Equal("https://github.com/wahltho/X-Plane-LevelUp-737NG-VREF", entry.RepositoryUrl);
        Assert.Equal("X-Plane-LevelUp-737NG-VREF-v*.zip", entry.Distribution.AssetNamePattern);
        Assert.True(entry.RestartRequired);

        foreach (var product in new[] { "zibo-737ng", "levelup-737ng" })
        {
            Assert.Contains(entry, catalog.ForProduct(product));
            var group = Assert.Single(catalog.ForProduct(product),
                package => package.Distribution.Kind == ContentPackageDistributionKind.CatalogGroup);
            var member = Assert.Single(group.Members, candidate => candidate.PackageId == id);
            Assert.Equal("vref", member.ModuleId);
            Assert.Equal(LevelUp.NavTableUpdater.Core.Manifest.CompatibilityModulePolicy.Optional, member.Policy);
            Assert.Equal(65, member.InstallationOrder);
            Assert.Equal("compatibility", member.SourceFormat);
            Assert.Equal("package-manifest.json", member.ManifestPath);
            Assert.Equal(entry.Distribution.AssetNamePattern, member.AssetNamePattern);
            var intentional = Assert.Single(group.Members,
                candidate => candidate.PackageId == "wahltho.zibo-40535.intentional-fixes");
            Assert.True(member.InstallationOrder < intentional.InstallationOrder);
        }
    }

    [Fact]
    public void PublishedCatalog_27kBetaIsOptionalForBothProductsWithExistingOwnershipPolicy()
    {
        var catalog = ContentPackageCatalog.Parse(File.ReadAllText(PublishedCatalogPath()), new Version(0, 28, 2));
        const string id = "wahltho.zibo-levelup-737ng.27k-sfp";
        var entry = Assert.Single(catalog.Packages, package => package.PackageId == id);
        Assert.Equal("737-800WSFP2 27K / SFP (Beta)", entry.DisplayName);
        Assert.Contains("Simulator validation", entry.Description);
        Assert.Equal("0.28.2", catalog.MinimumToolkitVersion);
        Assert.Equal(ContentPackageDistributionKind.GitHubReleaseArchive, entry.Distribution.Kind);
        Assert.Equal(3, entry.Distribution.ManifestSchemaVersion);
        Assert.Equal("X-Plane-Zibo-LevelUp-737NG-27K-SFP-v*.zip", entry.Distribution.AssetNamePattern);
        Assert.True(entry.RestartRequired);
        var policy = Assert.Single(catalog.OwnershipPolicies, policy => policy.PackageId == id);
        Assert.Contains("27k-sfp", policy.ModuleIds);
        Assert.Contains(".sfp27-standalone", policy.StandaloneEvidencePaths);
        Assert.Contains(".sfp27-standalone.lock", policy.StandaloneEvidencePaths);
        foreach (var product in new[] { "zibo-737ng", "levelup-737ng" })
        {
            Assert.Contains(entry, catalog.ForProduct(product));
            var group = Assert.Single(catalog.ForProduct(product),
                package => package.Distribution.Kind == ContentPackageDistributionKind.CatalogGroup);
            var member = Assert.Single(group.Members, member => member.PackageId == id);
            Assert.Equal("27k-sfp", member.ModuleId);
            Assert.Equal(LevelUp.NavTableUpdater.Core.Manifest.CompatibilityModulePolicy.Optional, member.Policy);
            Assert.Equal(68, member.InstallationOrder);
            Assert.Equal("compatibility", member.SourceFormat);
            Assert.Equal("package-manifest.json", member.ManifestPath);
            Assert.Equal(entry.Distribution.AssetNamePattern, member.AssetNamePattern);
            Assert.True(group.Members.Single(member => member.ModuleId == "vref").InstallationOrder < member.InstallationOrder);
            Assert.True(group.Members.Single(member => member.InstallationOrder == 70).InstallationOrder > member.InstallationOrder);
        }
    }

    [Fact]
    public void PublishedCatalog_AdvertisesVerifiedLevelUpPaintkitReleaseContract()
    {
        var catalog = ContentPackageCatalog.Parse(File.ReadAllText(PublishedCatalogPath()));

        var paintkit = Assert.Single(
            catalog.ForProduct("levelup-737ng"),
            package => package.PackageId == "levelup.paintkit");

        Assert.Equal("1.19.0", catalog.CatalogVersion);
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
    public void PublishedCatalog_AdvertisesAircraftScopedOptimizedXluaContract()
    {
        var catalog = ContentPackageCatalog.Parse(File.ReadAllText(PublishedCatalogPath()));

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
        Assert.Null(xlua.Distribution.ManifestSchemaVersion);
        Assert.Equal(1, xlua.Distribution.ManifestSchemaVersionFor("stable"));
        Assert.Equal(3, xlua.Distribution.ManifestSchemaVersionFor("beta"));
    }

    [Fact]
    public void PublishedCatalog_AdvertisesRealbenchLoggerAsProductNeutralXPlaneOverlay()
    {
        var catalog = ContentPackageCatalog.Parse(File.ReadAllText(PublishedCatalogPath()));

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

    [Fact]
    public void PublishedCatalog_AdvertisesYanshAsProductNeutralXPlaneTool()
    {
        var catalog = ContentPackageCatalog.Parse(File.ReadAllText(PublishedCatalogPath()));

        var yansh = Assert.Single(
            catalog.ForProduct("zibo-737ng"),
            package => package.PackageId == "olivierbutler.yansh");

        Assert.Contains(yansh, catalog.ForProduct("levelup-737ng"));
        Assert.Equal(ContentPackageCategory.Tool, yansh.Category);
        Assert.Equal(ContentPatchActivation.ExplicitOptIn, yansh.Activation);
        Assert.Equal(["zibo-737ng", "levelup-737ng"], yansh.SupportedProducts);
        Assert.Equal("https://github.com/olivierbutler/YANSH", yansh.RepositoryUrl);
        Assert.Equal("xPlaneInstallation", yansh.InstallScope);
        Assert.Equal("Resources/plugins/YANSH", yansh.TargetPath);
        Assert.Equal(["stable"], yansh.SupportedChannels);
        Assert.Equal(ContentPackageDistributionKind.GitHubToolRelease, yansh.Distribution.Kind);
        Assert.Equal("YANSH-*-manifest.json", yansh.Distribution.ManifestAssetNamePattern);
        Assert.Equal(1, yansh.Distribution.ManifestSchemaVersion);
    }

    [Fact]
    public void ChannelSpecificToolManifestSchemas_AcceptStable1AndBeta3()
    {
        var catalog = ContentPackageCatalog.Parse(BuildChannelSchemaCatalog(
            "\"manifestSchemaVersions\": { \"stable\": 1, \"beta\": 3 }"));

        var distribution = Assert.Single(catalog.Packages).Distribution;
        Assert.Null(distribution.ManifestSchemaVersion);
        Assert.Equal(1, distribution.ManifestSchemaVersionFor("stable"));
        Assert.Equal(3, distribution.ManifestSchemaVersionFor("beta"));
    }

    [Theory]
    [InlineData("\"manifestSchemaVersion\": 1, \"manifestSchemaVersions\": { \"stable\": 1, \"beta\": 3 }")]
    [InlineData("\"manifestSchemaVersions\": { \"stable\": 1 }")]
    [InlineData("\"manifestSchemaVersions\": { \"stable\": 1, \"beta\": 2 }")]
    public void ChannelSpecificToolManifestSchemas_RejectAmbiguousIncompleteOrUnsupportedContracts(string schemaContract)
    {
        Assert.Throws<InvalidDataException>(() =>
            ContentPackageCatalog.Parse(BuildChannelSchemaCatalog(schemaContract)));
    }

    private static string BuildChannelSchemaCatalog(string schemaContract) => $$"""
        {
          "schemaVersion": 1,
          "catalogVersion": "1.0.0",
          "packages": [
            {
              "packageId": "wahltho.optimized-xlua",
              "displayName": "Optimized XLua",
              "description": "Aircraft runtime.",
              "category": "aircraftComponent",
              "activation": "explicitOptIn",
              "supportedProducts": ["zibo-737ng", "levelup-737ng"],
              "repositoryUrl": "https://github.com/wahltho/XLua",
              "restartRequired": true,
              "installScope": "aircraftInstallation",
              "targetPath": "plugins/xlua",
              "supportedChannels": ["stable", "beta"],
              "distribution": {
                "kind": "gitHubToolRelease",
                "manifestAssetNamePattern": "Xlua.*-manifest.json",
                {{schemaContract}}
              }
            }
          ]
        }
        """;

    private static string PublishedCatalogPath() =>
        Path.Combine(AppContext.BaseDirectory, "Content", "content-package-catalog.json");

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
