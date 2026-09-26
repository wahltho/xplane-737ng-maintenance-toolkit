using System.Text.Json;

namespace LevelUp.NavTableUpdater.Core.Content.PatchHandlers;

/// <summary>
/// Inserts a marked block or replaces one exact, declared earlier version.
/// Marker presence without an exact known body is always an error.
/// </summary>
public sealed class MarkedBlockMigrationPatchHandler : IContentPatchHandler
{
    public string Operation => "migrate-marked-block-v1";

    public bool SupportsStructuralSourceValidation => true;

    public byte[] Apply(byte[] source, JsonElement payload)
    {
        if (!payload.RequiredString("format").Equals(Operation, StringComparison.Ordinal))
            throw new InvalidOperationException("Unsupported marked-block migration format.");

        var name = payload.TryGetProperty("name", out var nameValue) && nameValue.ValueKind is JsonValueKind.String
            ? nameValue.GetString() ?? "unnamed marked block" : "unnamed marked block";
        var begin = payload.RequiredString("beginMarker");
        var end = payload.RequiredString("endMarker");
        var anchor = payload.RequiredArray("anchorLines").StringArray();
        var content = payload.RequiredArray("contentLines").StringArray();
        var position = payload.RequiredString("position");
        if (string.IsNullOrWhiteSpace(begin) || string.IsNullOrWhiteSpace(end)
            || begin.Equals(end, StringComparison.Ordinal)
            || position is not ("before" or "after") || anchor.Count == 0
            || content.Any(line => line == begin || line == end))
            throw new InvalidOperationException($"{name}: invalid marked-block migration contract.");

        var currentBlock = new[] { begin }.Concat(content).Append(end).ToArray();
        var legacyBlocks = payload.RequiredArray("legacyBlocks").EnumerateArray()
            .Select(block => block.StringArray()).ToArray();
        if (legacyBlocks.Any(block => block.Count < 2 || block[0] != begin || block[^1] != end
                || block.Skip(1).SkipLast(1).Any(line => line == begin || line == end)
                || block.SequenceEqual(currentBlock))
            || legacyBlocks.Distinct(new SequenceComparer()).Count() != legacyBlocks.Length)
            throw new InvalidOperationException($"{name}: invalid or duplicate legacy marked block.");

        var text = Utf8PatchText.Decode(source);
        var lines = text.Lines.ToList();
        var anchors = FindSequence(lines, anchor);
        if (anchors.Count != 1)
            throw new InvalidOperationException($"{name}: expected exactly one anchor; found {anchors.Count}.");

        var starts = FindSequence(lines, [begin]);
        var ends = FindSequence(lines, [end]);
        if (starts.Count == 0 && ends.Count == 0)
        {
            var at = position == "before" ? anchors[0] : anchors[0] + anchor.Count;
            lines.InsertRange(at, currentBlock);
            return text.Encode(lines);
        }

        if (starts.Count != 1 || ends.Count != 1 || starts[0] >= ends[0]
            || (position == "after" && starts[0] < anchors[0] + anchor.Count)
            || (position == "before" && ends[0] >= anchors[0]))
            throw new InvalidOperationException($"{name}: marked block is partial, duplicated or misplaced.");

        var installed = lines.Skip(starts[0]).Take(ends[0] - starts[0] + 1).ToArray();
        if (installed.SequenceEqual(currentBlock)) return source;
        if (!legacyBlocks.Any(block => block.SequenceEqual(installed)))
            throw new InvalidOperationException($"{name}: marked block has unknown or modified content.");

        lines.RemoveRange(starts[0], installed.Length);
        lines.InsertRange(starts[0], currentBlock);
        return text.Encode(lines);
    }

    private static List<int> FindSequence(IReadOnlyList<string> lines, IReadOnlyList<string> sequence)
    {
        var matches = new List<int>();
        for (var index = 0; index <= lines.Count - sequence.Count; index++)
            if (Enumerable.Range(0, sequence.Count).All(offset => lines[index + offset] == sequence[offset]))
                matches.Add(index);
        return matches;
    }

    private sealed class SequenceComparer : IEqualityComparer<IReadOnlyList<string>>
    {
        public bool Equals(IReadOnlyList<string>? left, IReadOnlyList<string>? right) =>
            left is not null && right is not null && left.SequenceEqual(right);

        public int GetHashCode(IReadOnlyList<string> lines)
        {
            var hash = new HashCode();
            foreach (var line in lines) hash.Add(line, StringComparer.Ordinal);
            return hash.ToHashCode();
        }
    }
}
