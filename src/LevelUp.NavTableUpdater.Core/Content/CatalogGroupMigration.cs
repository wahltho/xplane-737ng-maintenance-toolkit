using System.Security.Cryptography;
using LevelUp.NavTableUpdater.Core.Manifest;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Content;

internal static class CatalogGroupMigration
{
    // Snapshots may overlap: older MTK versions recorded composite hashes on a
    // no-change refresh. Immutable, verified transaction records retain the missing edges.
    public static ContentComponentState? Prepare(string aircraftRoot, CompatibilityPackageManifest manifest,
        IReadOnlyDictionary<string, ContentComponentState>? installed,
        IReadOnlyList<BackupRecord>? journal = null,
        IReadOnlySet<string>? structurallyValidatedReplacements = null)
    {
        if (manifest.Sources.Count == 0 || installed is null) return null;
        var sourceIds = manifest.Sources.Select(s => s.PackageId).ToHashSet(StringComparer.Ordinal);
        var previous = installed.Where(p => sourceIds.Contains(p.Key)).Select(p => p.Value).ToArray();
        if (previous.Length == 0) return null;
        var targets = manifest.Modules.SelectMany(m => m.Targets).Select(t => t.RelativePath).ToHashSet(StringComparer.Ordinal);
        var result = new ContentComponentState { ComponentId = manifest.PackageId,
            InstalledUtc = previous.Min(p => p.InstalledUtc), RestoreAvailable = previous.All(p => p.RestoreAvailable) };
        foreach (var group in previous.SelectMany(p => p.Files.Select(f => (Owner: p, File: f)))
            .GroupBy(f => f.File.RelativePath, StringComparer.Ordinal))
        {
            if (!targets.Contains(group.Key)) throw new InvalidOperationException($"Catalog migration omits managed target {group.Key}.");
            var path = ContentPatchPathSafety.ResolveTarget(aircraftRoot, group.Key, "Catalog migration");
            if (!File.Exists(path))
            {
                if (!group.Any(x => !x.File.OriginalExisted))
                    throw new InvalidOperationException($"Migration target is missing: {group.Key}.");
                // A clean baseline also removes files originally created by a patch.
                result.Files.Add(new() { RelativePath = group.Key, TargetPath = path, OriginalExisted = false });
                continue;
            }
            var current = File.ReadAllBytes(path);
            var currentHash = Hash(current);
            var files = group.Select(x => x.File).ToArray();
            var initialMatches = files.Where(f => Equal(f.InstalledSha256, currentHash)).ToArray();
            if (initialMatches.Length == 0)
            {
                var recordedOriginal = files.FirstOrDefault(f => f.OriginalExisted
                    && f.OriginalSizeBytes == current.LongLength && Equal(f.OriginalSha256, currentHash));
                if (recordedOriginal is null)
                {
                    // A released aircraft installer recorded preimages but not output
                    // hashes. Recover only a single-owner structural target with a
                    // verified replacement preimage. The caller must still validate
                    // the complete selected patch pipeline against the current bytes.
                    if (files.Length == 1 && structurallyValidatedReplacements?.Contains(group.Key) == true
                        && (!files[0].OriginalExisted || BackupMatches(files[0])))
                    {
                        result.Files.Add(new() { RelativePath = group.Key, TargetPath = path,
                            OriginalExisted = true, OriginalSha256 = currentHash, OriginalSizeBytes = current.LongLength,
                            InstalledSha256 = currentHash, InstalledSizeBytes = current.LongLength });
                        continue;
                    }
                    throw ChainError(group.Key, currentHash, group.Select(x => x.Owner.ComponentId));
                }
                // Current bytes themselves prove this original. A missing/corrupt old
                // backup is replaced inside the engine transaction, never during planning.
                result.Files.Add(CopyOriginal(recordedOriginal, path, currentHash, current.LongLength,
                    BackupMatches(recordedOriginal) ? recordedOriginal.BackupPath : ""));
                continue;
            }

            var edges = new List<ContentComponentFileState>();
            foreach (var item in group)
            {
                var file = item.File;
                if (file.OriginalExisted && !BackupMatches(file))
                {
                    if (file.OriginalSizeBytes != current.LongLength || !Equal(file.OriginalSha256, currentHash))
                        throw new InvalidOperationException($"Original backup failed verification for {group.Key}; package={item.Owner.ComponentId}; expected={file.OriginalSha256}; backup={file.BackupPath}; current={currentHash}.");
                    // The current target can also supply an intermediate/no-change
                    // original. Do not replace any historical backup while planning.
                    edges.Add(CopyOriginal(file, path, file.InstalledSha256!, file.InstalledSizeBytes ?? 0, path));
                }
                else edges.Add(file);
                var records = (journal ?? []).Where(r => r.PackageId == item.Owner.ComponentId
                    && (r.Operation is "ContentPatchInstall" or "ContentPatchUpdate" or "ContentPatchRepair")
                    && !string.IsNullOrWhiteSpace(r.SourcePath) && Path.GetFullPath(r.SourcePath).Equals(path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    .ToArray();
                var anchor = records.Where(r => r.BackupPath == file.BackupPath
                    && Equal(r.SourceSha256, file.OriginalSha256)).Select(r => (DateTimeOffset?)r.CreatedUtc).Min();
                if (anchor is null) continue;
                foreach (var record in records.Where(r => r.CreatedUtc >= anchor.Value && !string.IsNullOrWhiteSpace(r.WrittenSha256)))
                {
                    var evidence = new ContentComponentFileState
                    {
                        BackupPath = record.BackupPath, OriginalExisted = record.SourceExisted,
                        OriginalSha256 = record.SourceSha256, OriginalSizeBytes = record.SourceSizeBytes,
                        InstalledSha256 = record.WrittenSha256, InstalledSizeBytes = record.WrittenSizeBytes
                    };
                    // Unusable historical journal entries supply no evidence. Current
                    // ownership backups above remain mandatory and are never bypassed.
                    if (!evidence.OriginalExisted || BackupMatches(evidence)) edges.Add(evidence);
                }
            }
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Visit(string hash)
            {
                if (visiting.Contains(hash)) throw ChainError(group.Key, currentHash, group.Select(x => x.Owner.ComponentId));
                if (!visited.Add(hash)) return;
                visiting.Add(hash);
                var predecessors = edges.Where(f => Equal(f.InstalledSha256, hash))
                    .Select(f => f.OriginalExisted ? f.OriginalSha256 ?? "" : "")
                    .Where(h => !Equal(h, hash)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (predecessors.Length == 0) roots.Add(hash);
                foreach (var predecessor in predecessors) Visit(predecessor);
                visiting.Remove(hash);
            }
            Visit(currentHash);
            if (roots.Count != 1 || files.Any(f => !visited.Contains(f.InstalledSha256 ?? "")
                || !visited.Contains(f.OriginalExisted ? f.OriginalSha256 ?? "" : "")))
                throw ChainError(group.Key, currentHash, group.Select(x => x.Owner.ComponentId));
            var root = roots.Single();
            var original = edges.FirstOrDefault(f => Equal(f.OriginalExisted ? f.OriginalSha256 : "", root));
            if (original is null) throw ChainError(group.Key, currentHash, group.Select(x => x.Owner.ComponentId));
            result.Files.Add(CopyOriginal(original, path, currentHash, current.LongLength, original.BackupPath == path ? "" : original.BackupPath));
            result.Files[^1].RelativePath = group.Key;
        }
        return result;
    }

    private static ContentComponentFileState CopyOriginal(ContentComponentFileState original,
        string path, string hash, long size, string backup) => new()
    {
        RelativePath = original.RelativePath, TargetPath = path, BackupPath = backup,
        OriginalExisted = original.OriginalExisted, OriginalSha256 = original.OriginalSha256,
        OriginalSizeBytes = original.OriginalSizeBytes, InstalledSha256 = hash, InstalledSizeBytes = size
    };

    private static InvalidOperationException ChainError(string path, string current, IEnumerable<string> owners) =>
        new($"Cannot verify the complete backup chain for {path}; existing installation retained. Current SHA-256={current}; packages={string.Join(",", owners)}; history is disconnected or ambiguous.");

    private static bool BackupMatches(ContentComponentFileState file)
    {
        if (string.IsNullOrWhiteSpace(file.BackupPath) || !File.Exists(file.BackupPath)) return false;
        var bytes = File.ReadAllBytes(file.BackupPath);
        return bytes.LongLength == file.OriginalSizeBytes && Equal(Hash(bytes), file.OriginalSha256);
    }
    private static bool Equal(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
