namespace LevelUp.NavTableUpdater.Core.Upstream;

public sealed record AircraftUpdateIndex(
    string Family,
    string SourceUrl,
    IReadOnlyList<AircraftUpdatePackage> Packages)
{
    public static AircraftUpdateIndex Empty(string family, string sourceUrl) =>
        new(family, sourceUrl, []);
}
