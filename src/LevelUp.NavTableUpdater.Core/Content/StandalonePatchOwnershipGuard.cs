using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Content;

// Standalone installers do not write MTK ownership or backup-chain records.
// Until a package-specific handoff can prove that chain, never adopt their
// patched output as the original aircraft file.
internal static class StandalonePatchOwnershipGuard
{
    private const string Fms = "plugins/xlua/scripts/B738.a_fms/B738.a_fms.lua";
    private const string Tablet = "plugins/xlua/scripts/B738.tablet/B738.tablet.lua";

    private sealed record StateMarker(string Name, string Path, string Remedy, string[] Targets);
    private sealed record LegacyMarker(string Name, string PackageId, string ModuleId,
        string Target, string Signature, string Remedy, string[] Backups);

    private static readonly StateMarker[] StateMarkers =
    [
        new("LevelUp FANS CDU", ".levelup-fans-cdu-patch/state.json",
            "Run the FANS CDU standalone installer with 'uninstall --aircraft-root <aircraft folder>'.",
            ["objects/737_cockpit_ovhd2.obj", "objects/737cockpit_overhead2.dds",
             "objects/737cockpit_overhead2_LIT.dds", "objects/737cockpit_overhead2_NML.png", Tablet]),
        new("AUTO JETWAY", ".zibo-auto-jetway-patch/state.json",
            "Run the AUTO JETWAY standalone installer with 'uninstall --aircraft-root <aircraft folder>'.", [Fms, Tablet]),
        new("CPDLC FANS PAGES", ".zibo-cpdlc-patch/state.json",
            "Run the CPDLC standalone installer with 'uninstall --aircraft-root <aircraft folder>'.", [Fms])
    ];

    private static readonly LegacyMarker[] LegacyMarkers =
    [
        new("Zibo VNAV descent tables", "x-plane-zibo-vnav-descent-tables", "vnav", Fms,
            "-- BEGIN ZIBO_VNAV_DESCENT_TABLES",
            "Follow the Zibo VNAV standalone README's backup restore procedure, preserving any other Lua edits.",
            ["plugins/xlua/scripts/B738.a_fms/B738.a_fms.backup"]),
        new("LevelUp VNAV descent tables", "x-plane-levelup-737ng-vnav-descent-tables", "vnav", Fms,
            "-- BEGIN LEVELUP_VNAV_DESCENT_TABLES",
            "Follow the LevelUp VNAV standalone README's backup restore procedure, preserving any other Lua edits.",
            ["plugins/xlua/scripts/B738.a_fms/B738.a_fms.backup"]),
        new("LevelUp Weight & Balance", "wahltho.levelup-737ng.weight-and-balance", "weight-and-balance", Tablet,
            "-- BEGIN LEVELUP_NG_WB", "Run the Weight & Balance standalone installer with '--uninstall'.",
            ["plugins/xlua/scripts/B738.tablet/B738.tablet.lua.levelupngwb.backup",
                                          "plugins/xlua/scripts/B738.tablet/B738.tablet.lua.levelup700wb.backup"]),
        new("LevelUp Weight & Balance", "wahltho.levelup-737ng.weight-and-balance", "weight-and-balance", Fms,
            "-- BEGIN LEVELUP_NG_WB", "Run the Weight & Balance standalone installer with '--uninstall'.",
            ["plugins/xlua/scripts/B738.a_fms/B738.a_fms.lua.levelupngwb.backup"]),
        new("Tablet Performance Calculator", "x-plane-zibo-40535-tablet-performance-calculator",
            "tablet-performance-calculator", Tablet, "-- BEGIN UPSTREAM_TABLET_PERF_CALC",
            "Run the Tablet Performance Calculator standalone installer with '--uninstall'.",
            ["plugins/xlua/scripts/B738.tablet/B738.tablet.lua.backup"])
    ];

    public static string? FindConflict(string aircraftRoot, IEnumerable<string> targetPaths,
        IReadOnlyDictionary<string, ContentComponentState>? toolkitComponents)
    {
        var targets = targetPaths.ToHashSet(StringComparer.Ordinal);
        foreach (var marker in StateMarkers.Where(marker => marker.Targets.Any(targets.Contains)))
        {
            if (Exists(aircraftRoot, marker.Path))
                return Blocked(marker.Name, marker.Path, marker.Remedy);
        }

        foreach (var marker in LegacyMarkers.Where(marker => targets.Contains(marker.Target)))
        {
            var targetPath = ContentPatchPathSafety.ResolveTarget(aircraftRoot, marker.Target, "Standalone patch target");
            if (!File.Exists(targetPath) || !File.ReadAllText(targetPath).Contains(marker.Signature, StringComparison.Ordinal))
                continue;

            var toolkitOwnsPatch = toolkitComponents?.Values.Any(component =>
                (component.ComponentId == marker.PackageId && component.Files.Any(file => file.RelativePath == marker.Target))
                || (component.Sources.Any(source => source.PackageId == marker.PackageId)
                    && component.EnabledModules.Contains(marker.ModuleId, StringComparer.Ordinal)
                    && component.Files.Any(file => file.RelativePath == marker.Target))) == true;
            if (!toolkitOwnsPatch)
                return Blocked(marker.Name, marker.Backups.FirstOrDefault(path => Exists(aircraftRoot, path)) ?? marker.Target,
                    marker.Remedy);
        }

        return null;
    }

    public static string? FindAircraftUpdateConflict(string aircraftRoot,
        IReadOnlyDictionary<string, ContentComponentState>? toolkitComponents)
    {
        // A full aircraft replacement can discard any of these targets, while the
        // automatic patch follow-up may rewrite them after an incremental update.
        var patchTargets = StateMarkers.SelectMany(marker => marker.Targets)
            .Concat(LegacyMarkers.Select(marker => marker.Target))
            .Distinct(StringComparer.Ordinal);
        try { return FindConflict(aircraftRoot, patchTargets, toolkitComponents); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return $"Cannot verify standalone patch ownership before the aircraft update: {ex.Message}";
        }
    }

    private static bool Exists(string root, string relativePath)
    {
        var path = ContentPatchPathSafety.ResolveTarget(root, relativePath, "Standalone patch evidence");
        return File.Exists(path) || Directory.Exists(path);
    }

    private static string Blocked(string name, string evidence, string remedy) =>
        $"{name} has standalone or unowned patch evidence at {evidence}. " +
        "The Toolkit cannot verify one complete backup chain and will not adopt the patched file as an original. " +
        $"{remedy} Then rescan in the Toolkit. " +
        "Do not delete its state or backup files by hand.";
}
