using LevelUp.NavTableUpdater.Core.Aircraft;

namespace LevelUp.NavTableUpdater.App.Services;

public sealed record QuickStartTopic(string Title, string Text);

public static class QuickStartGuide
{
    public const string ManualUrl = "https://github.com/wahltho/xplane-737ng-maintenance-toolkit/blob/main/docs/USER_MANUAL.md";

    public static IReadOnlyList<QuickStartTopic> ForProduct(string? family)
    {
        var patchAdvice = family switch
        {
            AircraftProductIds.LevelUp737Ng => "LevelUp requires VNAV, FANS CDU and Weight & Balance. Other patches are optional.",
            AircraftProductIds.Zibo737Ng => "All Zibo patches are optional. None is selected automatically.",
            _ => "LevelUp has three required patches; all Zibo patches are optional."
        };
        return
        [
            new("1. Choose your aircraft", "Click Browse on the left and select your aircraft folder. Check that the product and folder shown are the ones you want."),
            new("2. Install a new aircraft", "Under Install new aircraft, choose the product and X-Plane folder. Choose a new destination folder, click Check package, then Install. Read the confirmation before proceeding."),
            new("3. Update your aircraft", "Check Update status on Start. Click Update under Aircraft package and confirm the proposed changes. " + patchAdvice),
            new("4. Choose optional patches", "In Advanced, tick the optional patches you want. Click Review, then Install/update. Ticking a box only changes your selection. Patch versions on Start shows what MTK lists as installed."),
            new("5. Check files or repair a package", "Check installation compares files and backups with MTK's records without changing them. Repair does change files. If a change is blocked, open the result help. Keep the aircraft, MTK state and backups unchanged while you investigate."),
            new("6. Restore a package", "Click Restore beside the aircraft package, patch package or tool you want to restore. Each uses its own backups. MTK checks whether they can be used; a backup appearing in the history does not mean it can be restored."),
            new("7. Get help", "Use Export diagnostics and check the ZIP before sharing it. Attach it to your question on Discord or in the Toolkit download page comments.")
        ];
    }
}
