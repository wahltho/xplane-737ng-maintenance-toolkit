using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Platform;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Content;

public sealed class CompatibilityPackageOperation
{
    private readonly CompatibilityPackagePlanBuilder _planBuilder;
    private readonly ContentPatchEngine _engine;
    private readonly Func<bool> _isXPlaneRunning;

    public CompatibilityPackageOperation(ToolStateStore stateStore, Func<bool>? isXPlaneRunning = null)
    {
        _isXPlaneRunning = isXPlaneRunning ?? XPlaneProcessDetector.IsXPlaneRunning;
        _planBuilder = new CompatibilityPackagePlanBuilder(stateStore);
        _engine = new ContentPatchEngine(stateStore, _isXPlaneRunning);
    }

    public async Task<ContentPatchPlan> PlanAsync(
        ContentPatchAction action,
        AircraftVariantViewAnalysis variant,
        string packageDirectory,
        IReadOnlyCollection<string> selectedModuleIds,
        CancellationToken cancellationToken = default)
    {
        var package = CompatibilityPackageLoader.LoadDirectory(packageDirectory);
        return await _planBuilder.BuildAsync(action, variant, package, selectedModuleIds, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<MaintenanceOperationResult> RunAsync(
        ContentPatchAction action,
        AircraftVariantViewAnalysis variant,
        string packageDirectory,
        IReadOnlyCollection<string> selectedModuleIds,
        CancellationToken cancellationToken = default)
    {
        if (_isXPlaneRunning())
        {
            return MaintenanceOperationResult.Blocked(
                "X-Plane is running. Close X-Plane before changing aircraft files.",
                ["[BLOCKED] X-Plane is running; compatibility package was not loaded."]);
        }

        var plan = await PlanAsync(action, variant, packageDirectory, selectedModuleIds, cancellationToken)
            .ConfigureAwait(false);
        return _engine.Execute(plan, variant);
    }

    public MaintenanceOperationResult Restore(AircraftVariantViewAnalysis variant, string packageDirectory)
    {
        var package = CompatibilityPackageLoader.LoadDirectory(packageDirectory);
        return _engine.Restore(CompatibilityPackagePlanBuilder.DescriptorFor(package.Manifest), variant);
    }

    public MaintenanceOperationResult Restore(
        ContentPatchDescriptor descriptor,
        AircraftVariantViewAnalysis variant) =>
        _engine.Restore(descriptor, variant);
}
