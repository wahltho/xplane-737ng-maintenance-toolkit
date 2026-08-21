using System.Security.Cryptography;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Content.PatchHandlers;
using LevelUp.NavTableUpdater.Core.Manifest;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Content;

public sealed class CompatibilityPackagePlanBuilder
{
    private readonly ToolStateStore _stateStore;
    private readonly ContentPatchHandlerRegistry _handlers;

    public CompatibilityPackagePlanBuilder(
        ToolStateStore stateStore,
        ContentPatchHandlerRegistry? handlers = null)
    {
        _stateStore = stateStore;
        _handlers = handlers ?? ContentPatchHandlerRegistry.CreateBuiltIn();
    }

    public Task<ContentPatchPlan> BuildAsync(
        ContentPatchAction action,
        AircraftVariantViewAnalysis variant,
        CompatibilityPackage package,
        IReadOnlyCollection<string> selectedModuleIds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CompatibilityPackageManifestParser.Validate(package.Manifest);
        var manifest = package.Manifest;
        var descriptor = DescriptorFor(manifest);
        var aircraftRoot = Path.GetDirectoryName(variant.AcfPath) ?? "";
        var log = new List<string>
        {
            $"[START] {descriptor.DisplayName} {action} for {AircraftProductIdentity.FromVariant(variant).DisplayName}",
            $"[PACKAGE] {manifest.PackageId} {manifest.PackageVersion}"
        };

        var selectedProduct = AircraftProductIds.Normalize(variant.Family);
        if (selectedProduct is null || !manifest.SupportedProducts.Contains(selectedProduct, StringComparer.Ordinal))
        {
            return Task.FromResult(Blocked(
                descriptor,
                manifest,
                action,
                aircraftRoot,
                $"Package supports [{string.Join(", ", manifest.SupportedProducts)}]; selected variant is {variant.Family}.",
                log));
        }

        if (manifest.SupportedUpstreamReleases.Count > 0
            && !SupportsUpstreamRelease(manifest.SupportedUpstreamReleases, variant))
        {
            return Task.FromResult(Blocked(
                descriptor,
                manifest,
                action,
                aircraftRoot,
                $"The selected aircraft version is not supported by compatibility package {manifest.PackageVersion}.",
                [.. log, $"[BLOCKED] Supported aircraft releases: {string.Join(", ", manifest.SupportedUpstreamReleases)}."]));
        }

        if (!TryResolveSelection(manifest, selectedModuleIds, out var selectedModules, out var selectionError))
        {
            return Task.FromResult(Blocked(descriptor, manifest, action, aircraftRoot, selectionError, log));
        }

        var selectedIds = selectedModules.Select(module => module.ModuleId).ToArray();
        log.Add($"[MODULES] {string.Join(", ", selectedModules.Select(module => $"{module.ModuleId} ({module.Policy})"))}");
        var componentState = _stateStore.TryGetContentInstallation(aircraftRoot)?.ContentComponents?
            .GetValueOrDefault(manifest.PackageId);
        if (action is ContentPatchAction.Uninstall)
        {
            return Task.FromResult(BuildUninstallPlan(descriptor, manifest, aircraftRoot, componentState, log));
        }

        if (selectedModules.Count == 0)
        {
            if (componentState is not null)
            {
                return Task.FromResult(BuildUninstallPlan(descriptor, manifest, aircraftRoot, componentState, log));
            }

            return Task.FromResult(new ContentPatchPlan(
                descriptor,
                manifest.PackageVersion,
                action,
                aircraftRoot,
                [],
                [.. log, "[NO-CHANGE] No compatibility modules are selected."],
                IsSafe: true,
                "No compatibility modules are selected; no aircraft files need to change."));
        }

        var selectedOperations = selectedModules
            .SelectMany(module => module.Targets.Select(target => new ModuleTarget(module, target)))
            .GroupBy(item => item.Target.RelativePath, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var targetPaths = selectedOperations.Keys
            .Concat(componentState?.Files.Select(file => file.RelativePath) ?? [])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var mutations = new List<ContentPatchMutation>();

        foreach (var relativePath in targetPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetPath = ContentPatchPathSafety.ResolveTarget(aircraftRoot, relativePath, "Compatibility target");
            var previousFile = componentState?.Files.FirstOrDefault(file =>
                file.RelativePath.Equals(relativePath, StringComparison.Ordinal));
            byte[] sourceBytes;
            var currentExists = File.Exists(targetPath);
            var currentBytes = currentExists ? File.ReadAllBytes(targetPath) : [];
            bool sourceExists;

            if (previousFile is null)
            {
                sourceExists = currentExists;
                sourceBytes = currentBytes;
                if (!currentExists
                    && (!selectedOperations.TryGetValue(relativePath, out var newFileOperations)
                        || !newFileOperations[0].Target.Operation.Equals("copy-file-v1", StringComparison.Ordinal)))
                {
                    return Task.FromResult(Blocked(
                        descriptor,
                        manifest,
                        action,
                        aircraftRoot,
                        $"Required aircraft file is missing: {relativePath}.",
                        log));
                }

            }
            else
            {
                if (!currentExists)
                {
                    return Task.FromResult(Blocked(descriptor, manifest, action, aircraftRoot,
                        $"Previously managed target is missing: {relativePath}.", log));
                }

                if (!Sha256(currentBytes).Equals(previousFile.InstalledSha256, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(Blocked(descriptor, manifest, action, aircraftRoot,
                        $"Managed target changed after installation: {relativePath}.", log));
                }

                sourceExists = previousFile.OriginalExisted;
                if (!sourceExists)
                {
                    sourceBytes = [];
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(previousFile.BackupPath)
                        || !File.Exists(previousFile.BackupPath))
                    {
                        return Task.FromResult(Blocked(descriptor, manifest, action, aircraftRoot,
                            $"Original compatibility-package backup is missing for {relativePath}.", log));
                    }

                    sourceBytes = File.ReadAllBytes(previousFile.BackupPath);
                    if (sourceBytes.LongLength != previousFile.OriginalSizeBytes
                        || !Sha256(sourceBytes).Equals(previousFile.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        return Task.FromResult(Blocked(descriptor, manifest, action, aircraftRoot,
                            $"Original compatibility-package backup failed validation for {relativePath}.", log));
                    }
                }
            }

            var desiredBytes = sourceBytes;
            var desiredExists = sourceExists;
            if (selectedOperations.TryGetValue(relativePath, out var operations))
            {
                foreach (var operation in operations)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var modulePackage = package.Modules[operation.Module.ModuleId];
                    if (!modulePackage.Payloads.TryGetValue(operation.Target.Payload, out var payload))
                    {
                        throw new InvalidOperationException(
                            $"Resolved compatibility payload is missing: {operation.Module.ModuleId}/{operation.Target.Payload}.");
                    }

                    var inputHash = desiredExists ? Sha256(desiredBytes) : "<missing>";
                    if (operation.Target.Operation.Equals("copy-file-v1", StringComparison.Ordinal))
                    {
                        if (desiredExists
                            && operation.Target.ResultSha256?.Equals(inputHash, StringComparison.OrdinalIgnoreCase) == true)
                        {
                            desiredBytes = payload.Bytes;
                            log.Add($"[MODULE] {operation.Module.ModuleId}: {relativePath} already contains the declared file payload.");
                            continue;
                        }

                        if (desiredExists
                            && operation.Target.SourceSha256.Count > 0
                            && !operation.Target.SourceSha256.Contains(inputHash, StringComparer.OrdinalIgnoreCase))
                        {
                            return Task.FromResult(Blocked(descriptor, manifest, action, aircraftRoot,
                                $"Module {operation.Module.DisplayName} does not support the current file at {relativePath}.",
                                [.. log, $"[BLOCKED] {operation.Module.ModuleId}/{relativePath} input SHA-256 {inputHash} is unsupported."]));
                        }

                        desiredBytes = payload.Bytes;
                        desiredExists = true;
                        var copiedHash = Sha256(desiredBytes);
                        if (!copiedHash.Equals(operation.Target.ResultSha256, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException(
                                $"Copied payload hash mismatch for {operation.Module.ModuleId}/{relativePath}.");
                        }

                        log.Add($"[MODULE] {operation.Module.ModuleId}: {relativePath} {inputHash} -> {copiedHash} (copy-file-v1).");
                        continue;
                    }

                    if (!desiredExists)
                    {
                        return Task.FromResult(Blocked(descriptor, manifest, action, aircraftRoot,
                            $"Module {operation.Module.DisplayName} requires an existing target: {relativePath}.", log));
                    }

                    var handler = _handlers.GetRequired(operation.Target.Operation);
                    var isInstalled = operation.Target.ResultSha256?.Equals(inputHash, StringComparison.OrdinalIgnoreCase) ?? false;
                    if (isInstalled)
                    {
                        log.Add($"[MODULE] {operation.Module.ModuleId}: {relativePath} already contains this operation.");
                        continue;
                    }

                    if (operation.Target.SourceSha256.Count > 0
                        && !operation.Target.SourceSha256.Contains(inputHash, StringComparer.OrdinalIgnoreCase))
                    {
                        return Task.FromResult(Blocked(descriptor, manifest, action, aircraftRoot,
                            $"Module {operation.Module.DisplayName} does not support the current pipeline state of {relativePath}.",
                            [.. log, $"[BLOCKED] {operation.Module.ModuleId}/{relativePath} input SHA-256 {inputHash} is unsupported."]));
                    }

                    if (operation.Target.SourceSha256.Count == 0 && !handler.SupportsStructuralSourceValidation)
                    {
                        return Task.FromResult(Blocked(descriptor, manifest, action, aircraftRoot,
                            $"Patch operation {operation.Target.Operation} requires sourceSha256 for {relativePath}.", log));
                    }

                    try
                    {
                        desiredBytes = handler.Apply(
                            desiredBytes,
                            payload.Json?.RootElement
                                ?? throw new InvalidOperationException(
                                    $"Compatibility patch payload is not JSON: {operation.Module.ModuleId}/{operation.Target.Payload}."));
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Task.FromResult(Blocked(descriptor, manifest, action, aircraftRoot,
                            $"Module {operation.Module.DisplayName} is structurally incompatible with {relativePath}: {ex.Message}",
                            [.. log, $"[BLOCKED] {operation.Module.ModuleId}/{relativePath}: {ex.Message}"]));
                    }

                    var resultHash = Sha256(desiredBytes);
                    if (operation.Target.ResultSha256 is not null
                        && !resultHash.Equals(operation.Target.ResultSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"Generated result hash mismatch for {operation.Module.ModuleId}/{relativePath}.");
                    }

                    log.Add($"[MODULE] {operation.Module.ModuleId}: {relativePath} {inputHash} -> {resultHash}.");
                }
            }

            var differs = desiredExists != currentExists
                || (desiredExists && !desiredBytes.AsSpan().SequenceEqual(currentBytes));
            if (differs)
            {
                mutations.Add(desiredExists
                    ? ContentPatchMutation.Write(
                        relativePath,
                        desiredBytes,
                        selectedOperations.ContainsKey(relativePath)
                            ? "Applied compatibility module pipeline"
                            : "Removed disabled compatibility modules")
                    : ContentPatchMutation.Delete(relativePath, "Removed file created by disabled compatibility module"));
            }
            else if (componentState is not null && selectedOperations.ContainsKey(relativePath))
            {
                mutations.Add(ContentPatchMutation.Write(relativePath, desiredBytes, "Retained verified compatibility target"));
            }
        }

        var status = mutations.Count == 0
            ? $"{descriptor.DisplayName} {manifest.PackageVersion} already matches the selected modules."
            : $"{descriptor.DisplayName} {manifest.PackageVersion} is ready with {selectedIds.Length} selected module(s).";
        return Task.FromResult(new ContentPatchPlan(
            descriptor,
            manifest.PackageVersion,
            action,
            aircraftRoot,
            mutations,
            log,
            IsSafe: true,
            status)
        {
            EnabledModules = selectedIds,
            OwnedRelativePaths = selectedOperations.Keys.ToHashSet(StringComparer.Ordinal)
        });
    }

    public static IReadOnlyList<string> DefaultSelection(CompatibilityPackageManifest manifest) =>
        ResolveSelection(
            manifest,
            manifest.Modules
            .Where(module => module.Policy is CompatibilityModulePolicy.Required || module.DefaultEnabled)
            .OrderBy(module => module.InstallationOrder)
            .Select(module => module.ModuleId)
            .ToArray());

    public static IReadOnlyList<string> ResolveSelection(
        CompatibilityPackageManifest manifest,
        IReadOnlyCollection<string> selectedModuleIds)
    {
        if (!TryResolveSelection(manifest, selectedModuleIds, out var modules, out var error))
        {
            throw new InvalidOperationException(error);
        }

        return modules.Select(module => module.ModuleId).ToArray();
    }

    public static ContentPatchDescriptor DescriptorFor(CompatibilityPackageManifest manifest) =>
        new(
            manifest.PackageId,
            manifest.AircraftFamily + " compatibility package",
            manifest.RepositoryUrl,
            new ContentPatchLifecyclePolicy(
                ContentPatchActivation.Managed,
                new HashSet<ContentPatchTrigger> { ContentPatchTrigger.Manual, ContentPatchTrigger.AfterAircraftUpdate }),
            manifest.RestartRequired);

    private static bool TryResolveSelection(
        CompatibilityPackageManifest manifest,
        IReadOnlyCollection<string> selectedModuleIds,
        out IReadOnlyList<CompatibilityPackageModule> modules,
        out string error)
    {
        var byId = manifest.Modules.ToDictionary(module => module.ModuleId, StringComparer.Ordinal);
        var selected = selectedModuleIds.ToHashSet(StringComparer.Ordinal);
        var unknown = selected.Where(id => !byId.ContainsKey(id)).ToArray();
        if (unknown.Length > 0)
        {
            modules = [];
            error = $"Unknown compatibility modules were selected: {string.Join(", ", unknown)}.";
            return false;
        }

        foreach (var required in manifest.Modules.Where(module => module.Policy is CompatibilityModulePolicy.Required))
        {
            selected.Add(required.ModuleId);
        }

        var pending = new Queue<string>(selected);
        while (pending.TryDequeue(out var id))
        {
            foreach (var dependency in byId[id].Requires)
            {
                if (selected.Add(dependency))
                {
                    pending.Enqueue(dependency);
                }
            }
        }

        foreach (var id in selected)
        {
            var conflict = byId[id].ConflictsWith.FirstOrDefault(selected.Contains);
            if (conflict is not null)
            {
                modules = [];
                error = $"Compatibility modules {id} and {conflict} conflict and cannot be installed together.";
                return false;
            }
        }

        modules = selected.Select(id => byId[id])
            .OrderBy(module => module.InstallationOrder)
            .ToArray();
        error = "";
        return true;
    }

    private static ContentPatchPlan BuildUninstallPlan(
        ContentPatchDescriptor descriptor,
        CompatibilityPackageManifest manifest,
        string aircraftRoot,
        ContentComponentState? state,
        List<string> log)
    {
        if (state is null || state.Files.Count == 0)
        {
            return Blocked(descriptor, manifest, ContentPatchAction.Uninstall, aircraftRoot,
                "No toolkit-owned compatibility package state is available for a safe uninstall.", log);
        }

        var mutations = new List<ContentPatchMutation>();
        foreach (var file in state.Files)
        {
            var targetPath = ContentPatchPathSafety.ResolveTarget(aircraftRoot, file.RelativePath, "Compatibility uninstall target");
            if (!File.Exists(targetPath)
                || !Sha256(File.ReadAllBytes(targetPath)).Equals(file.InstalledSha256, StringComparison.OrdinalIgnoreCase))
            {
                return Blocked(descriptor, manifest, ContentPatchAction.Uninstall, aircraftRoot,
                    $"Installed compatibility target changed after installation: {file.RelativePath}.", log);
            }

            if (!file.OriginalExisted)
            {
                mutations.Add(ContentPatchMutation.Delete(file.RelativePath, "Removed file created by compatibility package"));
                continue;
            }

            if (string.IsNullOrWhiteSpace(file.BackupPath)
                || !File.Exists(file.BackupPath))
            {
                return Blocked(descriptor, manifest, ContentPatchAction.Uninstall, aircraftRoot,
                    $"Original compatibility backup is missing for {file.RelativePath}.", log);
            }

            var bytes = File.ReadAllBytes(file.BackupPath);
            if (bytes.LongLength != file.OriginalSizeBytes
                || !Sha256(bytes).Equals(file.OriginalSha256, StringComparison.OrdinalIgnoreCase))
            {
                return Blocked(descriptor, manifest, ContentPatchAction.Uninstall, aircraftRoot,
                    $"Original compatibility backup failed validation for {file.RelativePath}.", log);
            }

            mutations.Add(ContentPatchMutation.Write(file.RelativePath, bytes, "Restored pre-package aircraft file"));
        }

        return new ContentPatchPlan(
            descriptor,
            manifest.PackageVersion,
            ContentPatchAction.Uninstall,
            aircraftRoot,
            mutations,
            log,
            IsSafe: true,
            $"{descriptor.DisplayName} will be uninstalled and original files restored.");
    }

    private static ContentPatchPlan Blocked(
        ContentPatchDescriptor descriptor,
        CompatibilityPackageManifest manifest,
        ContentPatchAction action,
        string aircraftRoot,
        string message,
        IReadOnlyList<string> log) =>
        ContentPatchPlan.Blocked(descriptor, manifest.PackageVersion, action, aircraftRoot, message, log);

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static bool SupportsUpstreamRelease(
        IReadOnlyCollection<string> supportedReleases,
        AircraftVariantViewAnalysis variant)
    {
        var supported = supportedReleases.Select(NormalizeRelease).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new[] { variant.LocalVersion, variant.SourceVersion, variant.SourceRef }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => NormalizeRelease(value!))
            .Any(supported.Contains);
    }

    private static string NormalizeRelease(string value) =>
        new(value.Trim().TrimStart('v', 'V').ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());

    private sealed record ModuleTarget(CompatibilityPackageModule Module, DeclarativePatchTarget Target);
}
