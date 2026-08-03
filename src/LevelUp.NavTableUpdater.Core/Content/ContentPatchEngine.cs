using System.Security.Cryptography;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Platform;
using LevelUp.NavTableUpdater.Core.State;
using LevelUp.NavTableUpdater.Core.Transactions;

namespace LevelUp.NavTableUpdater.Core.Content;

public sealed class ContentPatchEngine
{
    private readonly ToolStateStore _stateStore;
    private readonly Func<bool> _isXPlaneRunning;

    public ContentPatchEngine(ToolStateStore stateStore, Func<bool>? isXPlaneRunning = null)
    {
        _stateStore = stateStore;
        _isXPlaneRunning = isXPlaneRunning ?? XPlaneProcessDetector.IsXPlaneRunning;
    }

    public MaintenanceOperationResult Execute(ContentPatchPlan plan, AircraftVariantViewAnalysis variant)
    {
        var log = plan.Log.ToList();
        log.Add($"[ENGINE] {plan.Descriptor.ComponentId} {plan.PackageVersion} {plan.Action}.");
        if (!plan.IsSafe)
        {
            log.Add($"[BLOCKED] {plan.StatusMessage}");
            return MaintenanceOperationResult.Blocked(plan.StatusMessage, log);
        }

        if (_isXPlaneRunning())
        {
            log.Add("[BLOCKED] X-Plane is running.");
            return MaintenanceOperationResult.Blocked("X-Plane is running. Close X-Plane before changing aircraft files.", log);
        }

        var aircraftRoot = Path.GetFullPath(plan.AircraftRoot);
        var mutations = NormalizeMutations(aircraftRoot, plan.Mutations);
        if (mutations.Count == 0)
        {
            log.Add("[NO-CHANGE] The patch plan contains no file changes.");
            return MaintenanceOperationResult.NoChange(plan.StatusMessage, log);
        }

        var changedMutations = mutations.Where(mutation => !MutationIsAlreadyApplied(mutation)).ToArray();
        if (changedMutations.Length == 0)
        {
            RecordState(plan, variant, mutations, backups: [], changed: false);
            log.Add("[NO-CHANGE] Every planned target already has the requested state.");
            return MaintenanceOperationResult.NoChange(plan.StatusMessage, log);
        }

        var createdUtc = DateTimeOffset.UtcNow;
        var backupRecords = new List<BackupRecord>();
        var rollback = new Stack<Action>();
        var originalStates = new Dictionary<string, OriginalFileState>(PathComparer());

        try
        {
            foreach (var mutation in changedMutations)
            {
                var original = CaptureOriginal(mutation.TargetPath);
                originalStates[mutation.RelativePath] = original;
                if (original.Existed)
                {
                    var backupPath = _stateStore.CreateProductBackupPath(
                        variant,
                        mutation.TargetPath,
                        createdUtc,
                        mutation.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                    File.Copy(mutation.TargetPath, backupPath, overwrite: false);
                    original = original with { BackupPath = backupPath };
                    originalStates[mutation.RelativePath] = original;
                    rollback.Push(() => File.Copy(backupPath, mutation.TargetPath, overwrite: true));
                    log.Add($"[BACKUP] {mutation.RelativePath} -> {backupPath}");
                }
                else
                {
                    rollback.Push(() =>
                    {
                        if (File.Exists(mutation.TargetPath))
                        {
                            File.Delete(mutation.TargetPath);
                        }
                    });
                }
            }

            foreach (var mutation in changedMutations)
            {
                switch (mutation.Kind)
                {
                    case ContentPatchMutationKind.Write:
                        AtomicFileMutation.Write(
                            mutation.TargetPath,
                            mutation.DesiredBytes ?? throw new InvalidOperationException($"{mutation.RelativePath}: write has no bytes."));
                        break;
                    case ContentPatchMutationKind.Delete:
                        if (File.Exists(mutation.TargetPath))
                        {
                            File.Delete(mutation.TargetPath);
                        }
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported mutation kind: {mutation.Kind}.");
                }

                ValidateMutation(mutation);
                log.Add($"[PATCH] {mutation.Description}: {mutation.RelativePath}");
            }

            backupRecords.AddRange(BuildBackupRecords(plan, variant, mutations, originalStates, createdUtc));
            RecordState(plan, variant, mutations, backupRecords, changed: true, originalStates);
        }
        catch
        {
            while (rollback.TryPop(out var restore))
            {
                restore();
                log.Add("[ROLLBACK] Restored a file changed by the failed content transaction.");
            }

            throw;
        }

        log.Add("[OK] Generic content patch transaction completed.");
        return MaintenanceOperationResult.Applied(
            plan.StatusMessage,
            backupRecords.Select(record => record.BackupPath).Where(path => path.Length > 0).ToArray(),
            log);
    }

    public MaintenanceOperationResult Restore(
        ContentPatchDescriptor descriptor,
        AircraftVariantViewAnalysis variant,
        string stateOperation = "ContentPatchRestore")
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(variant);
        var aircraftRoot = Path.GetFullPath(Path.GetDirectoryName(variant.AcfPath) ?? "");
        var log = new List<string>
        {
            $"[START] Restore {descriptor.DisplayName} for {AircraftProductIdentity.FromVariant(variant).DisplayName}"
        };

        if (_isXPlaneRunning())
        {
            log.Add("[BLOCKED] X-Plane is running.");
            return MaintenanceOperationResult.Blocked(
                "X-Plane is running. Close X-Plane before restoring aircraft files.",
                log);
        }

        var component = _stateStore.TryGetContentInstallation(aircraftRoot)?.ContentComponents
            .GetValueOrDefault(descriptor.ComponentId);
        if (component is null || component.Files.Count == 0)
        {
            log.Add("[BLOCKED] No component restore state is recorded.");
            return MaintenanceOperationResult.Blocked(
                $"No restorable backup is recorded for {descriptor.DisplayName}.",
                log);
        }

        var files = new List<RestoreFile>(component.Files.Count);
        foreach (var file in component.Files)
        {
            var targetPath = ContentPatchPathSafety.ResolveTarget(aircraftRoot, file.RelativePath, "Restore target");
            if (!PathsEqual(targetPath, file.TargetPath))
            {
                log.Add($"[BLOCKED] Recorded target path does not match the current installation: {file.RelativePath}.");
                return MaintenanceOperationResult.Blocked(
                    $"Restore state is inconsistent for {file.RelativePath}.",
                    log);
            }

            var installed = CaptureOriginal(targetPath);
            if (!MatchesInstalledState(installed, file))
            {
                log.Add($"[BLOCKED] Package-owned file changed after installation: {file.RelativePath}.");
                return MaintenanceOperationResult.Blocked(
                    $"Restore would overwrite a later change to {file.RelativePath}.",
                    log);
            }

            byte[]? originalBytes = null;
            if (file.OriginalExisted)
            {
                if (string.IsNullOrWhiteSpace(file.BackupPath)
                    || !File.Exists(file.BackupPath))
                {
                    log.Add($"[BLOCKED] Original backup is missing: {file.RelativePath}.");
                    return MaintenanceOperationResult.Blocked(
                        $"Original backup is missing for {file.RelativePath}.",
                        log);
                }

                originalBytes = File.ReadAllBytes(file.BackupPath);
                if (!MatchesBytes(originalBytes, file.OriginalSizeBytes, file.OriginalSha256))
                {
                    log.Add($"[BLOCKED] Original backup failed size/SHA-256 validation: {file.RelativePath}.");
                    return MaintenanceOperationResult.Blocked(
                        $"Original backup is damaged for {file.RelativePath}.",
                        log);
                }
            }

            files.Add(new RestoreFile(file, targetPath, installed, originalBytes));
        }

        var createdUtc = DateTimeOffset.UtcNow;
        var preRestoreBackups = new List<BackupRecord>();
        var rollback = new Stack<Action>();
        try
        {
            foreach (var file in files)
            {
                if (file.Installed.Existed)
                {
                    var backupPath = _stateStore.CreateProductBackupPath(
                        variant,
                        file.TargetPath,
                        createdUtc,
                        file.State.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                    File.Copy(file.TargetPath, backupPath, overwrite: false);
                    rollback.Push(() => File.Copy(backupPath, file.TargetPath, overwrite: true));
                    preRestoreBackups.Add(new BackupRecord
                    {
                        Operation = stateOperation + "PreImage",
                        SourcePath = file.TargetPath,
                        BackupPath = backupPath,
                        CreatedUtc = createdUtc,
                        CgYFeet = variant.CurrentCgYFeet,
                        CgZFeet = variant.CurrentCgZFeet,
                        PackageId = descriptor.ComponentId,
                        PackageVersion = component.PackageVersion,
                        SourceExisted = true,
                        SourceSizeBytes = file.Installed.SizeBytes,
                        SourceSha256 = file.Installed.Sha256,
                        WrittenSizeBytes = file.State.OriginalSizeBytes,
                        WrittenSha256 = file.State.OriginalSha256
                    });
                    log.Add($"[BACKUP] Pre-restore image: {file.State.RelativePath} -> {backupPath}");
                }
                else
                {
                    rollback.Push(() =>
                    {
                        if (File.Exists(file.TargetPath))
                        {
                            File.Delete(file.TargetPath);
                        }
                    });
                }
            }

            foreach (var file in files)
            {
                if (file.OriginalBytes is null)
                {
                    if (File.Exists(file.TargetPath))
                    {
                        File.Delete(file.TargetPath);
                    }
                }
                else
                {
                    AtomicFileMutation.Write(file.TargetPath, file.OriginalBytes);
                }

                var restored = CaptureOriginal(file.TargetPath);
                if (restored.Existed != file.State.OriginalExisted
                    || (restored.Existed
                        && (restored.SizeBytes != file.State.OriginalSizeBytes
                            || !string.Equals(restored.Sha256, file.State.OriginalSha256, StringComparison.OrdinalIgnoreCase))))
                {
                    throw new InvalidOperationException(
                        $"Restored file failed size/SHA-256 validation: {file.State.RelativePath}.");
                }

                log.Add($"[RESTORE] {file.State.RelativePath}");
            }

            if (descriptor.Lifecycle.Activation is ContentPatchActivation.Managed)
            {
                _stateStore.UpdateProductTarget(variant, target =>
                {
                    target.ContentComponents.Remove(descriptor.ComponentId);
                    if (string.Equals(target.InstalledContentPackageId, descriptor.ComponentId, StringComparison.Ordinal))
                    {
                        target.InstalledContentPackageId = null;
                        target.InstalledContentPackageVersion = null;
                    }

                    target.LastContentOperationUtc = DateTimeOffset.UtcNow;
                    target.LastOperation = stateOperation;
                    target.Backups.AddRange(preRestoreBackups);
                });
            }

            _stateStore.UpdateContentInstallation(aircraftRoot, installation =>
            {
                installation.ContentComponents.Remove(descriptor.ComponentId);
                installation.Backups.AddRange(preRestoreBackups);
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            while (rollback.TryPop(out var restore))
            {
                restore();
                log.Add("[ROLLBACK] Restored a file changed by the failed restore transaction.");
            }

            log.Add($"[FAILED] {ex.Message}");
            return MaintenanceOperationResult.Blocked(
                $"Restore failed and rollback completed: {ex.Message}",
                log);
        }

        log.Add("[OK] Content restore completed.");
        return MaintenanceOperationResult.Restored(
            $"Restored {descriptor.DisplayName} to its exact pre-installation state.",
            preRestoreBackups.Select(record => record.BackupPath).ToArray(),
            log);
    }

    private void RecordState(
        ContentPatchPlan plan,
        AircraftVariantViewAnalysis variant,
        IReadOnlyList<ResolvedMutation> mutations,
        IReadOnlyList<BackupRecord> backups,
        bool changed,
        IReadOnlyDictionary<string, OriginalFileState>? originals = null)
    {
        var previous = _stateStore.TryGetContentInstallation(plan.AircraftRoot)?.ContentComponents
            .GetValueOrDefault(plan.Descriptor.ComponentId);
        _stateStore.UpdateContentInstallation(plan.AircraftRoot, installation =>
        {
            installation.ContentComponents ??= new Dictionary<string, ContentComponentState>(StringComparer.Ordinal);
            if (plan.Action is ContentPatchAction.Uninstall)
            {
                installation.ContentComponents.Remove(plan.Descriptor.ComponentId);
            }
            else
            {
                var now = DateTimeOffset.UtcNow;
                var fileStates = mutations.Select(mutation => BuildFileState(mutation, previous, originals)).ToList();
                installation.ContentComponents[plan.Descriptor.ComponentId] = new ContentComponentState
                {
                    ComponentId = plan.Descriptor.ComponentId,
                    PackageVersion = plan.PackageVersion,
                    InstalledUtc = previous?.InstalledUtc ?? now,
                    LastOperationUtc = now,
                    LastOperation = $"ContentPatch{plan.Action}",
                    Files = fileStates
                };
            }

            installation.Backups.AddRange(backups);
        });

        if (plan.Descriptor.Lifecycle.Activation is not ContentPatchActivation.Managed)
        {
            return;
        }

        _stateStore.UpdateProductTarget(variant, target =>
        {
            if (plan.Action is ContentPatchAction.Uninstall)
            {
                target.ContentComponents.Remove(plan.Descriptor.ComponentId);
                if (string.Equals(target.InstalledContentPackageId, plan.Descriptor.ComponentId, StringComparison.Ordinal))
                {
                    target.InstalledContentPackageId = null;
                    target.InstalledContentPackageVersion = null;
                }
            }
            else
            {
                var installationState = _stateStore.TryGetContentInstallation(plan.AircraftRoot)?.ContentComponents
                    .GetValueOrDefault(plan.Descriptor.ComponentId);
                if (installationState is not null)
                {
                    target.ContentComponents[plan.Descriptor.ComponentId] = installationState;
                }

                target.InstalledContentPackageId = plan.Descriptor.ComponentId;
                target.InstalledContentPackageVersion = plan.PackageVersion;
            }

            target.LastContentOperationUtc = DateTimeOffset.UtcNow;
            target.LastOperation = changed ? $"ContentPatch{plan.Action}" : "ContentPatchNoChange";
            target.Backups.AddRange(backups);
        });
    }

    private static ContentComponentFileState BuildFileState(
        ResolvedMutation mutation,
        ContentComponentState? previous,
        IReadOnlyDictionary<string, OriginalFileState>? originals)
    {
        var previousFile = previous?.Files.FirstOrDefault(file =>
            string.Equals(file.RelativePath, mutation.RelativePath, StringComparison.Ordinal));
        OriginalFileState? original = null;
        if (originals is not null)
        {
            originals.TryGetValue(mutation.RelativePath, out original);
        }
        var installed = CaptureOriginal(mutation.TargetPath);
        return new ContentComponentFileState
        {
            RelativePath = mutation.RelativePath,
            TargetPath = mutation.TargetPath,
            BackupPath = previousFile?.BackupPath ?? original?.BackupPath ?? "",
            OriginalExisted = previousFile?.OriginalExisted ?? original?.Existed ?? installed.Existed,
            OriginalSizeBytes = previousFile?.OriginalSizeBytes ?? original?.SizeBytes ?? installed.SizeBytes,
            OriginalSha256 = previousFile?.OriginalSha256 ?? original?.Sha256 ?? installed.Sha256,
            InstalledSizeBytes = installed.SizeBytes,
            InstalledSha256 = installed.Sha256
        };
    }

    private static IReadOnlyList<BackupRecord> BuildBackupRecords(
        ContentPatchPlan plan,
        AircraftVariantViewAnalysis variant,
        IReadOnlyList<ResolvedMutation> mutations,
        IReadOnlyDictionary<string, OriginalFileState> originals,
        DateTimeOffset createdUtc) =>
        mutations
            .Where(mutation => originals.TryGetValue(mutation.RelativePath, out var original) && original.Existed)
            .Select(mutation =>
            {
                var original = originals[mutation.RelativePath];
                var written = mutation.Kind is ContentPatchMutationKind.Write
                    ? CaptureBytes(mutation.DesiredBytes ?? [])
                    : new OriginalFileState(false, null, null, "");
                return new BackupRecord
                {
                    Operation = $"ContentPatch{plan.Action}",
                    SourcePath = mutation.TargetPath,
                    BackupPath = original.BackupPath,
                    CreatedUtc = createdUtc,
                    CgYFeet = variant.CurrentCgYFeet,
                    CgZFeet = variant.CurrentCgZFeet,
                    PackageId = plan.Descriptor.ComponentId,
                    PackageVersion = plan.PackageVersion,
                    SourceExisted = true,
                    SourceSizeBytes = original.SizeBytes,
                    SourceSha256 = original.Sha256,
                    WrittenSizeBytes = written.SizeBytes,
                    WrittenSha256 = written.Sha256
                };
            })
            .ToArray();

    private static List<ResolvedMutation> NormalizeMutations(string aircraftRoot, IReadOnlyList<ContentPatchMutation> mutations)
    {
        var result = new List<ResolvedMutation>(mutations.Count);
        var seen = new HashSet<string>(PathComparer());
        foreach (var mutation in mutations)
        {
            var relative = NormalizeRelativePath(mutation.RelativePath);
            var target = ContentPatchPathSafety.ResolveTarget(aircraftRoot, relative, "Patch target");
            if (!seen.Add(target))
            {
                throw new InvalidOperationException($"Patch plan contains duplicate target: {relative}.");
            }

            if (mutation.Kind is ContentPatchMutationKind.Write && mutation.DesiredBytes is null)
            {
                throw new InvalidOperationException($"{relative}: write mutation has no desired content.");
            }

            result.Add(new ResolvedMutation(relative.Replace('\\', '/'), target, mutation.Kind, mutation.DesiredBytes, mutation.Description));
        }

        return result;
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException($"Unsafe patch target path: {relativePath}.");
        }

        var parts = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(part => part is "." or ".."))
        {
            throw new InvalidOperationException($"Unsafe patch target path: {relativePath}.");
        }

        return Path.Combine(parts);
    }

    private static bool MutationIsAlreadyApplied(ResolvedMutation mutation) =>
        mutation.Kind switch
        {
            ContentPatchMutationKind.Delete => !File.Exists(mutation.TargetPath),
            ContentPatchMutationKind.Write => File.Exists(mutation.TargetPath)
                && mutation.DesiredBytes is not null
                && File.ReadAllBytes(mutation.TargetPath).AsSpan().SequenceEqual(mutation.DesiredBytes),
            _ => false
        };

    private static void ValidateMutation(ResolvedMutation mutation)
    {
        if (mutation.Kind is ContentPatchMutationKind.Delete)
        {
            if (File.Exists(mutation.TargetPath))
            {
                throw new InvalidOperationException($"{mutation.RelativePath}: delete did not remove the target.");
            }

            return;
        }

        if (!File.Exists(mutation.TargetPath)
            || mutation.DesiredBytes is null
            || !File.ReadAllBytes(mutation.TargetPath).AsSpan().SequenceEqual(mutation.DesiredBytes))
        {
            throw new InvalidOperationException($"{mutation.RelativePath}: written target failed byte verification.");
        }
    }

    private static OriginalFileState CaptureOriginal(string path)
    {
        if (!File.Exists(path))
        {
            return new OriginalFileState(false, null, null, "");
        }

        var bytes = File.ReadAllBytes(path);
        return new OriginalFileState(
            true,
            bytes.LongLength,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            "");
    }

    private static OriginalFileState CaptureBytes(byte[] bytes) =>
        new(
            true,
            bytes.LongLength,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            "");

    private static bool MatchesInstalledState(OriginalFileState current, ContentComponentFileState expected)
    {
        var expectedExists = expected.InstalledSizeBytes.HasValue && !string.IsNullOrWhiteSpace(expected.InstalledSha256);
        return current.Existed == expectedExists
            && (!expectedExists
                || (current.SizeBytes == expected.InstalledSizeBytes
                    && string.Equals(current.Sha256, expected.InstalledSha256, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool MatchesBytes(byte[] bytes, long? expectedSize, string? expectedSha256) =>
        expectedSize.HasValue
        && !string.IsNullOrWhiteSpace(expectedSha256)
        && bytes.LongLength == expectedSize.Value
        && Convert.ToHexString(SHA256.HashData(bytes)).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase);

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    private static StringComparer PathComparer() =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private sealed record ResolvedMutation(
        string RelativePath,
        string TargetPath,
        ContentPatchMutationKind Kind,
        byte[]? DesiredBytes,
        string Description);

    private sealed record OriginalFileState(
        bool Existed,
        long? SizeBytes,
        string? Sha256,
        string BackupPath);

    private sealed record RestoreFile(
        ContentComponentFileState State,
        string TargetPath,
        OriginalFileState Installed,
        byte[]? OriginalBytes);
}
