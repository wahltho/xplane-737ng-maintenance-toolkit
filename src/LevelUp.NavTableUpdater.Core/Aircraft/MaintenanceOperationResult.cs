namespace LevelUp.NavTableUpdater.Core.Aircraft;

public sealed record MaintenanceOperationResult(
    bool Succeeded,
    bool Changed,
    string Status,
    string Message,
    IReadOnlyList<string> BackupPaths,
    IReadOnlyList<string> Log)
{
    public static MaintenanceOperationResult Applied(string message, IReadOnlyList<string> backupPaths, IReadOnlyList<string> log) =>
        new(Succeeded: true, Changed: true, Status: "Applied", message, backupPaths, log);

    public static MaintenanceOperationResult NoChange(string message, IReadOnlyList<string> log) =>
        new(Succeeded: true, Changed: false, Status: "No change", message, BackupPaths: [], log);

    public static MaintenanceOperationResult Restored(string message, IReadOnlyList<string> backupPaths, IReadOnlyList<string> log) =>
        new(Succeeded: true, Changed: true, Status: "Restored", message, backupPaths, log);

    public static MaintenanceOperationResult Blocked(string message, IReadOnlyList<string> log) =>
        new(Succeeded: false, Changed: false, Status: "Blocked", message, BackupPaths: [], log);
}
