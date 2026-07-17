namespace LevelUp.NavTableUpdater.Core.Upstream;

public enum AircraftUpdateDryRunEntryAction
{
    Add = 0,
    Replace,
    PreserveProtectedLocalFile,
    PreserveToolkitMetadata,
    BlockedUnsafePath
}

public sealed record AircraftUpdateDryRunEntry(
    string PackageFileName,
    string RelativePath,
    AircraftUpdateDryRunEntryAction Action,
    long SizeBytes,
    string Detail);

public sealed record AircraftUpdateDryRunResult(
    bool Succeeded,
    string Summary,
    IReadOnlyList<AircraftUpdateDryRunEntry> Entries,
    IReadOnlyList<string> Findings)
{
    public int AddCount => Entries.Count(entry => entry.Action == AircraftUpdateDryRunEntryAction.Add);

    public int ReplaceCount => Entries.Count(entry => entry.Action == AircraftUpdateDryRunEntryAction.Replace);

    public int ProtectedCount => Entries.Count(entry => entry.Action is AircraftUpdateDryRunEntryAction.PreserveProtectedLocalFile
        or AircraftUpdateDryRunEntryAction.PreserveToolkitMetadata);

    public int BlockedCount => Entries.Count(entry => entry.Action == AircraftUpdateDryRunEntryAction.BlockedUnsafePath);
}
