namespace LevelUp.NavTableUpdater.Core.Analysis;

public enum InstallState
{
    NoTargetSelected,
    UnsupportedTarget,
    PortNoLuaInstallation,
    NotInstalled,
    CorrectlyInstalled,
    RepairRequired,
    OutdatedMarkedInstallation,
    KnownLegacyInstallation,
    PartiallyInstalled,
    AircraftUpdateOverwroteInstallation,
    UnknownThirdPartyModification
}
