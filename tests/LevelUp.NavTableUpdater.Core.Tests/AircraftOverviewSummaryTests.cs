using LevelUp.NavTableUpdater.App.Services;
using LevelUp.NavTableUpdater.Core.Upstream;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class AircraftOverviewSummaryTests
{
    [Fact]
    public void Zibo_HasNoMandatoryPatchesAndDoesNotRequireUninstalledOptions()
    {
        var summary = Summary(false, [new("VNAV", false, null, "v0.2.0")]);
        Assert.Equal("No updates found", summary.Status);
        Assert.Contains("All patches are optional", summary.RequiredPatches);
        Assert.Equal("None recorded by the Toolkit", summary.OptionalPatches);
    }

    [Fact]
    public void MissingLevelUpPatches_RequireActionEvenWhenAircraftIsCurrent()
    {
        var summary = Summary(true, [new("VNAV", true, "0.2.0", "v0.2.0"), new("FANS CDU", true, null, "0.1.6")]);
        Assert.Equal("Required patches missing", summary.Status);
        Assert.StartsWith("1 of 2 installed", summary.RequiredPatches);
        Assert.Contains("Use Update", summary.NextStep);
    }

    [Fact]
    public void InstalledPatchesWithoutReleaseCheck_AreNotCalledCurrent()
    {
        var summary = Summary(true, [new("VNAV", true, "0.2.0", null)]);
        Assert.Equal("Not fully checked", summary.Status);
        Assert.Contains("latest releases not checked", summary.RequiredPatches);
        Assert.DoesNotContain("current", summary.RequiredPatches);
    }

    [Fact]
    public void OnlyInstalledOptionalPatchesAreListedAndCompared()
    {
        var summary = Summary(false, [new("VNAV", false, "v0.2.0", "0.2.0"),
            new("CPDLC", false, "1.1.0", "1.2.0"), new("AUTO JETWAY", false, null, "0.2.3")]);
        Assert.Equal("Optional patch versions differ", summary.Status);
        Assert.Contains("VNAV v0.2.0", summary.OptionalPatches);
        Assert.Contains("CPDLC 1.1.0", summary.OptionalPatches);
        Assert.DoesNotContain("AUTO JETWAY", summary.OptionalPatches);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void FailedChecks_CannotReuseOldVersionsToClaimCurrent(bool aircraftFailed, bool patchesFailed)
    {
        var summary = AircraftOverviewSummary.Create(true, true, "v2.S1.51C", Check(),
            [new("VNAV", true, "0.2.0", "0.2.0")], false, aircraftFailed, patchesFailed, "10:00", "10:01");
        Assert.Equal("Release check failed", summary.Status);
        Assert.Contains("Retry", summary.NextStep);
        if (patchesFailed) Assert.DoesNotContain("current", summary.RequiredPatches);
        if (aircraftFailed) Assert.DoesNotContain("current", summary.Aircraft);
    }

    [Theory]
    [InlineData(AircraftUpdatePlanAction.Unknown)]
    [InlineData(AircraftUpdatePlanAction.LocalNewerThanIndex)]
    [InlineData(AircraftUpdatePlanAction.MissingRequiredPackage)]
    [InlineData(AircraftUpdatePlanAction.BaselineMismatch)]
    public void UncomparableOrUnsupportedAircraft_NeverShowNoUpdates(AircraftUpdatePlanAction action)
    {
        var summary = Summary(false, [], Check(action));
        Assert.Equal("Review aircraft version", summary.Status);
        Assert.DoesNotContain("; current", summary.Aircraft);
    }

    [Fact]
    public void CustomAircraft_DoNotOfferAnOfficialUpdate()
    {
        var summary = Summary(false, [], Check() with { IsCustomDistribution = true });
        Assert.Equal("Review aircraft version", summary.Status);
        Assert.Contains("custom aircraft", summary.Aircraft);
    }

    [Theory]
    [InlineData("unknown", "v2.S1.51C")]
    [InlineData("v2.S1.51C", "-")]
    [InlineData("v2.S1.50", "v2.S1.51C")]
    public void IncompleteOrInconsistentCurrentResult_DoesNotClaimCurrent(string local, string available)
    {
        var summary = Summary(false, [], Check() with { LocalVersionDisplay = local, AvailableVersionDisplay = available });
        Assert.Equal("Review aircraft version", summary.Status);
        Assert.DoesNotContain("; current", summary.Aircraft);
        Assert.StartsWith($"Installed: {local}", summary.Aircraft);
    }

    [Theory]
    [InlineData(AircraftUpdatePlanAction.ApplyCumulativePatch)]
    [InlineData(AircraftUpdatePlanAction.InstallBaselineAndCumulativePatch)]
    public void AircraftUpdate_TakesPriorityOverOptionalPatchVersions(AircraftUpdatePlanAction action)
    {
        var summary = Summary(false, [new("VNAV", false, "0.1.0", "0.2.0")], Check(action));
        Assert.Equal("Aircraft update available", summary.Status);
    }

    [Fact]
    public void UnknownInstalledPatchVersion_IsRecordedButNotCalledCurrent()
    {
        var summary = Summary(true, [new("VNAV", true, "version unknown", "0.2.0")]);
        Assert.StartsWith("1 of 1 installed", summary.RequiredPatches);
        Assert.Contains("installed version unknown", summary.RequiredPatches);
        Assert.Equal("Not fully checked", summary.Status);
    }

    [Fact]
    public void MissingRequiredCatalog_CannotReportZeroOfZeroAsComplete()
    {
        var summary = Summary(true, []);
        Assert.Equal("Patch list unavailable", summary.Status);
        Assert.Contains("unavailable", summary.RequiredPatches);
    }

    [Fact]
    public void NoAircraftCheck_AndRunningCheck_DoNotReportCurrent()
    {
        var uncheckedSummary = AircraftOverviewSummary.Create(false, true, "4.05.35", null, [], false, false, false, "Not checked", "Not checked");
        Assert.Equal("Not fully checked", uncheckedSummary.Status);
        Assert.Contains("latest release not checked", uncheckedSummary.Aircraft);
        var checking = AircraftOverviewSummary.Create(false, true, "4.05.35", Check(), [], true, false, false, "10:00", "10:01");
        Assert.Equal("Checking releases…", checking.Status);
    }

    [Fact]
    public void LastCheckTimes_AreShownSeparately()
    {
        var summary = Summary(true, [new("VNAV", true, "v0.2.0", "0.2.0")]);
        Assert.Equal("No updates found", summary.Status);
        Assert.Equal("Aircraft releases: 2026-10-02 10:00:00. Patch releases: 2026-10-02 10:01:00.", summary.LastChecked);
    }

    private static AircraftOverviewSummary Summary(bool levelUp, IReadOnlyList<AircraftPatchVersion> patches,
        AircraftUpstreamUpdateCheckResult? check = null) => AircraftOverviewSummary.Create(levelUp, true, "v2.S1.51C",
            check ?? Check(), patches, false, false, false, "2026-10-02 10:00:00", "2026-10-02 10:01:00");

    private static AircraftUpstreamUpdateCheckResult Check(AircraftUpdatePlanAction action = AircraftUpdatePlanAction.UpToDate) =>
        new("", "", "levelup-737ng", "", "v2.S1.51C", "v2.S1.51C", action, "", false, [], []);
}
