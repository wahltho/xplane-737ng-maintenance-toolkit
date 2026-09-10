using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.Manifest;

namespace LevelUp.NavTableUpdater.App.Services;

internal static class RequiredPatchFollowUp
{
    public static async Task<MaintenanceOperationResult?> RunAsync(
        ContentPackageCatalog catalog,
        string family,
        IUserInteractionService interaction,
        Func<Task<MaintenanceOperationResult>> applyGroup)
    {
        if (AircraftProductIds.Normalize(family) != AircraftProductIds.LevelUp737Ng)
            return null;

        // Required group maintenance is independent of the installed VNAV state.
        var group = catalog.ForProduct(AircraftProductIds.LevelUp737Ng).SingleOrDefault(p =>
            p.Distribution.Kind == ContentPackageDistributionKind.CatalogGroup);
        if (group is null)
            return Pending("The LevelUp maintenance group is unavailable. Refresh the catalog and retry Update.");

        var names = group.Members.Where(m => m.Policy == CompatibilityModulePolicy.Required)
            .Select(m => catalog.Packages.Single(p => p.PackageId == m.PackageId).DisplayName);
        if (!await interaction.ConfirmAsync(new ConfirmationRequest(
                "Update required LevelUp patches?",
                $"The aircraft update is not complete until the required patches have been checked: {string.Join(", ", names)}. " +
                "The latest stable source releases will be resolved and validated. Previously selected optional patches are retained.",
                "Update patches", "Not now")))
            return Pending("Required patch maintenance was deferred. Run Update again before flying.");

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
