namespace LevelUp.NavTableUpdater.Core.Upstream;

/// <summary>Read-only checks run sequentially, only while the original startup target remains selected.</summary>
public static class StartupReleaseChecks
{
    public static async Task RunAsync(Func<bool> canCheck, Func<Task> checkAircraft, Func<Task> checkPatches)
    {
        if (!canCheck()) return;
        await checkAircraft();
        if (!canCheck()) return;
        await checkPatches();
    }
}
