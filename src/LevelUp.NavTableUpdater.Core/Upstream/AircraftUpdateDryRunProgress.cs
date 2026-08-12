namespace LevelUp.NavTableUpdater.Core.Upstream;

public sealed record AircraftUpdateDryRunProgress(
    string PackageFileName,
    int PackageIndex,
    int PackageCount,
    int ProcessedFileCount,
    int TotalFileCount,
    string? CurrentPath);
