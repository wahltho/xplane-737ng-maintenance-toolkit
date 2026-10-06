using LevelUp.NavTableUpdater.App.Services;
using LevelUp.NavTableUpdater.Core.Aircraft;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class QuickStartGuideTests
{
    [Fact]
    public void Guide_DistinguishesRequiredLevelUpAndOptionalZiboPatches()
    {
        var lu = string.Join("\n", QuickStartGuide.ForProduct(AircraftProductIds.LevelUp737Ng).Select(t => t.Text));
        var zibo = string.Join("\n", QuickStartGuide.ForProduct(AircraftProductIds.Zibo737Ng).Select(t => t.Text));
        Assert.Contains("requires VNAV, FANS CDU and Weight & Balance", lu);
        Assert.Contains("All Zibo patches are optional", zibo);
        Assert.Contains("None is selected automatically", zibo);
        Assert.DoesNotContain("requires VNAV", zibo);
        Assert.Contains("three required patches", string.Join(" ", QuickStartGuide.ForProduct(null).Select(t => t.Text)));
    }

    [Fact]
    public void Guide_ExplainsSelectionChecksAndRestoreWithoutPromisingSafety()
    {
        var text = string.Join("\n", QuickStartGuide.ForProduct(null).Select(t => t.Text));
        Assert.Contains("Ticking a box only changes your selection", text);
        Assert.Contains("without changing them", text);
        Assert.Contains("Repair does change files", text);
        Assert.Contains("Keep the aircraft, MTK state and backups unchanged", text);
        Assert.Contains("does not mean it can be restored", text);
        Assert.Contains("Export diagnostics", text);
        Assert.EndsWith("/main/docs/USER_MANUAL.md", QuickStartGuide.ManualUrl);
    }
}
