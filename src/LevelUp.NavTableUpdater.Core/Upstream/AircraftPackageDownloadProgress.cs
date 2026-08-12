namespace LevelUp.NavTableUpdater.Core.Upstream;

public sealed record AircraftPackageDownloadProgress(
    string Transport,
    string Status,
    double Percentage,
    long DownloadRateBytesPerSecond = 0,
    int ConnectedPeers = 0);
