using LevelUp.NavTableUpdater.Core.Upstream;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class AircraftUpdatePlanReusePolicyTests
{
    [Fact]
    public void CanReuseValidatedLocalPlan_WithMatchingCachedPackage_ReturnsTrue()
    {
        var package = BuildPackage();
        var updateCheck = BuildUpdateCheck(Path.Combine(Path.GetTempPath(), "levelup-update.manifest.json"), package);
        var cacheEntry = new AircraftUpdatePackageCacheEntry(
            package,
            Path.Combine(Path.GetTempPath(), package.FileName),
            AircraftUpdatePackageCacheState.Imported,
            package.ExpectedSizeBytes,
            package.ExpectedSha256);

        var result = AircraftUpdatePlanReusePolicy.CanReuseValidatedLocalPlan(updateCheck, [cacheEntry]);

        Assert.True(result);
    }

    [Theory]
    [InlineData(AircraftUpdatePackageCacheState.Missing)]
    [InlineData(AircraftUpdatePackageCacheState.Invalid)]
    public void CanReuseValidatedLocalPlan_WithoutValidCache_ReturnsFalse(
        AircraftUpdatePackageCacheState cacheState)
    {
        var package = BuildPackage();
        var updateCheck = BuildUpdateCheck(Path.Combine(Path.GetTempPath(), "levelup-update.manifest.json"), package);
        var cacheEntry = new AircraftUpdatePackageCacheEntry(
            package,
            Path.Combine(Path.GetTempPath(), package.FileName),
            cacheState,
            package.ExpectedSizeBytes,
            package.ExpectedSha256);

        var result = AircraftUpdatePlanReusePolicy.CanReuseValidatedLocalPlan(updateCheck, [cacheEntry]);

        Assert.False(result);
    }

    [Fact]
    public void CanReuseValidatedLocalPlan_WithOnlinePlan_ReturnsFalse()
    {
        var package = BuildPackage();
        var updateCheck = BuildUpdateCheck(
            "https://github.com/petrolpram/737NG-Updates/releases/latest/download/release-index.json",
            package);
        var cacheEntry = new AircraftUpdatePackageCacheEntry(
            package,
            Path.Combine(Path.GetTempPath(), package.FileName),
            AircraftUpdatePackageCacheState.Cached,
            package.ExpectedSizeBytes,
            package.ExpectedSha256);

        var result = AircraftUpdatePlanReusePolicy.CanReuseValidatedLocalPlan(updateCheck, [cacheEntry]);

        Assert.False(result);
    }

    [Fact]
    public void CanReuseValidatedLocalPlan_WithoutRequiredPackages_ReturnsFalse()
    {
        var updateCheck = BuildUpdateCheck(
            Path.Combine(Path.GetTempPath(), "levelup-update.manifest.json"));

        var result = AircraftUpdatePlanReusePolicy.CanReuseValidatedLocalPlan(updateCheck, []);

        Assert.False(result);
    }

    [Theory]
    [InlineData(AircraftUpdatePlanAction.ApplyCumulativePatch, true)]
    [InlineData(AircraftUpdatePlanAction.InstallBaselineAndCumulativePatch, true)]
    [InlineData(AircraftUpdatePlanAction.UpToDate, false)]
    [InlineData(AircraftUpdatePlanAction.LocalNewerThanIndex, false)]
    public void UpdateCheck_HasUpdate_OnlyForActionablePlans(
        AircraftUpdatePlanAction action,
        bool expected)
    {
        var package = BuildPackage();
        var updateCheck = BuildUpdateCheck(
            Path.Combine(Path.GetTempPath(), "levelup-update.manifest.json"),
            action,
            package);

        Assert.Equal(expected, updateCheck.HasUpdate);
    }

    private static AircraftUpdatePackage BuildPackage() =>
        new(
            LevelUpAircraftUpdatePackageLoader.Family,
            AircraftUpdatePackageKind.CumulativePatch,
            new AircraftUpstreamVersion(0, 0, 1),
            "levelup-update.7z",
            SourceUrl: "",
            ReleaseVersion: "v2.S1.50C",
            BaselineVersion: "V2.S1",
            ExpectedSizeBytes: 123,
            ExpectedSha256: new string('a', 64));

    private static AircraftUpstreamUpdateCheckResult BuildUpdateCheck(
        string sourceUrl,
        params AircraftUpdatePackage[] packages) =>
        BuildUpdateCheck(sourceUrl, AircraftUpdatePlanAction.ApplyCumulativePatch, packages);

    private static AircraftUpstreamUpdateCheckResult BuildUpdateCheck(
        string sourceUrl,
        AircraftUpdatePlanAction action,
        params AircraftUpdatePackage[] packages) =>
        new(
            "Incremental update package loaded",
            "Apply local LevelUp package.",
            LevelUpAircraftUpdatePackageLoader.Family,
            sourceUrl,
            "2.S1.0",
            "v2.S1.50C",
            action,
            "Apply cumulative patch",
            IsCustomDistribution: false,
            packages,
            Findings: []);
}
