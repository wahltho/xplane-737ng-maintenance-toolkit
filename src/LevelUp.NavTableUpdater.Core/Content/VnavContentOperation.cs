using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Analysis;
using LevelUp.NavTableUpdater.Core.Manifest;
using LevelUp.NavTableUpdater.Core.Platform;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Content;

public enum VnavContentAction
{
    Install,
    Update,
    Repair,
    Uninstall
}

public sealed class VnavContentOperation
{
    private readonly IPackagePayloadSource _payloadSource;
    private readonly ContentPatchEngine _engine;
    private readonly VnavContentPlanBuilder _planBuilder = new();
    private readonly AircraftInstallAnalyzer _analyzer = new();
    private readonly Func<bool> _isXPlaneRunning;

    public VnavContentOperation(
        ToolStateStore stateStore,
        IPackagePayloadSource payloadSource,
        Func<bool>? isXPlaneRunning = null)
    {
        _payloadSource = payloadSource;
        _isXPlaneRunning = isXPlaneRunning ?? XPlaneProcessDetector.IsXPlaneRunning;
        _engine = new ContentPatchEngine(stateStore, _isXPlaneRunning);
    }

    public async Task<MaintenanceOperationResult> RunAsync(
        VnavContentAction action,
        AircraftVariantViewAnalysis variant,
        PackageManifest manifest,
        CancellationToken cancellationToken = default)
    {
        var genericAction = action switch
        {
            VnavContentAction.Install => ContentPatchAction.Install,
            VnavContentAction.Update => ContentPatchAction.Update,
            VnavContentAction.Repair => ContentPatchAction.Repair,
            VnavContentAction.Uninstall => ContentPatchAction.Uninstall,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

        if (_isXPlaneRunning())
        {
            return MaintenanceOperationResult.Blocked(
                "X-Plane is running. Close X-Plane before changing aircraft files.",
                [
                    $"[START] VNAV {action} for {variant.DisplayName}",
                    "[BLOCKED] X-Plane is running."
                ]);
        }

        IReadOnlyDictionary<string, PackagePayload> payloads = new Dictionary<string, PackagePayload>();
        if (genericAction is not ContentPatchAction.Uninstall)
        {
            var aircraftRoot = Path.GetDirectoryName(variant.AcfPath) ?? "";
            var analysis = _analyzer.Analyze(aircraftRoot, manifest);
            if (analysis.IsSafeToPatch
                && (analysis.State is not InstallState.CorrectlyInstalled || genericAction is ContentPatchAction.Repair))
            {
                payloads = await _payloadSource.GetPayloadsAsync(manifest, cancellationToken).ConfigureAwait(false);
            }
        }

        var plan = await _planBuilder.BuildAsync(
            genericAction,
            variant,
            new VnavContentPackage(manifest, payloads),
            cancellationToken).ConfigureAwait(false);
        return _engine.Execute(plan, variant);
    }

    public MaintenanceOperationResult RestoreLatest(
        AircraftVariantViewAnalysis variant,
        PackageManifest manifest) =>
        _engine.Restore(
            ContentPatchCatalog.Vnav(manifest.PackageId, manifest.RepositoryUrl),
            variant,
            stateOperation: "VnavContentRestore");
}
