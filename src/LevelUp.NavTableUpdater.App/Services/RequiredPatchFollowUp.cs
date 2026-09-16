using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Content;

namespace LevelUp.NavTableUpdater.App.Services;

internal static class RequiredPatchFollowUp
{
    public static async Task<MaintenanceOperationResult?> RunAsync(
        ContentPackageCatalog catalog,
        string family,
        Func<Task<MaintenanceOperationResult>> applyGroup)
    {
        if (AircraftProductIds.Normalize(family) != AircraftProductIds.LevelUp737Ng)
            return null;

        // Required group maintenance is independent of the installed VNAV state.
        var group = catalog.ForProduct(AircraftProductIds.LevelUp737Ng).SingleOrDefault(p =>
            p.Distribution.Kind == ContentPackageDistributionKind.CatalogGroup);
        if (group is null)
            return Pending("The LevelUp maintenance group is unavailable. Refresh the catalog and retry Update.");

        try
        {
            var result = await applyGroup();
            return result.Succeeded ? result : result with
            {
                Status = "Required patches pending",
                Message = "Required patches are still pending. " + result.Message
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
            or HttpRequestException or OperationCanceledException)
        {
            return Pending("Required patch maintenance did not complete: " + ex.Message);
        }
    }

    private static MaintenanceOperationResult Pending(string message) =>
        new(false, false, "Required patches pending", message, [], ["[PENDING] " + message]);
}
