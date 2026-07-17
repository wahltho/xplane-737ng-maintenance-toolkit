namespace LevelUp.NavTableUpdater.Core.Aircraft;

public sealed record AircraftCg(double YFeet, double ZFeet);

public sealed record DefaultView(double XFeet, double YFeet, double ZFeet, double PitchDegrees);

public sealed record QuickView0(double XMeters, double YMeters, double ZMeters, double PitchDegrees);

public sealed record AcfMetadata(
    string? Name,
    string? Description,
    string? Studio,
    string? AcfVersion,
    string? FileWriterVersion,
    AircraftCg? Cg,
    DefaultView? DefaultView);
