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

    private static AircraftVariantViewAnalysis CreateVariant(string root) =>
        new(
            AircraftId: "zibo-737-800x",
            DisplayName: "Boeing 737-800",
            Family: "Zibo",
            AcfPath: Path.Combine(root, "b738_4k.acf"),
            PrefsPath: Path.Combine(root, "b738_4k_prefs.txt"),
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
