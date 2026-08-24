using System.Text.Json;

namespace LevelUp.NavTableUpdater.Core.Content.PatchHandlers;

public sealed class MarkedBlockInsertionPatchHandler : IContentPatchHandler
{
    public string Operation => "insert-marked-block-v1";

    public bool SupportsStructuralSourceValidation => true;

    public byte[] Apply(byte[] source, JsonElement payload)
    {
        if (!payload.RequiredString("format").Equals(Operation, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Unsupported marked-block patch format.");
        }

        var name = payload.TryGetProperty("name", out var nameValue) && nameValue.ValueKind is JsonValueKind.String
            ? nameValue.GetString() ?? "unnamed marked block"
            : "unnamed marked block";
        var beginMarker = payload.RequiredString("beginMarker");
        var endMarker = payload.RequiredString("endMarker");
        var contentLines = payload.RequiredArray("contentLines").StringArray();
        var anchorLines = payload.RequiredArray("anchorLines").StringArray();
        var position = payload.RequiredString("position");
        if (beginMarker.Equals(endMarker, StringComparison.Ordinal)
            || contentLines.Any(line => line.Equals(beginMarker, StringComparison.Ordinal)
                || line.Equals(endMarker, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"{name}: block markers must be unique and outside contentLines.");
        }

        if (position is not ("before" or "after"))
        {
            throw new InvalidOperationException($"{name}: position must be 'before' or 'after'.");
        }

        var text = Utf8PatchText.Decode(source);
        var lines = text.Lines.ToList();
        var beginMatches = FindSequence(lines, [beginMarker]);
        var endMatches = FindSequence(lines, [endMarker]);
        var installedBlock = new[] { beginMarker }.Concat(contentLines).Append(endMarker).ToArray();
        if (beginMatches.Count > 0 || endMatches.Count > 0)
        {
            var installedMatches = FindSequence(lines, installedBlock);
            if (beginMatches.Count == 1 && endMatches.Count == 1 && installedMatches.Count == 1)
            {
                return source;
            }

            throw new InvalidOperationException(
                $"{name}: marked block is partial, duplicated or modified; found begin={beginMatches.Count}, end={endMatches.Count}, exact={installedMatches.Count}.");
        }

        var anchors = FindSequence(lines, anchorLines);
        if (anchors.Count != 1)
        {
            throw new InvalidOperationException(
                $"{name}: expected exactly one insertion anchor; found {anchors.Count}.");
        }

        var insertAt = position.Equals("before", StringComparison.Ordinal)
            ? anchors[0]
            : anchors[0] + anchorLines.Count;
        lines.InsertRange(insertAt, installedBlock);
        return text.Encode(lines);
    }

    private static List<int> FindSequence(IReadOnlyList<string> lines, IReadOnlyList<string> sequence)
    {
        if (sequence.Count == 0)
        {
            throw new InvalidOperationException("Empty marked-block sequence.");
        }

        var matches = new List<int>();
        for (var index = 0; index <= lines.Count - sequence.Count; index++)
        {
            if (Enumerable.Range(0, sequence.Count).All(offset =>
                    lines[index + offset].Equals(sequence[offset], StringComparison.Ordinal)))
            {
                matches.Add(index);
            }
        }

        return matches;
    }
}
