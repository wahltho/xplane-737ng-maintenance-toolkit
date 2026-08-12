using LevelUp.NavTableUpdater.Core.Aircraft;

namespace LevelUp.NavTableUpdater.Core.Upstream;

public sealed record AircraftFreshInstallProduct(
    string ProductId,
    string DisplayName,
    string DefaultFolderName)
{
    public static IReadOnlyList<AircraftFreshInstallProduct> All { get; } =
    [
        new(AircraftProductIds.Zibo737Ng, "Zibo Boeing 737-800X", "B737-800X"),
        new(AircraftProductIds.LevelUp737Ng, "LevelUp 737NG Series", "737NG Series")
    ];
}
