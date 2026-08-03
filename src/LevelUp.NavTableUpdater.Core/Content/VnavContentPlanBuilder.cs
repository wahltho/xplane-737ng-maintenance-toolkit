using System.Security.Cryptography;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Analysis;
using LevelUp.NavTableUpdater.Core.Manifest;
using LevelUp.NavTableUpdater.Core.Transactions;

namespace LevelUp.NavTableUpdater.Core.Content;

public sealed record VnavContentPackage(
    PackageManifest Manifest,
    IReadOnlyDictionary<string, PackagePayload> Payloads);

public sealed class VnavContentPlanBuilder : IContentPatchPlanBuilder<VnavContentPackage>
{
    private readonly AircraftInstallAnalyzer _analyzer = new();

    public Task<ContentPatchPlan> BuildAsync(
        ContentPatchAction action,
        AircraftVariantViewAnalysis variant,
        VnavContentPackage package,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manifest = package.Manifest;
        var descriptor = ContentPatchCatalog.Vnav(manifest.PackageId, manifest.RepositoryUrl);
        var aircraftRoot = Path.GetDirectoryName(variant.AcfPath) ?? "";
        var analysis = _analyzer.Analyze(aircraftRoot, manifest);
        var log = new List<string>
        {
            $"[START] VNAV {action} for {variant.DisplayName}",
            $"[PACKAGE] {manifest.PackageId} {manifest.PackageVersion} ({manifest.ReleaseTag})"
        };

        if (action is ContentPatchAction.Uninstall)
        {
            return Task.FromResult(BuildUninstall(descriptor, manifest, analysis, aircraftRoot, log));
        }

        if (!analysis.IsSafeToPatch)
        {
            return Task.FromResult(ContentPatchPlan.Blocked(
                descriptor,
                manifest.PackageVersion,
                action,
                aircraftRoot,
                $"Target state is not safe to patch: {analysis.StateLabel}.",
                log));
        }

        if (analysis.State is InstallState.CorrectlyInstalled && action is not ContentPatchAction.Repair)
        {
            log.Add("[NO-CHANGE] VNAV content is already installed and current.");
            return Task.FromResult(new ContentPatchPlan(
                descriptor,
                manifest.PackageVersion,
                action,
                aircraftRoot,
                [],
                log,
                IsSafe: true,
                "VNAV content is already installed and current."));
        }

        var prepared = VnavLuaPatchTransaction.PrepareApply(
            File.ReadAllBytes(analysis.TargetScriptPath),
            manifest,
            package.Payloads);
        var mutations = new List<ContentPatchMutation>
        {
            ContentPatchMutation.Write(manifest.TargetRelativePath, prepared.Bytes, "Applied manifest-owned VNAV hook blocks")
        };
        var scriptFolder = Path.GetDirectoryName(manifest.TargetRelativePath)?.Replace('\\', '/') ?? "";
        foreach (var payload in manifest.Payloads)
        {
            if (!package.Payloads.TryGetValue(payload.FileName, out var resolved))
            {
                throw new InvalidOperationException($"Resolved payload '{payload.FileName}' is missing.");
            }

            PackagePayloadValidator.ValidatePayload(payload, resolved.Bytes, resolved.Source);
            mutations.Add(ContentPatchMutation.Write(
                CombineRelative(scriptFolder, payload.FileName),
                resolved.Bytes,
                $"Installed verified VNAV payload {payload.FileName}"));
        }

        log.Add($"[PLAN] inserted={prepared.Summary.InsertedBlocks}, replaced={prepared.Summary.ReplacedBlocks}, migratedLegacy={prepared.Summary.MigratedLegacyBlocks}.");
        return Task.FromResult(new ContentPatchPlan(
            descriptor,
            manifest.PackageVersion,
            action,
            aircraftRoot,
            mutations,
            log,
            IsSafe: true,
            $"VNAV {action} completed for {manifest.PackageId} {manifest.PackageVersion}."));
    }

    private static ContentPatchPlan BuildUninstall(
        ContentPatchDescriptor descriptor,
        PackageManifest manifest,
        AircraftAnalysisResult analysis,
        string aircraftRoot,
        List<string> log)
    {
        if (!analysis.TargetScriptExists || !VnavLuaPatchTransaction.HasMarkedBlocks(analysis.TargetScriptPath, manifest))
        {
            log.Add("[NO-CHANGE] No manifest-owned VNAV markers were found.");
            return new ContentPatchPlan(
                descriptor,
                manifest.PackageVersion,
                ContentPatchAction.Uninstall,
                aircraftRoot,
                [],
                log,
                IsSafe: true,
                "No manifest-owned VNAV markers were found.");
        }

        if (analysis.State is InstallState.PartiallyInstalled or InstallState.UnknownThirdPartyModification)
        {
            return ContentPatchPlan.Blocked(
                descriptor,
                manifest.PackageVersion,
                ContentPatchAction.Uninstall,
                aircraftRoot,
                $"Target state is not safe to uninstall automatically: {analysis.StateLabel}.",
                log);
        }

        var prepared = VnavLuaPatchTransaction.PrepareUninstall(File.ReadAllBytes(analysis.TargetScriptPath), manifest);
        var mutations = new List<ContentPatchMutation>
        {
            ContentPatchMutation.Write(manifest.TargetRelativePath, prepared.Bytes, "Removed manifest-owned VNAV hook blocks")
        };
        var scriptFolder = Path.GetDirectoryName(manifest.TargetRelativePath)?.Replace('\\', '/') ?? "";
        foreach (var payload in manifest.Payloads)
        {
            var relativePath = CombineRelative(scriptFolder, payload.FileName);
            var targetPath = Path.Combine(aircraftRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(targetPath) && IsManifestPayload(payload, File.ReadAllBytes(targetPath)))
            {
                mutations.Add(ContentPatchMutation.Delete(relativePath, $"Removed manifest-owned VNAV payload {payload.FileName}"));
            }
            else if (File.Exists(targetPath))
            {
                log.Add($"[PAYLOAD] Left changed payload in place: {payload.FileName}.");
            }
        }

        log.Add($"[PLAN] removed={prepared.Summary.RemovedBlocks}.");
        return new ContentPatchPlan(
            descriptor,
            manifest.PackageVersion,
            ContentPatchAction.Uninstall,
            aircraftRoot,
            mutations,
            log,
            IsSafe: true,
            $"VNAV Uninstall completed for {manifest.PackageId}.");
    }

    private static bool IsManifestPayload(PayloadFile payload, byte[] bytes)
    {
        if (bytes.LongLength != payload.Size)
        {
            return false;
        }

        return Convert.ToHexString(SHA256.HashData(bytes)).Equals(payload.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static string CombineRelative(string directory, string fileName) =>
        string.IsNullOrWhiteSpace(directory) ? fileName : $"{directory.TrimEnd('/')}/{fileName}";
}
