using LevelUp.NavTableUpdater.Core.Diagnostics;

namespace LevelUp.NavTableUpdater.App.Services;

public sealed record InstallationFileCheckRow(string Path, string Owner, string Kind, string Status, string Detail);

public sealed record InstallationCheckSummary(
    string Status, string Summary, string AircraftFolder, string CheckedAt,
    IReadOnlyList<InstallationFileCheckRow> Files, IReadOnlyList<string> Notes)
{
    public static InstallationCheckSummary Create(DiagnosticReport report)
    {
        var verified = report.Files.Count(f => f.Status is "MatchesRecordedHash" or "AbsentAsRecorded");
        var review = report.Files.Count(f => f.Status is "DiffersFromRecordedHash" or "Missing" or "MissingDirectory"
            or "UnexpectedlyPresent" or "UnexpectedDirectory" or "UnsafePath" or "SymlinkNotRead");
        var incomplete = report.Files.Count(f => f.Status is not ("MatchesRecordedHash" or "AbsentAsRecorded"
            or "ObservedWithoutExpectedHash" or "DiffersFromRecordedHash" or "Missing" or "MissingDirectory"
            or "UnexpectedlyPresent" or "UnexpectedDirectory" or "UnsafePath" or "SymlinkNotRead"));
        var status = review > 0 ? "Needs review"
            : report.StateStatus != "Loaded" || verified == 0 || incomplete > 0 || report.Warnings.Count > 0
                ? "Check incomplete" : "Recorded file checks passed";
        var summary = $"{verified} recorded file check(s) passed; {review} need review; {incomplete} not fully checked. "
            + $"Toolkit state: {report.StateStatus}.";
        var notes = report.Warnings.Concat(new[]
        {
            "This checks Toolkit records, not every file in the aircraft. It does not prove that the aircraft works correctly in X-Plane.",
            "A hash difference does not identify who changed a file. Another patch may have changed an older snapshot of the same file.",
            "Directory backups are checked for presence only. A complete restore chain is not verified.",
            "No online release check, download, export or file change was made. Use Review before installing or repairing a package."
        }).ToArray();
        return new(status, summary, report.Context.AircraftFolder,
            report.ExportedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            report.Files.Select(f => new InstallationFileCheckRow(f.Path, f.Owner, f.Kind, Label(f.Status), f.Detail)).ToArray(), notes);
    }

    private static string Label(string status) => status switch
    {
        "MatchesRecordedHash" => "Matches recorded hash",
        "AbsentAsRecorded" => "Absent as recorded",
        "DiffersFromRecordedHash" => "Differs from recorded snapshot",
        "Missing" => "File missing",
        "MissingDirectory" => "Directory missing",
        "UnexpectedlyPresent" => "Expected absence; file present",
        "UnexpectedDirectory" => "Expected file; directory present",
        "UnsafePath" => "Unsafe path; not read",
        "SymlinkNotRead" => "Symlink; not read",
        "ObservedWithoutExpectedHash" => "Present; no reference hash",
        "DirectoryPresentNotHashVerified" => "Directory present; contents not verified",
        "HashBudgetExceeded" => "Not checked: size limit reached",
        "ChangedDuringRead" => "File changed during check",
        "CouldNotRead" => "Could not read",
        _ => $"Not checked: {status}"
    };
}
