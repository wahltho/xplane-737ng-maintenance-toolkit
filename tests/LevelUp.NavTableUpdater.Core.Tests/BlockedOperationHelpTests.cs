using LevelUp.NavTableUpdater.App.Services;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class BlockedOperationHelpTests
{
    private const string Fms = "plugins/xlua/scripts/B738.a_fms/B738.a_fms.lua";

    [Theory]
    [InlineData("Applied")]
    [InlineData("Restored")]
    [InlineData("No change")]
    [InlineData("Complete")]
    [InlineData("Canceled")]
    [InlineData("Transaction in progress")]
    [InlineData("Package ready")]
    public void NonFailureStatuses_DoNotOfferFailureAdviceEvenWithOldErrorText(string status) =>
        Assert.Null(BlockedOperationHelp.FromResult(status, $"Original backup is missing for {Fms}."));

    [Theory]
    [InlineData("Blocked")]
    [InlineData("Review blocked")]
    [InlineData("Failed")]
    [InlineData("Installation failed")]
    [InlineData("Review required")]
    [InlineData("Update incomplete")]
    public void UnknownFailures_DoNotInventAnAffectedFileOrInstaller(string status)
    {
        var help = BlockedOperationHelp.FromResult(status, "Unexpected test failure.")!;
        Assert.False(help.HasAffectedPath);
        Assert.Contains("operation log", help.Reason);
        Assert.Contains("diagnostic package", help.NextStep);
        Assert.DoesNotContain("standalone", help.Reason);
        Assert.DoesNotContain("reinstall", help.NextStep);
    }

    [Theory]
    [InlineData("Original backup is missing for ")]
    [InlineData("Original compatibility backup is missing for ")]
    [InlineData("Original compatibility-package backup is missing for ")]
    [InlineData("Original backup failed validation for ")]
    [InlineData("Original compatibility backup failed validation for ")]
    [InlineData("Original compatibility-package backup failed validation for ")]
    [InlineData("Original backup failed integrity validation for ")]
    [InlineData("Original backup failed size/SHA-256 validation for ")]
    [InlineData("Original backup failed verification for ")]
    [InlineData("Original backup failed verification for retired file ")]
    public void MissingOrInvalidBackups_NameTheRequiredFileAndPreserveExistingFiles(string prefix)
    {
        var help = BlockedOperationHelp.FromResult("Blocked", prefix + Fms + ".")!;
        Assert.Equal(Fms, help.AffectedPath);
        Assert.Contains("original backup", help.Reason);
        Assert.Contains("accessible", help.NextStep);
        Assert.Contains("remaining backups intact", help.NextStep);
    }

    [Fact]
    public void DisconnectedHistory_PreservesPathsAndDoesNotRecommendDeletingBackups()
    {
        const string path = "objects/737_cockpit_ovhd2.obj";
        var help = BlockedOperationHelp.FromResult("Blocked",
            $"Cannot verify the complete backup chain for {path}; existing installation retained. Current SHA-256={new string('a', 64)}; history is disconnected or ambiguous.")!;
        Assert.Equal(path, help.AffectedPath);
        Assert.Contains("cannot verify", help.Reason);
        Assert.Contains("history before", help.NextStep);
        Assert.DoesNotContain("standalone", help.Reason);
    }

    [Fact]
    public void UnownedMarker_DoesNotClaimTheUserRanAStandaloneInstaller()
    {
        var message = $"LevelUp VNAV descent tables has standalone or unowned patch evidence at {Fms}. "
            + "The Toolkit cannot verify one complete backup chain and will not adopt the patched file as an original. Follow the standalone README.";
        var help = BlockedOperationHelp.FromResult("Required patches pending", "Required patches are still pending. " + message)!;
        Assert.Equal(Fms, help.AffectedPath);
        Assert.Contains("does not establish how", help.Reason);
        Assert.Contains("If you installed", help.NextStep);
        Assert.Contains("moved", help.NextStep);
    }

    [Fact]
    public void ChangedManagedFile_DoesNotAttributeTheChangeOrOfferBlindRepair()
    {
        var help = BlockedOperationHelp.FromResult("Blocked", $"Managed target changed after installation: {Fms}.")!;
        Assert.Equal(Fms, help.AffectedPath);
        Assert.Contains("does not identify", help.Reason);
        Assert.Contains("before repairing or restoring", help.NextStep);
    }

    [Theory]
    [InlineData("Managed-scope copy target has an unknown source hash: objects/GSE/737misc2.dds.", "objects/GSE/737misc2.dds")]
    [InlineData("Unrecognized original: objects/GSE/737misc2.dds", "objects/GSE/737misc2.dds")]
    [InlineData("Retired file has an unknown or locally modified SHA-256: win_x64/xlua.xpl (aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa)", "win_x64/xlua.xpl")]
    public void UnknownSources_AreNeverDescribedAsSafeToOverwrite(string message, string path)
    {
        var help = BlockedOperationHelp.FromResult("Blocked", message)!;
        Assert.Equal(path, help.AffectedPath);
        Assert.Contains("not a recognized source", help.Reason);
        Assert.Contains("keep this file unchanged", help.NextStep);
    }

    [Fact]
    public void XPlaneRunning_HasOnePracticalNextStep()
    {
        var help = BlockedOperationHelp.FromResult("Blocked", "X-Plane is running. Close X-Plane before changing aircraft files.")!;
        Assert.Contains("Close X-Plane completely", help.NextStep);
        Assert.False(help.HasAffectedPath);
    }

    [Theory]
    [InlineData("Could not check whether X-Plane is running.")]
    [InlineData("Could not read the API rate limit headers.")]
    [InlineData("Could not determine whether the target is a symbolic link.")]
    [InlineData("Download canceled by the user.")]
    public void InconclusiveChecks_DoNotBecomeConfirmedCauses(string message)
    {
        var help = BlockedOperationHelp.FromResult("Failed", message)!;
        Assert.Contains("operation log", help.Reason);
        Assert.False(help.HasAffectedPath);
    }

    [Fact]
    public void ConfirmedSymbolicLinkRejection_DoesNotRecommendBypassingTheCheck()
    {
        var help = BlockedOperationHelp.FromResult("Failed", "Content patch archive contains a symbolic link: objects/GSE/example.obj.")!;
        Assert.Contains("symbolic link was rejected", help.Reason);
        Assert.Contains("Do not bypass", help.NextStep);
    }

    [Fact]
    public void Generic403_IsNotAutomaticallyAttributedToRateLimiting()
    {
        var help = BlockedOperationHelp.FromResult("Failed", "Response status code does not indicate success: 403 (Forbidden).")!;
        Assert.DoesNotContain("request limit", help.Reason);
        var limited = BlockedOperationHelp.FromResult("Failed", "Response status code does not indicate success: 403 (rate limit exceeded).")!;
        Assert.Contains("request limit", limited.Reason);
        Assert.Contains("reset", limited.NextStep);
    }

    [Fact]
    public void CanceledRequiredPatches_AreNotReportedAsDamagedFiles()
    {
        var help = BlockedOperationHelp.FromResult("Required patches pending",
            "Catalog group action canceled; required patches are still pending.")!;
        Assert.Contains("canceled", help.Reason);
        Assert.Contains("Review the required", help.NextStep);
        Assert.False(help.HasAffectedPath);
    }
}
