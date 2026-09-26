using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.Manifest;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class StandalonePatchOwnershipGuardTests
{
    private const string Fms = "plugins/xlua/scripts/B738.a_fms/B738.a_fms.lua";
    private const string Tablet = "plugins/xlua/scripts/B738.tablet/B738.tablet.lua";

    [Theory]
    [InlineData(".levelup-fans-cdu-patch/state.json", "objects/737_cockpit_ovhd2.obj")]
    [InlineData(".zibo-auto-jetway-patch/state.json", Fms)]
    [InlineData(".zibo-cpdlc-patch/state.json", Fms)]
    public void StandaloneState_BlocksOnlyItsTargets(string statePath, string target)
    {
        using var directory = new DeclarativePatchManifestTests.TemporaryDirectory();
        Write(directory.Path, statePath, "{}");

        var conflict = StandalonePatchOwnershipGuard.FindConflict(directory.Path, [target], null);

        Assert.Contains("standalone", conflict!, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(StandalonePatchOwnershipGuard.FindAircraftUpdateConflict(directory.Path, null));
        Assert.Null(StandalonePatchOwnershipGuard.FindConflict(directory.Path, ["unrelated.txt"], null));
    }

    [Theory]
    [InlineData(Fms, "-- BEGIN ZIBO_VNAV_DESCENT_TABLES DOFILE", "plugins/xlua/scripts/B738.a_fms/B738.a_fms.backup")]
    [InlineData(Fms, "-- BEGIN LEVELUP_VNAV_DESCENT_TABLES DOFILE", "plugins/xlua/scripts/B738.a_fms/B738.a_fms.backup")]
    [InlineData(Fms, "-- BEGIN LEVELUP_NG_WB FMS_EMPTY_WEIGHT", "plugins/xlua/scripts/B738.a_fms/B738.a_fms.lua.levelupngwb.backup")]
    [InlineData(Tablet, "-- BEGIN LEVELUP_NG_WB DOFILE", "plugins/xlua/scripts/B738.tablet/B738.tablet.lua.levelupngwb.backup")]
    [InlineData(Tablet, "-- BEGIN UPSTREAM_TABLET_PERF_CALC DOFILE", "plugins/xlua/scripts/B738.tablet/B738.tablet.lua.backup")]
    public void ActiveLegacyPatch_BlocksButStaleBackupAloneDoesNot(string target, string signature, string backup)
    {
        using var directory = new DeclarativePatchManifestTests.TemporaryDirectory();
        Write(directory.Path, target, "stock");
        Write(directory.Path, backup, "original");

        Assert.Null(StandalonePatchOwnershipGuard.FindConflict(directory.Path, [target], null));
        Assert.Null(StandalonePatchOwnershipGuard.FindAircraftUpdateConflict(directory.Path, null));

        Write(directory.Path, target, $"stock\n{signature}\n");
        Assert.NotNull(StandalonePatchOwnershipGuard.FindConflict(directory.Path, [target], null));
    }

    [Fact]
    public void ActiveLegacyPatchWithoutBackup_IsNotAdoptedAsOriginal()
    {
        using var directory = new DeclarativePatchManifestTests.TemporaryDirectory();
        Write(directory.Path, Tablet, "-- BEGIN UPSTREAM_TABLET_PERF_CALC DOFILE\n");

        var conflict = StandalonePatchOwnershipGuard.FindConflict(directory.Path, [Tablet], null);

        Assert.Contains("backup chain", conflict!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolkitOwnedMarkerWithoutStandaloneBackup_RemainsUsable()
    {
        using var directory = new DeclarativePatchManifestTests.TemporaryDirectory();
        Write(directory.Path, Tablet, "-- BEGIN UPSTREAM_TABLET_PERF_CALC DOFILE\n");
        Write(directory.Path, "plugins/xlua/scripts/B738.tablet/B738.tablet.lua.backup", "stale backup");
        var owned = new ContentComponentState
        {
            ComponentId = "wahltho.levelup-737ng.maintenance",
            EnabledModules = ["tablet-performance-calculator"],
            Sources = [new ResolvedCatalogSource { PackageId = "x-plane-zibo-40535-tablet-performance-calculator" }],
            Files = [new ContentComponentFileState { RelativePath = Tablet }]
        };

        Assert.Null(StandalonePatchOwnershipGuard.FindConflict(directory.Path, [Tablet],
            new Dictionary<string, ContentComponentState> { [owned.ComponentId] = owned }));
        Assert.Null(StandalonePatchOwnershipGuard.FindAircraftUpdateConflict(directory.Path,
            new Dictionary<string, ContentComponentState> { [owned.ComponentId] = owned }));
    }

    private static void Write(string root, string relativePath, string text)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
