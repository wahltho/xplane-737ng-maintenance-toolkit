using System.Text.Json;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Upstream;

// An aircraft delta replaces only its declared files. Ownership of all other
// targets and the user's module selection must survive that transaction.
internal static class AircraftContentOwnership
{
    public static AircraftContentGenerationState Capture(ContentInstallationToolState installation, AircraftToolState product) =>
        Clone(new AircraftContentGenerationState
        {
            InstallationComponents = installation.ContentComponents,
            ProductComponents = product.ContentComponents,
            PendingContentModules = installation.PendingContentModules,
            InstalledContentPackageId = product.InstalledContentPackageId,
            InstalledContentPackageVersion = product.InstalledContentPackageVersion,
            LastContentOperationUtc = product.LastContentOperationUtc
        });

    public static void ReplaceTargets(ContentInstallationToolState installation, AircraftToolState product,
        IReadOnlySet<string> paths, AircraftContentGenerationState? restore = null)
    {
        installation.HasAuthoritativeContentState = true;
        Replace(installation.ContentComponents, paths, restore?.InstallationComponents);
        Replace(product.ContentComponents, paths, restore?.ProductComponents);
    }

    private static void Replace(Dictionary<string, ContentComponentState> current, IReadOnlySet<string> paths,
        Dictionary<string, ContentComponentState>? restore)
    {
        foreach (var component in current.Values)
            component.Files.RemoveAll(f => paths.Contains(f.RelativePath));
        if (restore is null) return;
        foreach (var pair in restore)
        {
            var saved = Clone(pair.Value);
            saved.Files.RemoveAll(f => !paths.Contains(f.RelativePath));
            if (saved.Files.Count == 0) continue;
            if (current.TryGetValue(pair.Key, out var component)) component.Files.AddRange(saved.Files);
            else current[pair.Key] = saved;
        }
    }

    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
