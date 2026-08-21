using System.Security.Cryptography;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Content.PatchHandlers;
using LevelUp.NavTableUpdater.Core.Manifest;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Content;

public sealed class DeclarativePatchPlanBuilder : IContentPatchPlanBuilder<DeclarativePatchPackage>
{
    private readonly ToolStateStore _stateStore;
    private readonly ContentPatchHandlerRegistry _handlers;

    public DeclarativePatchPlanBuilder(
        ToolStateStore stateStore,
        ContentPatchHandlerRegistry? handlers = null)
    {
        _stateStore = stateStore;
        _handlers = handlers ?? ContentPatchHandlerRegistry.CreateBuiltIn();
    }

    public Task<ContentPatchPlan> BuildAsync(
        ContentPatchAction action,
        AircraftVariantViewAnalysis variant,
        DeclarativePatchPackage package,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DeclarativePatchManifestParser.Validate(package.Manifest);
        var manifest = package.Manifest;
        var descriptor = DescriptorFor(manifest);
        var aircraftRoot = Path.GetDirectoryName(variant.AcfPath) ?? "";
        var log = new List<string>
        {
            $"[START] {descriptor.DisplayName} {action} for {variant.DisplayName}",
            $"[PACKAGE] {manifest.PackageId} {manifest.PackageVersion}",
            $"[POLICY] {descriptor.Lifecycle.Activation}; triggers={string.Join(",", descriptor.Lifecycle.Triggers)}"
        };

        var supportedProducts = DeclarativePatchProductCompatibility.ResolveSupportedProducts(manifest);
        var selectedProduct = AircraftProductIds.Normalize(variant.Family);
        if (supportedProducts.Count == 0
            || selectedProduct is null
            || !supportedProducts.Contains(selectedProduct))
        {
            return Task.FromResult(ContentPatchPlan.Blocked(
                descriptor,
                manifest.PackageVersion,
                action,
                aircraftRoot,
                $"Package supports [{string.Join(", ", supportedProducts)}]; selected variant is {variant.Family}.",
                log));
        }

        var componentState = _stateStore.TryGetContentInstallation(aircraftRoot)?.ContentComponents?
            .GetValueOrDefault(manifest.PackageId);
        if (action is ContentPatchAction.Uninstall)
        {
            return Task.FromResult(BuildUninstallPlan(descriptor, manifest, aircraftRoot, componentState, log));
        }

        var manifestTargets = manifest.Targets.Select(target => target.RelativePath).ToHashSet(StringComparer.Ordinal);
        var omittedInstalledTargets = componentState?.Files
            .Where(file => !manifestTargets.Contains(file.RelativePath))
            .Select(file => file.RelativePath)
            .ToArray() ?? [];
        if (omittedInstalledTargets.Length > 0)
        {
            return Task.FromResult(ContentPatchPlan.Blocked(
                descriptor,
                manifest.PackageVersion,
                action,
                aircraftRoot,
                $"Package update omits previously installed targets: {string.Join(", ", omittedInstalledTargets)}. An explicit migration package is required.",
                log));
        }

        var targetStates = new List<TargetState>();
        var stateMatchesPackage = componentState?.PackageVersion.Equals(manifest.PackageVersion, StringComparison.OrdinalIgnoreCase) ?? false;
        foreach (var target in manifest.Targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var handler = _handlers.GetRequired(target.Operation);
            var usesStructuralSourceValidation = target.SourceSha256.Count == 0;
            if (usesStructuralSourceValidation && !handler.SupportsStructuralSourceValidation)
            {
                return Task.FromResult(ContentPatchPlan.Blocked(
                    descriptor,
                    manifest.PackageVersion,
                    action,
                    aircraftRoot,
                    $"Patch operation {target.Operation} requires sourceSha256 for {target.RelativePath}.",
                    [.. log, $"[BLOCKED] {target.Operation} cannot validate a hashless source structurally."]));
            }

            var targetPath = ResolveTarget(aircraftRoot, target.RelativePath);
            if (!File.Exists(targetPath))
            {
                return Task.FromResult(ContentPatchPlan.Blocked(
                    descriptor,
                    manifest.PackageVersion,
                    action,
                    aircraftRoot,
                    $"Required aircraft file is missing: {target.RelativePath}.",
                    log));
            }

            var bytes = File.ReadAllBytes(targetPath);
            var hash = Sha256(bytes);
            var recorded = componentState?.Files.FirstOrDefault(file =>
                string.Equals(file.RelativePath, target.RelativePath, StringComparison.Ordinal));
            var isSource = target.SourceSha256.Contains(hash, StringComparer.OrdinalIgnoreCase);
            var isInstalled = (target.ResultSha256?.Equals(hash, StringComparison.OrdinalIgnoreCase) ?? false)
                || (stateMatchesPackage && (recorded?.InstalledSha256?.Equals(hash, StringComparison.OrdinalIgnoreCase) ?? false));
            byte[]? preparedResult = null;
            if (usesStructuralSourceValidation && !isInstalled)
            {
                if (!package.Payloads.TryGetValue(target.Payload, out var structuralPayload))
                {
                    throw new InvalidOperationException($"Resolved patch payload is missing: {target.Payload}.");
                }

                try
                {
                    preparedResult = handler.Apply(
                        bytes,
                        structuralPayload.Json?.RootElement
                            ?? throw new InvalidOperationException($"Patch payload is not JSON: {target.Payload}."));
                    isInstalled = preparedResult.AsSpan().SequenceEqual(bytes);
                    isSource = !isInstalled;
                    log.Add(isInstalled
                        ? $"[STRUCTURAL] {target.RelativePath} already contains every exact installed block."
                        : $"[STRUCTURAL] {target.RelativePath} contains every required exact source/installed block.");
                }
                catch (InvalidOperationException ex)
                {
                    return Task.FromResult(ContentPatchPlan.Blocked(
                        descriptor,
                        manifest.PackageVersion,
                        action,
                        aircraftRoot,
                        $"Structurally incompatible patch target: {target.RelativePath}. {ex.Message}",
                        [.. log, $"[BLOCKED] Structural validation failed for {target.RelativePath}: {ex.Message}"]));
                }
            }

            if (!isSource && !isInstalled)
            {
                return Task.FromResult(ContentPatchPlan.Blocked(
                    descriptor,
                    manifest.PackageVersion,
                    action,
                    aircraftRoot,
                    $"Unsupported or modified patch target: {target.RelativePath}.",
                    [.. log, $"[BLOCKED] {target.RelativePath} SHA-256 {hash} is neither a supported source nor the recorded result."]));
            }

            targetStates.Add(new TargetState(target, bytes, hash, isInstalled, preparedResult));
        }

        if (targetStates.All(state => state.IsInstalled))
        {
            log.Add("[NO-CHANGE] Every declarative patch target is already installed.");
            ContentPatchMutation[] stateRefreshMutations = componentState is null
                ? []
                : targetStates.Select(state => ContentPatchMutation.Write(
                    state.Target.RelativePath,
                    state.Bytes,
                    $"Retained current {descriptor.DisplayName} target")).ToArray();
            return Task.FromResult(new ContentPatchPlan(
                descriptor,
                manifest.PackageVersion,
                action,
                aircraftRoot,
                stateRefreshMutations,
                log,
                IsSafe: true,
                $"{descriptor.DisplayName} {manifest.PackageVersion} is already installed."));
        }

        if (componentState is null && targetStates.Any(state => state.IsInstalled))
        {
            return Task.FromResult(ContentPatchPlan.Blocked(
                descriptor,
                manifest.PackageVersion,
                action,
                aircraftRoot,
                "The patch is partially present but has no toolkit-owned restore state. Restore the original package before installing it with this toolkit.",
                log));
        }

        var mutations = new List<ContentPatchMutation>();
        foreach (var state in targetStates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (state.IsInstalled)
            {
                mutations.Add(ContentPatchMutation.Write(
                    state.Target.RelativePath,
                    state.Bytes,
                    $"Retained current {descriptor.DisplayName} target"));
                continue;
            }

            if (!package.Payloads.TryGetValue(state.Target.Payload, out var payload))
            {
                throw new InvalidOperationException($"Resolved patch payload is missing: {state.Target.Payload}.");
            }

            var handler = _handlers.GetRequired(state.Target.Operation);
            var result = state.PreparedResult ?? handler.Apply(
                state.Bytes,
                payload.Json?.RootElement
                    ?? throw new InvalidOperationException($"Patch payload is not JSON: {state.Target.Payload}."));
            var resultHash = Sha256(result);
            if (state.Target.ResultSha256 is not null
                && !resultHash.Equals(state.Target.ResultSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Generated result hash mismatch for {state.Target.RelativePath}.");
            }

            mutations.Add(ContentPatchMutation.Write(
                state.Target.RelativePath,
                result,
                $"Applied {state.Target.Operation}"));
            log.Add($"[PLAN] {state.Target.RelativePath}: {state.Hash} -> {resultHash} ({state.Target.Operation}).");
        }

        return Task.FromResult(new ContentPatchPlan(
            descriptor,
            manifest.PackageVersion,
            action,
            aircraftRoot,
            mutations,
            log,
            IsSafe: true,
            $"{descriptor.DisplayName} {action} completed for package {manifest.PackageVersion}."));
    }

    private static ContentPatchPlan BuildUninstallPlan(
        ContentPatchDescriptor descriptor,
        DeclarativePatchManifest manifest,
        string aircraftRoot,
        ContentComponentState? state,
        List<string> log)
    {
        if (state is null || state.Files.Count == 0)
        {
            return ContentPatchPlan.Blocked(
                descriptor,
                manifest.PackageVersion,
                ContentPatchAction.Uninstall,
                aircraftRoot,
                "No toolkit-owned installation state is available for a safe uninstall.",
                log);
        }

        var mutations = new List<ContentPatchMutation>();
        foreach (var file in state.Files)
        {
            var targetPath = ResolveTarget(aircraftRoot, file.RelativePath);
            var currentHash = File.Exists(targetPath) ? Sha256(File.ReadAllBytes(targetPath)) : null;
            if (!string.Equals(currentHash, file.InstalledSha256, StringComparison.OrdinalIgnoreCase))
            {
                return ContentPatchPlan.Blocked(
                    descriptor,
                    manifest.PackageVersion,
                    ContentPatchAction.Uninstall,
                    aircraftRoot,
                    $"Installed target changed after patching; refusing to overwrite it: {file.RelativePath}.",
                    log);
            }

            if (!file.OriginalExisted)
            {
                mutations.Add(ContentPatchMutation.Delete(file.RelativePath, "Removed file created by optional patch"));
                continue;
            }

            if (string.IsNullOrWhiteSpace(file.BackupPath) || !File.Exists(file.BackupPath))
            {
                return ContentPatchPlan.Blocked(
                    descriptor,
                    manifest.PackageVersion,
                    ContentPatchAction.Uninstall,
                    aircraftRoot,
                    $"Original backup is missing for {file.RelativePath}.",
                    log);
            }

            var backupBytes = File.ReadAllBytes(file.BackupPath);
            if (backupBytes.LongLength != file.OriginalSizeBytes
                || !Sha256(backupBytes).Equals(file.OriginalSha256, StringComparison.OrdinalIgnoreCase))
            {
                return ContentPatchPlan.Blocked(
                    descriptor,
                    manifest.PackageVersion,
                    ContentPatchAction.Uninstall,
                    aircraftRoot,
                    $"Original backup failed integrity validation for {file.RelativePath}.",
                    log);
            }

            mutations.Add(ContentPatchMutation.Write(file.RelativePath, backupBytes, "Restored pre-patch aircraft file"));
        }

        return new ContentPatchPlan(
            descriptor,
            manifest.PackageVersion,
            ContentPatchAction.Uninstall,
            aircraftRoot,
            mutations,
            log,
            IsSafe: true,
            $"{descriptor.DisplayName} was uninstalled and original files were restored.");
    }

    internal static ContentPatchDescriptor DescriptorFor(DeclarativePatchManifest manifest)
    {
        if (manifest.PackageId.Equals(ContentPatchCatalog.FansCdu.ComponentId, StringComparison.Ordinal))
        {
            return ContentPatchCatalog.FansCdu with
            {
                RepositoryUrl = manifest.RepositoryUrl,
                RestartRequired = manifest.RestartRequired
            };
        }

        return new ContentPatchDescriptor(
            manifest.PackageId,
            manifest.PackageId,
            manifest.RepositoryUrl,
            new ContentPatchLifecyclePolicy(
                ContentPatchActivation.ExplicitOptIn,
                new HashSet<ContentPatchTrigger> { ContentPatchTrigger.Manual }),
            manifest.RestartRequired);
    }

    private static string ResolveTarget(string aircraftRoot, string relativePath)
        => ContentPatchPathSafety.ResolveTarget(aircraftRoot, relativePath, "Patch target");

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record TargetState(
        DeclarativePatchTarget Target,
        byte[] Bytes,
        string Hash,
        bool IsInstalled,
        byte[]? PreparedResult);
}
