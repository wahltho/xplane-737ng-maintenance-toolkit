using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Content;

// Recognition rules come exclusively from the catalog and saved catalog snapshots.
internal static class StandalonePatchOwnershipGuard
{
    public static string? FindAircraftUpdateConflict(string aircraftRoot,
        IReadOnlyDictionary<string, ContentComponentState>? toolkitComponents)
    {
        var catalog = ContentPackageCatalog.LoadBundled();
        return FindConflict(aircraftRoot, catalog.OwnershipPolicies.SelectMany(policy => policy.TargetPaths),
            toolkitComponents, catalog);
    }

    public static string? FindConflict(string aircraftRoot, IEnumerable<string> targetPaths,
        IReadOnlyDictionary<string, ContentComponentState>? toolkitComponents,
        ContentPackageCatalog? catalog = null, IReadOnlyList<BackupRecord>? journal = null,
        string? product = null, KnownAircraftBaselines? baselines = null) =>
        ContentPatchOwnershipVerifier.FindConflict(aircraftRoot, targetPaths, toolkitComponents,
            catalog ?? ContentPackageCatalog.LoadBundled(), journal, product, baselines);

    public static string? FindAircraftUpdateConflict(string aircraftRoot,
        ContentInstallationToolState? installation, ContentPackageCatalog catalog, string? product = null)
    {
        try
        {
            ContentPatchOwnershipVerifier.CheckAircraft(aircraftRoot, installation, catalog, product);
            return null;
        }
        catch (Exception ex) when (ContentPatchOwnershipVerifier.IsVerificationFailure(ex))
        {
            return $"Cannot verify patch ownership before changing aircraft files: {ex.Message}";
        }
    }
}
