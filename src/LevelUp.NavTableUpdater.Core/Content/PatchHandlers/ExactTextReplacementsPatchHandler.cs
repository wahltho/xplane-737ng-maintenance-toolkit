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
            if (oldMatches.Count == 1 && newMatches.Count == 0)
            {
                lines.RemoveRange(oldMatches[0], oldLines.Count);
                lines.InsertRange(oldMatches[0], newLines);
            }
            else if (oldMatches.Count == 0 && newMatches.Count == 1)
            {
                continue;
            }
            else
            {
                throw new InvalidOperationException(
                    $"{name}: expected exactly one old block or one installed block; found old={oldMatches.Count}, installed={newMatches.Count}.");
            }
        }

        return text.Encode(lines);
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
