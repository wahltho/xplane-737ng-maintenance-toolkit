using System.Security.Cryptography;
using LevelUp.NavTableUpdater.Core.Manifest;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Content;

internal static class CatalogGroupMigration
{
    // Follow recorded hash transitions, never guess that the oldest whole-file backup is compatible.
    public static ContentComponentState? Prepare(string aircraftRoot, CompatibilityPackageManifest manifest,
        IReadOnlyDictionary<string, ContentComponentState>? installed)
    {
        if (manifest.Sources.Count == 0 || installed is null) return null;
        var sourceIds = manifest.Sources.Select(s => s.PackageId).ToHashSet(StringComparer.Ordinal);
        var previous = installed.Where(p => sourceIds.Contains(p.Key)).Select(p => p.Value).ToArray();
        if (previous.Length == 0) return null;
        var targets = manifest.Modules.SelectMany(m => m.Targets).Select(t => t.RelativePath).ToHashSet(StringComparer.Ordinal);
        var result = new ContentComponentState { ComponentId = manifest.PackageId, RestoreAvailable = previous.All(p => p.RestoreAvailable) };
        foreach (var group in previous.SelectMany(p => p.Files).GroupBy(f => f.RelativePath, StringComparer.Ordinal))
        {
            if (!targets.Contains(group.Key)) throw new InvalidOperationException($"Catalog migration omits managed target {group.Key}.");
            var path = ContentPatchPathSafety.ResolveTarget(aircraftRoot, group.Key, "Catalog migration");
            if (!File.Exists(path)) throw new InvalidOperationException($"Migration target is missing: {group.Key}.");
            var current = File.ReadAllBytes(path);
            var hash = Hash(current);
            var remaining = group.ToList();
            var initialMatches = remaining
                .Where(f => string.Equals(f.InstalledSha256, hash, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (initialMatches.Length == 0)
            {
                // A clean aircraft reinstall can restore the exact pre-patch file while
                // standalone patch state remains outside the aircraft directory. Rebase
                // only on a byte-for-byte verified recorded original; an unknown file
                // must continue to block migration.
                var recordedOriginal = remaining.FirstOrDefault(f =>
                    f.OriginalExisted
                    && string.Equals(f.OriginalSha256, hash, StringComparison.OrdinalIgnoreCase)
                    && BackupMatches(f));
                if (recordedOriginal is null)
                    throw new InvalidOperationException($"Cannot verify the complete backup chain for {group.Key}; existing installation retained.");

                result.Files.Add(new()
                {
                    RelativePath = group.Key,
                    TargetPath = path,
                    BackupPath = recordedOriginal.BackupPath,
                    OriginalExisted = true,
                    OriginalSha256 = recordedOriginal.OriginalSha256,
                    OriginalSizeBytes = recordedOriginal.OriginalSizeBytes,
                    InstalledSha256 = hash,
                    InstalledSizeBytes = current.LongLength
                });
                continue;
            }
            ContentComponentFileState? original = null;
            while (remaining.Count > 0)
            {
                var matching = remaining.Where(f => string.Equals(f.InstalledSha256, hash, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matching.Length == 0) throw new InvalidOperationException($"Cannot verify the complete backup chain for {group.Key}; existing installation retained.");
                // Validate all no-change records at this hash before stepping back to an older hash.
                // Otherwise those records become unreachable even though their backups are valid.
                var next = matching.FirstOrDefault(f => f.OriginalExisted
                    && string.Equals(f.OriginalSha256, f.InstalledSha256, StringComparison.OrdinalIgnoreCase))
                    ?? matching[0];
                remaining.Remove(next);
                if (next.OriginalExisted)
                {
                    if (string.IsNullOrWhiteSpace(next.BackupPath) || !File.Exists(next.BackupPath))
                        throw new InvalidOperationException($"Original backup is unavailable for {group.Key}.");
                    var bytes = File.ReadAllBytes(next.BackupPath);
                    if (bytes.LongLength != next.OriginalSizeBytes || !Hash(bytes).Equals(next.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Original backup failed verification for {group.Key}.");
                    hash = Hash(bytes);
                }
                else hash = "";
                original = next;
            }
            result.Files.Add(new() { RelativePath = group.Key, TargetPath = path,
                BackupPath = original!.BackupPath, OriginalExisted = original.OriginalExisted,
                OriginalSha256 = original.OriginalSha256, OriginalSizeBytes = original.OriginalSizeBytes,
                InstalledSha256 = Hash(current), InstalledSizeBytes = current.LongLength });
        }
        return result;
    }

    private static bool BackupMatches(ContentComponentFileState file)
    {
        if (string.IsNullOrWhiteSpace(file.BackupPath) || !File.Exists(file.BackupPath)) return false;
        var bytes = File.ReadAllBytes(file.BackupPath);
        return bytes.LongLength == file.OriginalSizeBytes
            && Hash(bytes).Equals(file.OriginalSha256, StringComparison.OrdinalIgnoreCase);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
