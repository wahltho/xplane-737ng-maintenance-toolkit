namespace LevelUp.NavTableUpdater.Core.Aircraft;

public sealed record AircraftMaintenanceMetadata(
    int SchemaVersion,
    string? AircraftFamily,
    string? Variant,
    string? Distribution,
    string? DistributionVersion,
    string? UpstreamFamily,
    string? UpstreamBaseVersion,
    string? Runtime)
{
    public const string FileName = "xplane-737ng-maintenance.json";
}
