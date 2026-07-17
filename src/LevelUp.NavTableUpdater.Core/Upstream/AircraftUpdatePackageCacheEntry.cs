namespace LevelUp.NavTableUpdater.Core.Upstream;

public enum AircraftUpdatePackageCacheState
{
    Missing = 0,
    Cached,
    Imported
}

public sealed record AircraftUpdatePackageCacheEntry(
    AircraftUpdatePackage Package,
    string CachePath,
    AircraftUpdatePackageCacheState State,
    long? SizeBytes,
    string? Sha256)
{
    public bool IsCached => State is AircraftUpdatePackageCacheState.Cached or AircraftUpdatePackageCacheState.Imported;
}
