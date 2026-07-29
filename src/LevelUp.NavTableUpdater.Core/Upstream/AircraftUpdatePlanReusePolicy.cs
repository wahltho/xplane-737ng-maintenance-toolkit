namespace LevelUp.NavTableUpdater.Core.Upstream;

public static class AircraftUpdatePlanReusePolicy
{
    public static bool CanReuseValidatedLocalPlan(
        AircraftUpstreamUpdateCheckResult? updateCheck,
        IReadOnlyCollection<AircraftUpdatePackageCacheEntry> cacheEntries)
    {
        if (updateCheck is null
            || updateCheck.IsCustomDistribution
            || updateCheck.RequiredPackages.Count == 0
            || !IsLocalManifestPath(updateCheck.SourceUrl)
            || cacheEntries.Count != updateCheck.RequiredPackages.Count)
        {
            return false;
        }

        return updateCheck.RequiredPackages.All(package =>
            cacheEntries.Any(entry =>
                entry.IsCached
                && string.Equals(
                    entry.Package.FileName,
                    package.FileName,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    entry.Package.ExpectedSha256,
                    package.ExpectedSha256,
                    StringComparison.OrdinalIgnoreCase)
                && entry.Package.ExpectedSizeBytes == package.ExpectedSizeBytes));
    }

    private static bool IsLocalManifestPath(string sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl))
        {
            return false;
        }

        try
        {
            return Path.IsPathFullyQualified(sourceUrl);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
