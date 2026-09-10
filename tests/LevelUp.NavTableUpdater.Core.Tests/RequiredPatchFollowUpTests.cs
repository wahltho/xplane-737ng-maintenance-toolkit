using LevelUp.NavTableUpdater.App.Services;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Content;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class RequiredPatchFollowUpTests
{
    private static ContentPackageCatalog Catalog(bool includeGroup = true)
    {
        var document = System.Text.Json.Nodes.JsonNode.Parse("""
        {"schemaVersion":1,"catalogVersion":"1.0.0","packages":[
        {"packageId":"vnav","displayName":"VNAV","description":"Required maintenance", "activation":"managed", "category":"compatibilityPackage","supportedProducts":["levelup-737ng"],"repositoryUrl":"https://github.com/example/vnav","distribution":{"kind":"gitHubReleaseArchive","assetNamePattern":"vnav-*.zip","manifestSchemaVersion":3}},
        {"packageId":"group","displayName":"LevelUp maintenance patches","description":"Required maintenance", "activation":"managed", "category":"compatibilityPackage","supportedProducts":["levelup-737ng"],"repositoryUrl":"https://github.com/example/toolkit","distribution":{"kind":"catalogGroup"},"members":[{"packageId":"vnav","moduleId":"vnav","policy":"required","installationOrder":10,"sourceFormat":"compatibility","manifestPath":"package-manifest.json","assetNamePattern":"vnav-*.zip"}]}]}
        """)!;
        if (!includeGroup) document["packages"]!.AsArray().RemoveAt(1);
        return ContentPackageCatalog.Parse(document.ToJsonString());
    }

    [Fact]
    public async Task EveryUpdateChecksGroupAgainEvenAfterPreviousNoChange()
    {
        var ui = new Interaction(true);
        var runs = 0;
        Task<MaintenanceOperationResult> Apply() { runs++; return Task.FromResult(MaintenanceOperationResult.NoChange("Current", [])); }
        var first = await RequiredPatchFollowUp.RunAsync(Catalog(), "LevelUp", ui, Apply);
        var second = await RequiredPatchFollowUp.RunAsync(Catalog(), "LevelUp", ui, Apply);
        Assert.True(first!.Succeeded);
        Assert.True(second!.Succeeded);
        Assert.Equal(2, runs);
        Assert.All(ui.Requests, r => Assert.Equal("Update required LevelUp patches?", r.Title));
    }

    [Fact]
    public async Task DeferredGroupIsUnsuccessfulAndDoesNotRunWrites()
    {
        var result = await RequiredPatchFollowUp.RunAsync(Catalog(), "LevelUp", new Interaction(false),
            () => throw new Exception("Must not execute"));
        Assert.False(result!.Succeeded);
        Assert.Equal("Required patches pending", result.Status);
    }

    [Fact]
    public async Task FailedGroupRetainsFailureAndBackupDetails()
    {
        var failed = new MaintenanceOperationResult(false, true, "Failed", "A required module failed", ["backup"], ["details"]);
        var result = await RequiredPatchFollowUp.RunAsync(Catalog(), "LevelUp", new Interaction(true), () => Task.FromResult(failed));
        Assert.False(result!.Succeeded);
        Assert.True(result.Changed);
        Assert.Equal(failed.BackupPaths, result.BackupPaths);
        Assert.Equal("Required patches pending", result.Status);
        Assert.Contains("A required module failed", result.Message);
    }

    [Fact]
    public async Task DownloadCancellationIsPendingNotSuccessful()
    {
        var result = await RequiredPatchFollowUp.RunAsync(Catalog(), "LevelUp", new Interaction(true),
            () => throw new OperationCanceledException("Canceled download"));
        Assert.False(result!.Succeeded);
        Assert.Equal("Required patches pending", result.Status);
    }

    [Fact]
    public async Task MissingLevelUpGroupCannotBeReportedAsUpToDate()
    {
        var ui = new Interaction(true);
        var result = await RequiredPatchFollowUp.RunAsync(Catalog(false), "LevelUp", ui,
            () => throw new Exception("Must not execute"));
        Assert.False(result!.Succeeded);
        Assert.Equal("Required patches pending", result.Status);
        Assert.Empty(ui.Requests);
    }

    [Fact]
    public async Task ZiboFallsBackToExistingVnavWorkflow()
    {
        var ui = new Interaction(true);
        Assert.Null(await RequiredPatchFollowUp.RunAsync(Catalog(), "Zibo", ui,
            () => throw new Exception("Must not execute")));
        Assert.Empty(ui.Requests);
    }

    private sealed class Interaction(bool accept) : IUserInteractionService
    {
        public List<ConfirmationRequest> Requests { get; } = [];
        public Task<bool> ConfirmAsync(ConfirmationRequest request) { Requests.Add(request); return Task.FromResult(accept); }
        public Task ShowMessageAsync(MessageRequest request) => Task.CompletedTask;
    }
}
