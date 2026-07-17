using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.Manifest;

namespace LevelUp.NavTableUpdater.Core.Transactions;

internal static class VnavLuaPatchTransaction
{
    public static VnavLuaPatchSummary Apply(
        string targetScriptPath,
        PackageManifest manifest,
        IReadOnlyDictionary<string, PackagePayload> payloads,
        string backupPath)
    {
        VnavLuaPatchSummary? summary = null;
        TextFileRewrite.Apply(
            targetScriptPath,
            backupPath,
            document =>
            {
                var result = RewriteApply(document, manifest, payloads);
                summary = result.Summary;
                return result.Document;
            },
            tempPath => ValidateMarkedInstall(tempPath, manifest));

        return summary ?? throw new InvalidOperationException("VNAV Lua patch transaction did not produce a summary.");
    }

    public static VnavLuaPatchSummary Uninstall(
        string targetScriptPath,
        PackageManifest manifest,
        string backupPath)
    {
        VnavLuaPatchSummary? summary = null;
        TextFileRewrite.Apply(
            targetScriptPath,
            backupPath,
            document =>
            {
                var result = RewriteUninstall(document, manifest);
                summary = result.Summary;
                return result.Document;
            },
            tempPath => ValidateUninstalled(tempPath, manifest));

        return summary ?? throw new InvalidOperationException("VNAV Lua uninstall transaction did not produce a summary.");
    }

    public static bool HasMarkedBlocks(string targetScriptPath, PackageManifest manifest)
    {
        var document = TextDocument.Read(targetScriptPath);
        return manifest.PatchOperations.Any(operation => CountContaining(document.Lines, operation.BeginMarker) > 0
            || CountContaining(document.Lines, operation.EndMarker) > 0);
    }

    private static (TextDocument Document, VnavLuaPatchSummary Summary) RewriteApply(
        TextDocument document,
        PackageManifest manifest,
        IReadOnlyDictionary<string, PackagePayload> payloads)
    {
        ValidateMarkerCounts(document.Lines, manifest);

        var lines = document.Lines.ToList();
        var preferredEnding = PreferredEnding(document);
        var inserted = 0;
        var replaced = 0;
        var migratedLegacy = 0;

        foreach (var operation in manifest.PatchOperations)
        {
            var fragmentFileName = manifest.Payloads.FirstOrDefault(payload => payload.Id.Equals(operation.Id, StringComparison.OrdinalIgnoreCase))?.FileName
                ?? throw new InvalidOperationException($"Manifest payload for patch operation '{operation.Id}' is missing.");
            if (!payloads.TryGetValue(fragmentFileName, out var fragmentPayload))
            {
                throw new InvalidOperationException($"Resolved payload '{fragmentFileName}' for patch operation '{operation.Id}' is missing.");
            }

            var fragmentLines = BuildFragmentLines(fragmentPayload.Text, preferredEnding);
            var beginIndex = IndexOfContaining(lines, operation.BeginMarker);
            if (beginIndex >= 0)
            {
                var endIndex = IndexOfContaining(lines, operation.EndMarker, beginIndex + 1);
                if (endIndex < 0)
                {
                    throw new InvalidOperationException($"{operation.Id}: marker block is incomplete.");
                }

                lines.RemoveRange(beginIndex, endIndex - beginIndex + 1);
                lines.InsertRange(beginIndex, fragmentLines);
                replaced++;
                continue;
            }

            var legacyRemoved = RemoveLegacyBlocks(lines, operation);
            if (legacyRemoved > 0)
            {
                migratedLegacy += legacyRemoved;
            }

            var anchorIndex = IndexOfNonCommentedContaining(lines, operation.Anchor);
            if (anchorIndex < 0)
            {
                throw new InvalidOperationException($"{operation.Id}: required anchor was not found.");
            }

            if (IndexOfNonCommentedContaining(lines, operation.Anchor, anchorIndex + 1) >= 0)
            {
                throw new InvalidOperationException($"{operation.Id}: required anchor is not unique.");
            }

            lines.InsertRange(anchorIndex + 1, fragmentLines);
            inserted++;
        }

        return (document.WithLines(lines), new VnavLuaPatchSummary(
            InsertedBlocks: inserted,
            ReplacedBlocks: replaced,
            RemovedBlocks: 0,
            MigratedLegacyBlocks: migratedLegacy));
    }

    private static (TextDocument Document, VnavLuaPatchSummary Summary) RewriteUninstall(
        TextDocument document,
        PackageManifest manifest)
    {
        ValidateMarkerCounts(document.Lines, manifest);

        var lines = document.Lines.ToList();
        var removed = 0;
        foreach (var operation in manifest.PatchOperations)
        {
            var beginIndex = IndexOfContaining(lines, operation.BeginMarker);
            if (beginIndex < 0)
            {
                continue;
            }

            var endIndex = IndexOfContaining(lines, operation.EndMarker, beginIndex + 1);
            if (endIndex < 0)
            {
                throw new InvalidOperationException($"{operation.Id}: marker block is incomplete.");
            }

            lines.RemoveRange(beginIndex, endIndex - beginIndex + 1);
            removed++;
        }

        if (removed == 0)
        {
            throw new InvalidOperationException("No manifest-owned VNAV marker blocks were found to uninstall.");
        }

        return (document.WithLines(lines), new VnavLuaPatchSummary(
            InsertedBlocks: 0,
            ReplacedBlocks: 0,
            RemovedBlocks: removed,
            MigratedLegacyBlocks: 0));
    }

    private static void ValidateMarkedInstall(string path, PackageManifest manifest)
    {
        var lines = TextDocument.Read(path).Lines;
        foreach (var operation in manifest.PatchOperations)
        {
            var beginCount = CountContaining(lines, operation.BeginMarker);
            var endCount = CountContaining(lines, operation.EndMarker);
            if (beginCount != 1 || endCount != 1)
            {
                throw new InvalidOperationException($"{operation.Id}: rewritten target must contain exactly one begin/end marker pair.");
            }
        }
    }

    private static void ValidateUninstalled(string path, PackageManifest manifest)
    {
        var lines = TextDocument.Read(path).Lines;
        foreach (var operation in manifest.PatchOperations)
        {
            var beginCount = CountContaining(lines, operation.BeginMarker);
            var endCount = CountContaining(lines, operation.EndMarker);
            if (beginCount != 0 || endCount != 0)
            {
                throw new InvalidOperationException($"{operation.Id}: uninstalled target still contains manifest-owned markers.");
            }
        }
    }

    private static void ValidateMarkerCounts(IReadOnlyList<TextLine> lines, PackageManifest manifest)
    {
        foreach (var operation in manifest.PatchOperations)
        {
            var beginCount = CountContaining(lines, operation.BeginMarker);
            var endCount = CountContaining(lines, operation.EndMarker);
            if (beginCount != endCount || beginCount > 1 || endCount > 1)
            {
                throw new InvalidOperationException($"{operation.Id}: marker mismatch or duplicate marker detected.");
            }
        }
    }

    private static int RemoveLegacyBlocks(List<TextLine> lines, PatchOperation operation)
    {
        var removed = 0;
        foreach (var signature in operation.LegacySignatures)
        {
            while (true)
            {
                var index = IndexOfNonCommentedContaining(lines, signature);
                if (index < 0)
                {
                    break;
                }

                var count = LegacyRemovalCount(lines, operation.Id, index);
                lines.RemoveRange(index, count);
                removed++;
            }
        }

        return removed;
    }

    private static int LegacyRemovalCount(IReadOnlyList<TextLine> lines, string operationId, int startIndex)
    {
        if (string.Equals(operationId, "dofile", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        for (var index = startIndex + 1; index < lines.Count; index++)
        {
            if (string.Equals(lines[index].Body.Trim(), "end", StringComparison.Ordinal))
            {
                return index - startIndex + 1;
            }
        }

        throw new InvalidOperationException($"{operationId}: legacy block starts but no closing end was found.");
    }

    private static IReadOnlyList<TextLine> BuildFragmentLines(string fragmentText, string ending)
    {
        var normalized = fragmentText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var bodies = normalized.Split('\n').ToList();
        if (bodies.Count > 0 && bodies[^1].Length == 0)
        {
            bodies.RemoveAt(bodies.Count - 1);
        }

        if (bodies.Count == 0)
        {
            throw new InvalidOperationException("Patch fragment is empty.");
        }

        return bodies.Select(body => new TextLine(body, ending)).ToArray();
    }

    private static string PreferredEnding(TextDocument document) =>
        document.Lines.Select(line => line.Ending).FirstOrDefault(ending => ending.Length > 0) ?? "\n";

    private static int CountContaining(IReadOnlyList<TextLine> lines, string text) =>
        string.IsNullOrWhiteSpace(text)
            ? 0
            : lines.Count(line => line.Body.Contains(text, StringComparison.Ordinal));

    private static int IndexOfContaining(IReadOnlyList<TextLine> lines, string text, int start = 0)
    {
        for (var index = start; index < lines.Count; index++)
        {
            if (lines[index].Body.Contains(text, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static int IndexOfNonCommentedContaining(IReadOnlyList<TextLine> lines, string text, int start = 0)
    {
        for (var index = start; index < lines.Count; index++)
        {
            if (lines[index].Body.Contains(text, StringComparison.Ordinal)
                && !lines[index].Body.TrimStart().StartsWith("--", StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}

internal sealed record VnavLuaPatchSummary(
    int InsertedBlocks,
    int ReplacedBlocks,
    int RemovedBlocks,
    int MigratedLegacyBlocks);
