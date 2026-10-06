using CommunityToolkit.Mvvm.ComponentModel;
using LevelUp.NavTableUpdater.App.Services;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.Manifest;

namespace LevelUp.NavTableUpdater.App.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AircraftOverviewVisible))]
    private AircraftOverviewSummary? aircraftOverview;

    public bool AircraftOverviewVisible => AircraftOverview is not null;
    [ObservableProperty]
    private IReadOnlyList<PatchModuleOverview> patchModules = [];
    private readonly Dictionary<string, DateTimeOffset> _patchReleaseCheckTimes = new(StringComparer.Ordinal);
    private DateTimeOffset? _aircraftReleaseCheckTime;

    private void RefreshAircraftOverview()
    {
        CheckInstallationCommand.NotifyCanExecuteChanged();
        if (InstallationCheck is { } check && check.AircraftFolder != SelectedProduct?.AircraftFolderPath)
            ClearInstallationCheck();
        PatchModules = [];
        if (SelectedProduct?.IsDetected != true || SelectedViewVariant is not { } variant
            || !PathsEqual(SelectedProduct.AircraftFolderPath, Path.GetDirectoryName(variant.AcfPath) ?? ""))
        {
            AircraftOverview = null;
            return;
        }
        var family = AircraftProductIds.Normalize(variant.Family) ?? "";
        var packages = _contentPackageCatalog.ForProduct(family);
        var installation = _stateStore.TryGetContentInstallation(SelectedProduct.AircraftFolderPath);
        var groups = packages.Where(p => p.Distribution.Kind == ContentPackageDistributionKind.CatalogGroup).ToArray();
        var groupedIds = groups.SelectMany(g => g.Members).Select(m => m.PackageId).ToHashSet(StringComparer.Ordinal);
        var patches = new List<AircraftPatchVersion>();
        var rows = new List<PatchModuleOverview>();
        var patchCheckFailed = false;
        foreach (var group in groups)
        {
            var state = installation?.ContentComponents.GetValueOrDefault(group.PackageId);
            var failed = _contentPatchReleaseErrors.ContainsKey(group.PackageId);
            patchCheckFailed |= failed;
            var latest = !failed && _contentPatchReleases.ContainsKey(group.PackageId)
                ? _catalogGroupResolutions.GetValueOrDefault(group.PackageId) : null;
            foreach (var member in group.Members.OrderBy(m => m.InstallationOrder))
            {
                var source = state?.EnabledModules.Contains(member.ModuleId, StringComparer.Ordinal) == true
                    ? state.Sources.FirstOrDefault(s => s.ModuleId == member.ModuleId) : null;
                var installed = source?.ReleaseTag
                    ?? installation?.ContentComponents.GetValueOrDefault(member.PackageId)?.PackageVersion;
                // Older group state can record a module without source version data.
                if (installed is null && state?.EnabledModules.Contains(member.ModuleId, StringComparer.Ordinal) == true)
                    installed = "version unknown";
                var available = latest?.Sources.FirstOrDefault(s => s.Member.ModuleId == member.ModuleId).Release?.Tag;
                var name = packages.FirstOrDefault(p => p.PackageId == member.PackageId)?.DisplayName ?? member.ModuleId;
                var patch = new AircraftPatchVersion(name, member.Policy == CompatibilityModulePolicy.Required, installed, available);
                patches.Add(patch);
                rows.Add(PatchModuleOverview.Create(member.ModuleId, patch, failed, IsContentPackageCatalogCheckRunning));
            }
        }
        foreach (var package in packages.Where(p => !groupedIds.Contains(p.PackageId)
                     && p.Distribution.Kind != ContentPackageDistributionKind.CatalogGroup
                     && p.Category is ContentPackageCategory.OptionalPatch or ContentPackageCategory.CompatibilityPackage))
        {
            var failed = _contentPatchReleaseErrors.ContainsKey(package.PackageId);
            patchCheckFailed |= failed;
            var patch = new AircraftPatchVersion(package.DisplayName, false,
                installation?.ContentComponents.GetValueOrDefault(package.PackageId)?.PackageVersion,
                failed ? null : _contentPatchReleases.GetValueOrDefault(package.PackageId)?.Tag);
            patches.Add(patch);
            rows.Add(PatchModuleOverview.Create(package.PackageId, patch, failed, IsContentPackageCatalogCheckRunning));
        }
        PatchModules = rows;
        var aircraftFailed = _lastUpstreamUpdateCheck is null && _aircraftReleaseCheckTime is not null;
        var aircraftCheckedAt = _aircraftReleaseCheckTime?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "Not checked";
        var checkedAt = _patchReleaseCheckTimes.TryGetValue(family, out var time)
            ? time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "Not checked";
        AircraftOverview = AircraftOverviewSummary.Create(family == AircraftProductIds.LevelUp737Ng,
            groups.Length > 0, UpstreamLocalVersion, _lastUpstreamUpdateCheck, patches,
            IsUpstreamCheckRunning || IsContentPackageCatalogCheckRunning, aircraftFailed, patchCheckFailed,
            aircraftCheckedAt, checkedAt);
    }
}
