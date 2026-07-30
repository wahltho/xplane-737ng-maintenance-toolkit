namespace LevelUp.NavTableUpdater.Core.Aircraft;

public sealed record AircraftProductIdentity(
    string Family,
    string DisplayName,
    string BackupScopeId)
{
    public static AircraftProductIdentity FromVariant(AircraftVariantViewAnalysis variant)
    {
        ArgumentNullException.ThrowIfNull(variant);

        if (IsLevelUp(variant.Family) || IsLevelUp(variant.AircraftId))
        {
            return new AircraftProductIdentity(
                "levelup-737ng",
                "LevelUp 737NG Series",
                "levelup-737ng-series");
        }

        if (IsZibo(variant.Family) || IsZibo(variant.AircraftId))
        {
            return new AircraftProductIdentity(
                "zibo-737ng",
                "Zibo Boeing 737-800X",
                "zibo-737-800x");
        }

        throw new InvalidOperationException($"Unsupported aircraft product family: {variant.Family}");
    }

    public bool MatchesLegacyAircraftId(string aircraftId) =>
        string.Equals(Family, "levelup-737ng", StringComparison.OrdinalIgnoreCase)
            ? IsLevelUp(aircraftId)
            : IsZibo(aircraftId);

    private static bool IsLevelUp(string value) =>
        value.StartsWith("levelup", StringComparison.OrdinalIgnoreCase);

    private static bool IsZibo(string value) =>
        value.StartsWith("zibo", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Zibo", StringComparison.OrdinalIgnoreCase);
}
