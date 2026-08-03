using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Manifest;

namespace LevelUp.NavTableUpdater.Core.Content;

public static class DeclarativePatchProductCompatibility
{
    public static IReadOnlySet<string> ResolveSupportedProducts(DeclarativePatchManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.SupportedProducts.Count > 0)
        {
            return manifest.SupportedProducts.ToHashSet(StringComparer.Ordinal);
        }

        if (manifest.AircraftFamily.Contains("LevelUp", StringComparison.OrdinalIgnoreCase))
        {
            return new HashSet<string>(StringComparer.Ordinal) { AircraftProductIds.LevelUp737Ng };
        }

        if (manifest.AircraftFamily.Contains("Zibo", StringComparison.OrdinalIgnoreCase))
        {
            return new HashSet<string>(StringComparer.Ordinal) { AircraftProductIds.Zibo737Ng };
        }

        return new HashSet<string>(StringComparer.Ordinal);
    }

    public static bool SupportsProduct(DeclarativePatchManifest manifest, string productId) =>
        ResolveSupportedProducts(manifest).Contains(productId);
}
