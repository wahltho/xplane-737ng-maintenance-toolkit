using LevelUp.NavTableUpdater.App.Services;
using LevelUp.NavTableUpdater.Core.Diagnostics;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class InstallationOverviewTests
{
    [Fact]
    public void ModuleVersions_DistinguishRecordedInstalledAndAvailableVersions()
    {
        var optional = PatchModuleOverview.Create("cpdlc", new("CPDLC", false, null, "v1.2.0"), false, false);
        Assert.Equal("—", optional.Installed);
        Assert.Equal("Optional", optional.Policy);
        Assert.Equal("No Toolkit install record", optional.Status);
        var required = PatchModuleOverview.Create("vnav", new("VNAV", true, "0.2.0", "v0.2.0"), false, false);
        Assert.Equal("Required", required.Policy);
        Assert.Equal("Versions match; files not checked", required.Status);
        Assert.Equal("Different version available", PatchModuleOverview.Create("vnav",
            new("VNAV", false, "0.1.0", "v0.2.0"), false, false).Status);
    }

    [Fact]
    public void FailedOrMissingReleaseCheck_CannotClaimVersionMatch()
    {
        var patch = new AircraftPatchVersion("VNAV", true, "0.2.0", "0.2.0");
        Assert.Equal("Release check failed", PatchModuleOverview.Create("vnav", patch, true, false).Status);
        Assert.Equal("Latest release not checked", PatchModuleOverview.Create("vnav", patch with { Available = null }, false, false).Status);
        Assert.Equal("Installed version unknown", PatchModuleOverview.Create("vnav", patch with { Installed = "version unknown" }, false, false).Status);
        Assert.Equal("Installed version unknown", PatchModuleOverview.Create("vnav", patch with { Installed = "unknown" }, false, false).Status);
    }

    [Theory]
    [InlineData("VREF tables (Beta)", "v0.1.0", true)]
    [InlineData("VREF tables", "v0.1.0-beta.1", true)]
    [InlineData("GSE", "v0.2.0-preview.3", true)]
    [InlineData("Patch", "v4.8b2", true)]
    [InlineData("CPDLC", "v1.2.0", false)]
    public void BetaLabel_DoesNotDependOnGithubPrereleaseFlag(string name, string version, bool expected)
    {
        Assert.Equal(expected, PatchModuleOverview.Create("module", new(name, false, null, version), false, false).IsBeta);
    }

    [Fact]
    public void Summary_LimitsPassedResultToRecordedChecks()
    {
        var report = Report("MatchesRecordedHash", "AbsentAsRecorded", "ObservedWithoutExpectedHash");
        var summary = InstallationCheckSummary.Create(report);
        Assert.Equal("Recorded file checks passed", summary.Status);
        Assert.StartsWith("2 recorded file check(s) passed", summary.Summary);
        Assert.Contains(summary.Notes, n => n.Contains("not every file"));
        Assert.Contains(summary.Notes, n => n.Contains("restore chain is not verified"));
    }

    [Theory]
    [InlineData("DiffersFromRecordedHash")]
    [InlineData("Missing")]
    [InlineData("MissingDirectory")]
    [InlineData("UnexpectedlyPresent")]
    [InlineData("UnexpectedDirectory")]
    [InlineData("UnsafePath")]
    [InlineData("SymlinkNotRead")]
    public void UnsafeOrDifferentFiles_NeedReviewWithoutGuessingCause(string status)
    {
        var summary = InstallationCheckSummary.Create(Report("MatchesRecordedHash", status));
        Assert.Equal("Needs review", summary.Status);
        Assert.Contains("1 need review", summary.Summary);
        Assert.Contains(summary.Notes, n => n.Contains("does not identify who"));
    }

    [Theory]
    [InlineData("DirectoryPresentNotHashVerified")]
    [InlineData("HashBudgetExceeded")]
    [InlineData("ChangedDuringRead")]
    [InlineData("CouldNotRead")]
    [InlineData("FutureUnknownStatus")]
    public void PartialChecks_NeverProducePassedResult(string status)
    {
        Assert.Equal("Check incomplete", InstallationCheckSummary.Create(Report("MatchesRecordedHash", status)).Status);
    }

    [Theory]
    [InlineData("Not present")]
    [InlineData("Could not read")]
    public void MissingOrUnreadableState_NeverProducesPassedResult(string stateStatus)
    {
        var report = Report("MatchesRecordedHash");
        report.StateStatus = stateStatus;
        Assert.Equal("Check incomplete", InstallationCheckSummary.Create(report).Status);
    }

    [Fact]
    public void NoRecordedChecksOrCollectorWarning_NeverProducePassedResult()
    {
        Assert.Equal("Check incomplete", InstallationCheckSummary.Create(Report("ObservedWithoutExpectedHash")).Status);
        var report = Report("MatchesRecordedHash");
        report.Warnings.Add("Further checks omitted: check limit reached.");
        var summary = InstallationCheckSummary.Create(report);
        Assert.Equal("Check incomplete", summary.Status);
        Assert.Contains(report.Warnings[0], summary.Notes);
    }

    private static DiagnosticReport Report(params string[] statuses)
    {
        var report = new DiagnosticReport { StateStatus = "Loaded", Context = new() { AircraftFolder = "/aircraft" } };
        foreach (var status in statuses)
            report.Files.Add(new("Managed file snapshot", "patch", "/aircraft/script.lua", 3, "hash", 3, "hash", status));
        return report;
    }
}
