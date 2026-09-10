using System.Text.Json;

namespace LevelUp.NavTableUpdater.Core.Content.PatchHandlers;

public sealed class ExactTextReplacementsPatchHandler : IContentPatchHandler
{
    public string Operation => "exact-text-replacements-v1";

    public bool SupportsStructuralSourceValidation => true;

    public byte[] Apply(byte[] source, JsonElement payload)
    {
        if (!payload.RequiredString("format").Equals(Operation, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Unsupported exact-text patch format.");
        }

        var text = Utf8PatchText.Decode(source);
        var lines = text.Lines.ToList();
        foreach (var replacement in payload.RequiredArray("replacements").EnumerateArray())
        {
            var oldLines = replacement.RequiredArray("oldLines").StringArray();
            var newLines = replacement.RequiredArray("newLines").StringArray();
            var name = replacement.TryGetProperty("name", out var nameValue) && nameValue.ValueKind is JsonValueKind.String
                ? nameValue.GetString() ?? "unnamed replacement"
                : "unnamed replacement";
            if (FindSequence(newLines, oldLines).Count > 0)
            {
                throw new InvalidOperationException(
                    $"{name}: old block occurs inside the installed block and cannot be classified idempotently.");
            }

            var oldMatches = FindSequence(lines, oldLines);
            var newMatches = FindSequence(lines, newLines);
            var legacyMatches = FindLegacyBlocks(lines, replacement, name);
            if (oldMatches.Count == 1 && newMatches.Count == 0 && legacyMatches.Count == 0)
            {
                lines.RemoveRange(oldMatches[0], oldLines.Count);
                lines.InsertRange(oldMatches[0], newLines);
            }
            else if (oldMatches.Count == 0 && newMatches.Count == 1 && legacyMatches.Count == 0)
            {
                continue;
            }
            else if (oldMatches.Count == 0 && newMatches.Count == 0 && legacyMatches.Count == 1)
            {
                // Block installed by an earlier release of the same package: upgrade in place.
                var (start, length) = legacyMatches[0];
                lines.RemoveRange(start, length);
                lines.InsertRange(start, newLines);
            }
            else
            {
                throw new InvalidOperationException(
                    $"{name}: expected exactly one old block or one installed block; found old={oldMatches.Count}, installed={newMatches.Count}, legacy={legacyMatches.Count}.");
            }
        }

        return text.Encode(lines);
    }

    /// <summary>
    /// Locates blocks written by earlier releases of the same package
    /// (<c>legacyNewLines</c>: an array of line arrays). Each legacy block must be
    /// distinct from the old and the current installed block.
    /// </summary>
    private static List<(int Start, int Length)> FindLegacyBlocks(IReadOnlyList<string> lines, JsonElement replacement, string name)
    {
        var matches = new List<(int Start, int Length)>();
        if (!replacement.TryGetProperty("legacyNewLines", out var legacyValue) || legacyValue.ValueKind is not JsonValueKind.Array)
        {
            return matches;
        }

        foreach (var legacyElement in legacyValue.EnumerateArray())
        {
            if (legacyElement.ValueKind is not JsonValueKind.Array)
            {
                throw new InvalidOperationException($"{name}: legacyNewLines entries must be arrays of lines.");
            }

            var legacyLines = legacyElement.StringArray();
            foreach (var start in FindSequence(lines, legacyLines))
            {
                matches.Add((start, legacyLines.Count));
            }
        }

        return matches;
    }

    private static List<int> FindSequence(IReadOnlyList<string> lines, IReadOnlyList<string> sequence)
    {
        if (sequence.Count == 0)
        {
            throw new InvalidOperationException("Empty text replacement sequence.");
        }

        var matches = new List<int>();
        for (var index = 0; index <= lines.Count - sequence.Count; index++)
        {
            if (Enumerable.Range(0, sequence.Count).All(offset => lines[index + offset].Equals(sequence[offset], StringComparison.Ordinal)))
            {
                matches.Add(index);
            }
        }

        return matches;
    }
}
