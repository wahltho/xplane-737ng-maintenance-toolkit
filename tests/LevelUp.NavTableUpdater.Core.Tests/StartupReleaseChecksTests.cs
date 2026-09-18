using LevelUp.NavTableUpdater.Core.Upstream;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class StartupReleaseChecksTests
{
    [Fact]
    public async Task Enabled_ChecksAircraftThenPatches_WithoutOverlap()
    {
        var events = new List<string>();
        var completion = new TaskCompletionSource();
        var run = StartupReleaseChecks.RunAsync(() => true,
            async () => { events.Add("aircraft"); await completion.Task; events.Add("aircraft done"); },
            () => { events.Add("patches"); return Task.CompletedTask; });
        Assert.Equal(["aircraft"], events);
        completion.SetResult();
        await run;
        Assert.Equal(["aircraft", "aircraft done", "patches"], events);
    }

    [Fact]
    public async Task DisabledOrNoAircraft_DoesNotCallEitherSource()
    {
        await StartupReleaseChecks.RunAsync(() => false,
            () => throw new Exception("Unexpected aircraft request"), () => throw new Exception("Unexpected patch request"));
    }

    [Fact]
    public async Task TargetChangedOrDisabledDuringAircraftCheck_SkipsPatches()
    {
        var canCheck = true;
        await StartupReleaseChecks.RunAsync(() => canCheck,
            () => { canCheck = false; return Task.CompletedTask; }, () => throw new Exception("Unexpected patch request"));
    }
}
