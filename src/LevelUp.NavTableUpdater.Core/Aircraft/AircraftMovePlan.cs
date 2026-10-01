namespace LevelUp.NavTableUpdater.Core.Aircraft;

public sealed record AircraftMoveEntry(string RelativePath, bool Directory, long Size, string Sha256,
    FileAttributes Attributes, DateTime LastWriteUtc, int? UnixMode);

public sealed record AircraftMovePlan(string Source, string Destination,
    IReadOnlyList<AircraftMoveEntry> Entries, string StateFingerprint, string SettingsFingerprint)
{
    public long TotalBytes => Entries.Sum(e => e.Size);
    public int FileCount => Entries.Count(e => !e.Directory);
}

public sealed record AircraftMoveProgress(string Message, int Percent);
public sealed record AircraftMoveResult(string Destination, bool CleanupPending);
