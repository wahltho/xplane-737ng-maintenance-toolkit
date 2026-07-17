using System.Security.Cryptography;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Analysis;
using LevelUp.NavTableUpdater.Core.Manifest;
using LevelUp.NavTableUpdater.Core.Platform;
using LevelUp.NavTableUpdater.Core.State;
using LevelUp.NavTableUpdater.Core.Transactions;

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
    private readonly ToolStateStore _stateStore;
    private readonly IPackagePayloadSource _payloadSource;
    private readonly AircraftInstallAnalyzer _analyzer = new();
    private readonly Func<bool> _isXPlaneRunning;

    public VnavContentOperation(
        ToolStateStore stateStore,
        IPackagePayloadSource payloadSource,
        Func<bool>? isXPlaneRunning = null)
    {
        _stateStore = stateStore;
        _payloadSource = payloadSource;
        _isXPlaneRunning = isXPlaneRunning ?? XPlaneProcessDetector.IsXPlaneRunning;
    }

    public async Task<MaintenanceOperationResult> RunAsync(
        VnavContentAction action,
        AircraftVariantViewAnalysis variant,
        PackageManifest manifest,
        CancellationToken cancellationToken = default)
    {
        var log = new List<string>
        {
            $"[START] VNAV {action} for {variant.DisplayName}",
            $"[PACKAGE] {manifest.PackageId} {manifest.PackageVersion} ({manifest.ReleaseTag})"
        };

        if (_isXPlaneRunning())
        {
            log.Add("[BLOCKED] X-Plane is running.");
            return MaintenanceOperationResult.Blocked("X-Plane is running. Close X-Plane before changing aircraft files.", log);
        }

        var analysis = _analyzer.Analyze(Path.GetDirectoryName(variant.AcfPath) ?? "", manifest);
        if (action is VnavContentAction.Uninstall)
        {
            return Uninstall(variant, manifest, analysis, log);
        }

        if (!analysis.IsSafeToPatch)
        {
            log.Add($"[BLOCKED] Target state is not safe to patch: {analysis.StateLabel}.");
            return MaintenanceOperationResult.Blocked($"Target state is not safe to patch: {analysis.StateLabel}.", log);
        }

        if (analysis.State is InstallState.CorrectlyInstalled && action is not VnavContentAction.Repair)
        {
            RecordContentState(variant, manifest, "VnavContentNoChange", backups: []);
            log.Add("[NO-CHANGE] VNAV content is already installed and current.");
            return MaintenanceOperationResult.NoChange("VNAV content is already installed and current.", log);
        }

        var payloads = await _payloadSource.GetPayloadsAsync(manifest, cancellationToken).ConfigureAwait(false);
        log.Add($"[PAYLOAD] Resolved and verified {payloads.Count} payload file(s).");

        var createdUtc = DateTimeOffset.UtcNow;
        var backupRecords = new List<BackupRecord>();
        var rollbackActions = new Stack<Action>();

        try
        {
            var targetScriptBackupPath = _stateStore.CreateBackupPath(variant, analysis.TargetScriptPath, createdUtc);
            var patchSummary = VnavLuaPatchTransaction.Apply(analysis.TargetScriptPath, manifest, payloads, targetScriptBackupPath);
            rollbackActions.Push(() => File.Copy(targetScriptBackupPath, analysis.TargetScriptPath, overwrite: true));
            backupRecords.Add(BuildBackupRecord("VnavContentPatch", analysis.TargetScriptPath, targetScriptBackupPath, createdUtc, variant, manifest));
            log.Add($"[BACKUP] {targetScriptBackupPath}");
            log.Add($"[PATCH] inserted={patchSummary.InsertedBlocks}, replaced={patchSummary.ReplacedBlocks}, migratedLegacy={patchSummary.MigratedLegacyBlocks}.");

            foreach (var record in CopyPayloads(variant, manifest, payloads, analysis.TargetScriptPath, createdUtc, rollbackActions, log))
            {
                backupRecords.Add(record);
            }

            RecordContentState(variant, manifest, $"VnavContent{action}", backupRecords);
        }
        catch
        {
            RollBack(rollbackActions, log);
            throw;
        }

        log.Add("[OK] VNAV content transaction completed.");
        return MaintenanceOperationResult.Applied(
            $"VNAV {action} completed for {manifest.PackageId} {manifest.PackageVersion}.",
            backupRecords.Select(record => record.BackupPath).ToArray(),
            log);
    }

    private MaintenanceOperationResult Uninstall(
        AircraftVariantViewAnalysis variant,
        PackageManifest manifest,
        AircraftAnalysisResult analysis,
        List<string> log)
    {
        if (!analysis.TargetScriptExists)
        {
            log.Add("[NO-CHANGE] Target script is missing.");
            return MaintenanceOperationResult.NoChange("VNAV content is not installed because the target script is missing.", log);
        }

        if (!VnavLuaPatchTransaction.HasMarkedBlocks(analysis.TargetScriptPath, manifest))
        {
            log.Add("[NO-CHANGE] No manifest-owned VNAV markers were found.");
            return MaintenanceOperationResult.NoChange("No manifest-owned VNAV markers were found.", log);
        }

        if (analysis.State is InstallState.PartiallyInstalled
            or InstallState.UnknownThirdPartyModification)
        {
            log.Add($"[BLOCKED] Target state is not safe to uninstall automatically: {analysis.StateLabel}.");
            return MaintenanceOperationResult.Blocked($"Target state is not safe to uninstall automatically: {analysis.StateLabel}.", log);
        }

        var createdUtc = DateTimeOffset.UtcNow;
        var backupRecords = new List<BackupRecord>();
        var rollbackActions = new Stack<Action>();

        try
        {
            var targetScriptBackupPath = _stateStore.CreateBackupPath(variant, analysis.TargetScriptPath, createdUtc);
            var patchSummary = VnavLuaPatchTransaction.Uninstall(analysis.TargetScriptPath, manifest, targetScriptBackupPath);
            rollbackActions.Push(() => File.Copy(targetScriptBackupPath, analysis.TargetScriptPath, overwrite: true));
            backupRecords.Add(BuildBackupRecord("VnavContentUninstall", analysis.TargetScriptPath, targetScriptBackupPath, createdUtc, variant, manifest));
            log.Add($"[BACKUP] {targetScriptBackupPath}");
            log.Add($"[PATCH] removed={patchSummary.RemovedBlocks}.");

            foreach (var record in RemoveMatchingPayloads(variant, manifest, analysis.TargetScriptPath, createdUtc, rollbackActions, log))
            {
                backupRecords.Add(record);
            }

            _stateStore.UpdateTarget(variant, target =>
            {
                target.InstalledContentPackageId = null;
                target.InstalledContentPackageVersion = null;
                target.LastContentOperationUtc = DateTimeOffset.UtcNow;
                target.LastOperation = "VnavContentUninstall";
                target.Backups.AddRange(backupRecords);
            });
        }
        catch
        {
            RollBack(rollbackActions, log);
            throw;
        }

        log.Add("[OK] VNAV content uninstall completed.");
        return MaintenanceOperationResult.Applied(
            $"VNAV Uninstall completed for {manifest.PackageId}.",
            backupRecords.Select(record => record.BackupPath).ToArray(),
            log);
    }

    private IEnumerable<BackupRecord> CopyPayloads(
        AircraftVariantViewAnalysis variant,
        PackageManifest manifest,
        IReadOnlyDictionary<string, PackagePayload> payloads,
        string targetScriptPath,
        DateTimeOffset createdUtc,
        Stack<Action> rollbackActions,
        ICollection<string> log)
    {
        var scriptFolder = Path.GetDirectoryName(targetScriptPath)
            ?? throw new InvalidOperationException("Target script path has no parent directory.");

        foreach (var payloadDefinition in manifest.Payloads)
        {
            var payload = payloads[payloadDefinition.FileName];
            var targetPath = Path.Combine(scriptFolder, payload.FileName);
            if (File.Exists(targetPath) && BytesEqual(File.ReadAllBytes(targetPath), payload.Bytes))
            {
                log.Add($"[PAYLOAD] {payload.FileName} already current.");
                continue;
            }

            string? backupPath = null;
            if (File.Exists(targetPath))
            {
                backupPath = _stateStore.CreateBackupPath(variant, targetPath, createdUtc);
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                File.Copy(targetPath, backupPath, overwrite: false);
                rollbackActions.Push(() => File.Copy(backupPath, targetPath, overwrite: true));
                log.Add($"[BACKUP] {backupPath}");
            }
            else
            {
                rollbackActions.Push(() =>
                {
                    if (File.Exists(targetPath))
                    {
                        File.Delete(targetPath);
                    }
                });
            }

            File.WriteAllBytes(targetPath, payload.Bytes);
            PackagePayloadValidator.ValidatePayload(payloadDefinition, File.ReadAllBytes(targetPath), targetPath);
            log.Add($"[PAYLOAD] Wrote {payload.FileName} from {payload.Source}.");

            if (backupPath is not null)
            {
                yield return BuildBackupRecord("VnavContentPayload", targetPath, backupPath, createdUtc, variant, manifest);
            }
        }
    }

    private IEnumerable<BackupRecord> RemoveMatchingPayloads(
        AircraftVariantViewAnalysis variant,
        PackageManifest manifest,
        string targetScriptPath,
        DateTimeOffset createdUtc,
        Stack<Action> rollbackActions,
        ICollection<string> log)
    {
        var scriptFolder = Path.GetDirectoryName(targetScriptPath)
            ?? throw new InvalidOperationException("Target script path has no parent directory.");

        foreach (var payload in manifest.Payloads)
        {
            var targetPath = Path.Combine(scriptFolder, payload.FileName);
            if (!File.Exists(targetPath))
            {
                continue;
            }

            var bytes = File.ReadAllBytes(targetPath);
            if (!IsManifestPayload(payload, bytes))
            {
                log.Add($"[PAYLOAD] Left changed payload in place: {payload.FileName}.");
                continue;
            }

            var backupPath = _stateStore.CreateBackupPath(variant, targetPath, createdUtc);
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            File.Copy(targetPath, backupPath, overwrite: false);
            File.Delete(targetPath);
            rollbackActions.Push(() => File.Copy(backupPath, targetPath, overwrite: true));
            log.Add($"[PAYLOAD] Removed manifest-owned payload {payload.FileName}.");
            yield return BuildBackupRecord("VnavContentUninstallPayload", targetPath, backupPath, createdUtc, variant, manifest);
        }
    }

    private void RecordContentState(
        AircraftVariantViewAnalysis variant,
        PackageManifest manifest,
        string operation,
        IReadOnlyList<BackupRecord> backups)
    {
        _stateStore.UpdateTarget(variant, target =>
        {
            target.InstalledContentPackageId = manifest.PackageId;
            target.InstalledContentPackageVersion = manifest.PackageVersion;
            target.LastContentOperationUtc = DateTimeOffset.UtcNow;
            target.LastOperation = operation;
            target.Backups.AddRange(backups);
        });
    }

    private static BackupRecord BuildBackupRecord(
        string operation,
        string sourcePath,
        string backupPath,
        DateTimeOffset createdUtc,
        AircraftVariantViewAnalysis variant,
        PackageManifest manifest) =>
        new()
        {
            Operation = operation,
            SourcePath = sourcePath,
            BackupPath = backupPath,
            CreatedUtc = createdUtc,
            CgYFeet = variant.CurrentCgYFeet,
            CgZFeet = variant.CurrentCgZFeet,
            PackageId = manifest.PackageId,
            PackageVersion = manifest.PackageVersion
        };

    private static void RollBack(Stack<Action> rollbackActions, ICollection<string> log)
    {
        while (rollbackActions.TryPop(out var rollback))
        {
            rollback();
            log.Add("[ROLLBACK] Restored a file changed by the failed transaction.");
        }
    }

    private static bool IsManifestPayload(PayloadFile payload, byte[] bytes)
    {
        if (bytes.LongLength != payload.Size)
        {
            return false;
        }

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return hash.Equals(payload.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static bool BytesEqual(byte[] left, byte[] right) =>
        left.AsSpan().SequenceEqual(right);
}
