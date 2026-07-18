using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Tests;

internal static class TestToolStateStore
{
    public static ToolStateStore Create(string fixturePath) =>
        new(
            Path.Combine(fixturePath, ".tool-state"),
            Path.Combine(fixturePath, ".tool-backups"));
}
