using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Platform;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Content;

public sealed class DeclarativeContentPatchOperation
{
    private readonly DeclarativePatchPlanBuilder _planBuilder;
    private readonly ContentPatchEngine _engine;
    private readonly Func<bool> _isXPlaneRunning;

    public DeclarativeContentPatchOperation(
        ToolStateStore stateStore,
        Func<bool>? isXPlaneRunning = null)
    {
        _isXPlaneRunning = isXPlaneRunning ?? XPlaneProcessDetector.IsXPlaneRunning;
        _planBuilder = new DeclarativePatchPlanBuilder(stateStore);
        _engine = new ContentPatchEngine(stateStore, _isXPlaneRunning);
    }

    public async Task<MaintenanceOperationResult> RunAsync(
        ContentPatchAction action,
        AircraftVariantViewAnalysis variant,
        string packageDirectory,
        CancellationToken cancellationToken = default)
    {
        if (_isXPlaneRunning())
        {
            return MaintenanceOperationResult.Blocked(
                "X-Plane is running. Close X-Plane before changing aircraft files.",
                ["[BLOCKED] X-Plane is running; optional patch package was not loaded."]);
        }

        var plan = await PlanAsync(action, variant, packageDirectory, cancellationToken).ConfigureAwait(false);
        return _engine.Execute(plan, variant);
    }

    public async Task<ContentPatchPlan> PlanAsync(
        ContentPatchAction action,
        AircraftVariantViewAnalysis variant,
        string packageDirectory,
        CancellationToken cancellationToken = default)
    {
        var package = DeclarativePatchPackageLoader.LoadDirectory(packageDirectory);
        return await _planBuilder.BuildAsync(action, variant, package, cancellationToken).ConfigureAwait(false);
    }

    public MaintenanceOperationResult Restore(
        AircraftVariantViewAnalysis variant,
        string packageDirectory)
    {
        var package = DeclarativePatchPackageLoader.LoadDirectory(packageDirectory);
        return _engine.Restore(DeclarativePatchPlanBuilder.DescriptorFor(package.Manifest), variant);
    }

    public MaintenanceOperationResult Restore(
        ContentPatchDescriptor descriptor,
        AircraftVariantViewAnalysis variant) =>
        _engine.Restore(descriptor, variant);
}
