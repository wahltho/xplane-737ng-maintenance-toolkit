using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class ToolStateStoreTests
{
    [Fact]
    public void CreateBackupPath_WhenCustomBackupRootIsConfigured_UsesCustomRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"xplane-737ng-state-tests-{Guid.NewGuid():N}");
        var stateRoot = Path.Combine(root, "state");
        var backupRoot = Path.Combine(root, "user-backups");
        var store = new ToolStateStore(stateRoot, backupRoot);
        var variant = CreateVariant(root);

        var backupPath = store.CreateBackupPath(
            variant,
            Path.Combine(root, "B738.a_fms.lua"),
            new DateTimeOffset(2026, 7, 18, 6, 0, 0, TimeSpan.Zero));

        Assert.StartsWith(Path.GetFullPath(backupRoot) + Path.DirectorySeparatorChar, backupPath, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.Combine(Path.GetFullPath(stateRoot), "backups"), backupPath, StringComparison.Ordinal);
        Assert.EndsWith(Path.Combine("zibo-737-800x", "20260718T060000000Z", "B738.a_fms.lua"), backupPath, StringComparison.Ordinal);
    }

    [Fact]
    public void SetBackupRootPath_ChangesFutureBackupPathsOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), $"xplane-737ng-state-tests-{Guid.NewGuid():N}");
        var firstBackupRoot = Path.Combine(root, "first");
        var secondBackupRoot = Path.Combine(root, "second");
        var store = new ToolStateStore(Path.Combine(root, "state"), firstBackupRoot);
        var variant = CreateVariant(root);

        var firstPath = store.CreateBackupPath(variant, Path.Combine(root, "b738_4k.acf"), DateTimeOffset.UnixEpoch);
        store.SetBackupRootPath(secondBackupRoot);
        var secondPath = store.CreateBackupPath(variant, Path.Combine(root, "b738_4k.acf"), DateTimeOffset.UnixEpoch);

        Assert.StartsWith(Path.GetFullPath(firstBackupRoot) + Path.DirectorySeparatorChar, firstPath, StringComparison.Ordinal);
        Assert.StartsWith(Path.GetFullPath(secondBackupRoot) + Path.DirectorySeparatorChar, secondPath, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateProductBackupPath_PreservesExactRelativePathAndUsesProductScope()
    {
        var root = Path.Combine(Path.GetTempPath(), $"xplane-737ng-state-tests-{Guid.NewGuid():N}");
        var backupRoot = Path.Combine(root, "backups");
        var store = new ToolStateStore(Path.Combine(root, "state"), backupRoot);
        var variant = CreateVariant(
            root,
            aircraftId: "levelup-737-600",
            family: "levelup-737ng",
            acfFileName: "737_60NG.acf");
        var relativePath = Path.Combine("plugins", "xlua", "scripts", "LU & Zibo Version.txt");

        var backupPath = store.CreateProductBackupPath(
            variant,
            Path.Combine(root, relativePath),
            new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero),
            relativePath);

        Assert.EndsWith(
            Path.Combine("levelup-737ng-series", "20260729T120000000Z", relativePath),
            backupPath,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("folder/../../outside.txt")]
    public void CreateProductBackupPath_WhenRelativePathTraverses_Throws(string relativePath)
    {
        var root = Path.Combine(Path.GetTempPath(), $"xplane-737ng-state-tests-{Guid.NewGuid():N}");
        var store = new ToolStateStore(Path.Combine(root, "state"), Path.Combine(root, "backups"));
        var variant = CreateVariant(root);

        Assert.Throws<InvalidDataException>(() => store.CreateProductBackupPath(
            variant,
            Path.Combine(root, "source.txt"),
            DateTimeOffset.UnixEpoch,
            relativePath));
    }

    [Fact]
    public void ProductTarget_MigratesProductRecordsAcrossLegacyVariantTargetsOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), $"xplane-737ng-state-tests-{Guid.NewGuid():N}");
        var store = new ToolStateStore(Path.Combine(root, "state"), Path.Combine(root, "backups"));
        var sixHundred = CreateVariant(root, "levelup-737-600", "levelup-737ng", "737_60NG.acf");
        var sevenHundred = CreateVariant(root, "levelup-737-700", "levelup-737ng", "737_70NG.acf");
        var productBackup = new BackupRecord
        {
            Operation = "AircraftUpdatePreImage",
            SourcePath = Path.Combine(root, "737_60NG.acf"),
            BackupPath = Path.Combine(root, "backup", "737_60NG.acf"),
            CreatedUtc = DateTimeOffset.UnixEpoch
        };
        var viewBackup = new BackupRecord
        {
            Operation = "QuickViewCgAdapt",
            SourcePath = sixHundred.PrefsPath,
            BackupPath = Path.Combine(root, "backup", "737_60NG_prefs.txt"),
            CreatedUtc = DateTimeOffset.UnixEpoch
        };
        store.UpdateTarget(sixHundred, state =>
        {
            state.InstalledAircraftUpdateVersion = "2.S1.50C";
            state.LastAircraftUpdateUtc = DateTimeOffset.UnixEpoch;
            state.LastOperation = "AircraftUpdateIncrementalApply";
            state.Backups.Add(productBackup);
            state.Backups.Add(viewBackup);
        });

        var migrated = Assert.IsType<AircraftToolState>(store.TryGetProductTarget(sevenHundred));

        Assert.Equal("levelup-737ng-series", migrated.AircraftId);
        Assert.Equal("2.S1.50C", migrated.InstalledAircraftUpdateVersion);
        Assert.Equal(productBackup.SourcePath, Assert.Single(migrated.Backups).SourcePath);

        store.UpdateProductTarget(sevenHundred, state => state.LastOperation = "AircraftUpdateRestore");
        var document = store.Load();
        Assert.Equal(2, document.Aircraft.Count);
        Assert.Contains(document.Aircraft.Values, state => state.AircraftId == "levelup-737-600"
            && state.Backups.Any(record => record.Operation == "QuickViewCgAdapt"));
        Assert.Contains(document.Aircraft.Values, state => state.AircraftId == "levelup-737ng-series"
            && state.Backups.Count == 1);
    }

    [Fact]
    public void ToolInstallationState_IsSeparatedByXPlaneRootAndPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), $"xplane-737ng-state-tests-{Guid.NewGuid():N}");
        var store = new ToolStateStore(Path.Combine(root, "state"), Path.Combine(root, "backups"));
        var firstXPlane = Path.Combine(root, "xp-one");
        var secondXPlane = Path.Combine(root, "xp-two");
        Directory.CreateDirectory(firstXPlane);
        Directory.CreateDirectory(secondXPlane);

        store.UpdateToolInstallation(firstXPlane, "wahltho.yal", state => state.InstalledVersion = "4.7");
        store.UpdateToolInstallation(secondXPlane, "wahltho.yal", state => state.InstalledVersion = "4.8-beta.1");

        Assert.Equal("4.7", store.TryGetToolInstallation(firstXPlane, "wahltho.yal")?.InstalledVersion);
        Assert.Equal("4.8-beta.1", store.TryGetToolInstallation(secondXPlane, "wahltho.yal")?.InstalledVersion);
        Assert.Equal(4, store.Load().SchemaVersion);
    }

    [Fact]
    public void ResourceInstallationState_IsSeparatedFromToolInstallations()
    {
        var root = Path.Combine(Path.GetTempPath(), $"xplane-737ng-state-tests-{Guid.NewGuid():N}");
        var store = new ToolStateStore(Path.Combine(root, "state"), Path.Combine(root, "backups"));

        store.UpdateResourceInstallation("levelup.paintkit", state =>
        {
            state.PackageVersion = "2.S1";
            state.TargetPath = Path.Combine(root, "LevelUp Paintkit");
        });

        Assert.Equal("2.S1", store.TryGetResourceInstallation("levelup.paintkit")?.PackageVersion);
        Assert.Empty(store.Load().ToolInstallations);

        store.RemoveResourceInstallation("levelup.paintkit");
        Assert.Null(store.TryGetResourceInstallation("levelup.paintkit"));
    }

    private static AircraftVariantViewAnalysis CreateVariant(
        string root,
        string aircraftId = "zibo-737-800x",
        string family = "Zibo",
        string acfFileName = "b738_4k.acf") =>
        new(
            AircraftId: aircraftId,
            DisplayName: "Boeing 737-800",
            Family: family,
            AcfPath: Path.Combine(root, acfFileName),
            PrefsPath: Path.Combine(root, Path.GetFileNameWithoutExtension(acfFileName) + "_prefs.txt"),
            Source: "test",
            SourceRef: "test",
            SourceVersion: "1",
            LocalVersion: null,
            AcfVersion: null,
            FileWriterVersion: null,
            CurrentCgYFeet: null,
            CurrentCgZFeet: null,
            ReferenceCgYFeet: 0,
            ReferenceCgZFeet: 0,
            DeltaYFeet: null,
            DeltaZFeet: null,
            DeltaYMeters: null,
            DeltaZMeters: null,
            Status: "test",
            IdentityStatus: "test",
            QuickViewStatus: "test",
            DefaultViewStatus: "test");
}
