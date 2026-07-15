namespace LevelUp.NavTableUpdater.Core.Analysis;

public enum InstallState
{
    NoTargetSelected,
    UnsupportedTarget,
    PortNoLuaInstallation,
    NotInstalled,
    CorrectlyInstalled,
    OutdatedMarkedInstallation,
    KnownLegacyInstallation,
    PartiallyInstalled,
    AircraftUpdateOverwroteInstallation,
    UnknownThirdPartyModification
}
