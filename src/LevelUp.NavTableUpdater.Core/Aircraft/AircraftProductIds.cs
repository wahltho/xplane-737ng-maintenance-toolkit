namespace LevelUp.NavTableUpdater.Core.Aircraft;

public static class AircraftProductIds
{
    public const string Zibo737Ng = "zibo-737ng";

    public const string LevelUp737Ng = "levelup-737ng";

    public static IReadOnlySet<string> Supported { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Zibo737Ng,
        LevelUp737Ng
    };

    public static bool IsSupported(string productId) => Supported.Contains(productId);

    public static string? Normalize(string productIdOrFamily)
    {
        if (IsSupported(productIdOrFamily))
        {
            return productIdOrFamily;
        }

        if (productIdOrFamily.Contains("LevelUp", StringComparison.OrdinalIgnoreCase))
        {
            return LevelUp737Ng;
        }

        return productIdOrFamily.Contains("Zibo", StringComparison.OrdinalIgnoreCase)
            ? Zibo737Ng
            : null;
    }
}
