using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.Platform;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Tools;

public sealed record ToolPackagePlannedAction(
    ContentPackageCatalogEntry CatalogEntry,
    ToolPackageRelease Release,
    string InstallationRoot,
    ToolPackageAction Action,
    IReadOnlyList<ToolResolvedDependency> ResolvedDependencies);

public sealed record ToolPackageDependencyPlan(IReadOnlyList<ToolPackagePlannedAction> Actions);

public sealed class ToolPackageDependencyPlanner(
    ContentPackageCatalog catalog,
    ToolStateStore stateStore,
    string? runtimeIdentifier = null)
{
    private readonly string _runtimeIdentifier = runtimeIdentifier ?? PackagePlatform.Current;

    public async Task<ToolPackageDependencyPlan> CreateAsync(
        ContentPackageCatalogEntry targetEntry,
        ToolPackageRelease targetRelease,
        string targetRoot,
        ToolPackageAction targetAction,
        string productId,
        Func<ContentPackageCatalogEntry, string?> resolveInstallationRoot,
        Func<ContentPackageCatalogEntry, string, ToolPackageRelease?, ToolPackageInspection> inspect,
        Func<ContentPackageCatalogEntry, ToolReleaseChannel, CancellationToken, Task<ToolPackageRelease?>> getLatest,
        CancellationToken cancellationToken = default)
    {
        var actions = new List<ToolPackagePlannedAction>();
        var planned = new Dictionary<string, ToolPackagePlannedAction>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        async Task<ToolPackagePlannedAction> VisitAsync(
            ContentPackageCatalogEntry entry,
            ToolPackageRelease release,
            string root,
            ToolPackageAction action)
        {
            var key = Key(entry.PackageId, root);
            if (planned.TryGetValue(key, out var existing)) return existing;
            if (!visiting.Add(key))
            {
                throw new InvalidDataException($"Tool package dependency cycle detected at {entry.PackageId}.");
            }

            try
            {
                var resolvedDependencies = new List<ToolResolvedDependency>();
                foreach (var requirement in release.Manifest.Dependencies)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var dependencyEntry = catalog.Packages.SingleOrDefault(candidate =>
                        candidate.PackageId.Equals(requirement.PackageId, StringComparison.Ordinal));
                    if (dependencyEntry is null
                        || dependencyEntry.Category is not (ContentPackageCategory.Tool or ContentPackageCategory.AircraftComponent))
                    {
                        throw new InvalidDataException(
                            $"Required package {requirement.PackageId} is not an installable tool or aircraft component in the current catalog.");
                    }

                    if (!dependencyEntry.SupportedProducts.Contains(productId, StringComparer.Ordinal))
                    {
                        throw new InvalidDataException(
                            $"Required package {dependencyEntry.DisplayName} does not support {productId}.");
                    }

                    if (!dependencyEntry.SupportsPlatform(_runtimeIdentifier))
                    {
                        throw new InvalidDataException(PackagePlatform.Unavailable(
                            dependencyEntry.DisplayName,
                            dependencyEntry.SupportedPlatforms,
                            _runtimeIdentifier));
                    }

                    var dependencyRoot = resolveInstallationRoot(dependencyEntry);
                    if (string.IsNullOrWhiteSpace(dependencyRoot))
                    {
                        throw new InvalidDataException($"Installation root for required package {dependencyEntry.DisplayName} could not be resolved.");
                    }

                    dependencyRoot = Path.GetFullPath(dependencyRoot);
                    var current = inspect(dependencyEntry, dependencyRoot, null);
                    var currentVersion = NormalizeInstalledVersion(current.InstalledVersion);
                    var recorded = stateStore.TryGetToolInstallation(dependencyRoot, dependencyEntry.PackageId);
                    var currentSatisfies = recorded is not null
                        && current.State is ToolPackageInstallState.Current
                        && ToolPackageVersion.IsAtLeast(currentVersion, requirement.MinimumVersion);
                    if (currentSatisfies)
                    {
                        resolvedDependencies.Add(new ToolResolvedDependency(
                            dependencyEntry.PackageId,
                            requirement.MinimumVersion,
                            dependencyRoot,
                            currentVersion));
                        continue;
                    }

                    var channel = DependencyChannel(dependencyEntry);
                    var dependencyRelease = await getLatest(dependencyEntry, channel, cancellationToken).ConfigureAwait(false)
                        ?? throw new InvalidDataException($"No supported release is available for required package {dependencyEntry.DisplayName}.");
                    if (!ToolPackageVersion.IsAtLeast(dependencyRelease.Manifest.PackageVersion, requirement.MinimumVersion))
                    {
                        throw new InvalidDataException(
                            $"{entry.DisplayName} requires {dependencyEntry.DisplayName} {DisplayMinimum(requirement.MinimumVersion)}, "
                            + $"but the latest supported release is {dependencyRelease.Manifest.PackageVersion}.");
                    }

                    var dependencyInspection = inspect(dependencyEntry, dependencyRoot, dependencyRelease);
                    var dependencyAction = RequiredAction(dependencyInspection);
                    if (dependencyAction is null)
                    {
                        throw new InvalidDataException(
                            $"Required package {dependencyEntry.DisplayName} cannot be prepared: {dependencyInspection.Status}.");
                    }

                    var dependencyPlan = await VisitAsync(
                        dependencyEntry,
                        dependencyRelease,
                        dependencyRoot,
                        dependencyAction.Value).ConfigureAwait(false);
                    resolvedDependencies.Add(new ToolResolvedDependency(
                        dependencyEntry.PackageId,
                        requirement.MinimumVersion,
                        dependencyRoot,
                        dependencyPlan.Release.Manifest.PackageVersion));
                }

                var item = new ToolPackagePlannedAction(entry, release, Path.GetFullPath(root), action, resolvedDependencies);
                planned[key] = item;
                actions.Add(item);
                return item;
            }
            finally
            {
                visiting.Remove(key);
            }
        }

        await VisitAsync(targetEntry, targetRelease, targetRoot, targetAction).ConfigureAwait(false);
        return new ToolPackageDependencyPlan(actions);
    }

    public IReadOnlyList<string> FindRestoreBlockers(string packageId, string installationRoot, string restoredVersion)
    {
        var fullRoot = Path.GetFullPath(installationRoot);
        return stateStore.Load().ToolInstallations.Values
            .Where(installation => !string.IsNullOrWhiteSpace(installation.InstalledVersion))
            .SelectMany(installation => installation.Dependencies.Select(dependency => (installation, dependency)))
            .Where(item => item.dependency.PackageId.Equals(packageId, StringComparison.Ordinal)
                && PathsEqual(item.dependency.InstallationRoot, fullRoot)
                && !ToolPackageVersion.IsAtLeast(restoredVersion, item.dependency.MinimumVersion))
            .Select(item => string.IsNullOrWhiteSpace(restoredVersion)
                ? $"{item.installation.PackageId} requires {packageId}; restoring would remove the required package."
                : $"{item.installation.PackageId} requires {packageId} {DisplayMinimum(item.dependency.MinimumVersion)}; restore would leave {restoredVersion}.")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static ToolPackageAction? RequiredAction(ToolPackageInspection inspection) => inspection.State switch
    {
        ToolPackageInstallState.NotInstalled => ToolPackageAction.Install,
        ToolPackageInstallState.UpdateAvailable or ToolPackageInstallState.InstalledVersionUnknown => ToolPackageAction.Update,
        ToolPackageInstallState.SelectedReleaseOlder => ToolPackageAction.SwitchChannel,
        ToolPackageInstallState.RepairRequired or ToolPackageInstallState.Current => ToolPackageAction.Repair,
        _ => null
    };

    private static ToolReleaseChannel DependencyChannel(ContentPackageCatalogEntry entry) =>
        entry.SupportedChannels.Contains("stable", StringComparer.Ordinal)
            ? ToolReleaseChannel.Stable
            : entry.SupportedChannels.Contains("beta", StringComparer.Ordinal)
                ? ToolReleaseChannel.Beta
                : throw new InvalidDataException($"Required package {entry.DisplayName} has no supported release channel.");

    private static string NormalizeInstalledVersion(string version) =>
        version is "-" or "Unknown" or "Not checked" ? "" : version.Trim().TrimStart('v', 'V');

    private static string DisplayMinimum(string minimumVersion) =>
        string.IsNullOrWhiteSpace(minimumVersion) ? "in any version" : minimumVersion + " or newer";

    private static string Key(string packageId, string root) => packageId + "\n" + Path.GetFullPath(root);

    private static bool PathsEqual(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)).Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }
}
