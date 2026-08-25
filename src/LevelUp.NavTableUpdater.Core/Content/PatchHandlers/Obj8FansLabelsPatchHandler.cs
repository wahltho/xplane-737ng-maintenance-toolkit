using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace LevelUp.NavTableUpdater.Core.Content.PatchHandlers;

public sealed class Obj8FansLabelsPatchHandler : IContentPatchHandler
{
    public string Operation => "obj8-fans-label-switch-v1";

    public bool SupportsStructuralSourceValidation => true;

    public byte[] Apply(byte[] sourceBytes, JsonElement payload)
    {
        if (!payload.RequiredString("format").Equals(Operation, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Unsupported OBJ8 patch format.");
        }

        var text = Utf8PatchText.Decode(sourceBytes);
        var parsed = Parse(text.Lines);
        var source = payload.RequiredObject("source");
        var sourceVertexCount = source.RequiredInt32("vertexCount");
        var sourceIndexCount = source.RequiredInt32("indexCount");
        var addedVertices = payload.RequiredArray("addedVertices").StringArray();
        var addedIndices = payload.RequiredArray("addedIndices").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        var result = payload.RequiredObject("result");
        var resultVertexCount = result.RequiredInt32("vertexCount");
        var resultIndexCount = result.RequiredInt32("indexCount");
        if (resultVertexCount != sourceVertexCount + addedVertices.Count
            || resultIndexCount != sourceIndexCount + addedIndices.Length)
        {
            throw new InvalidOperationException("OBJ8 result counts do not match patch declaration.");
        }

        var move = payload.RequiredObject("moveIndexRangesToEnd");
        var positions = new List<int>();
        foreach (var range in move.RequiredArray("ranges").EnumerateArray())
        {
            if (range.ValueKind is not JsonValueKind.Array)
            {
                throw new InvalidOperationException("OBJ8 move range must be an array.");
            }

            var values = range.EnumerateArray().Select(value => value.GetInt32()).ToArray();
            if (values.Length != 2)
            {
                throw new InvalidOperationException("OBJ8 move range must contain offset and count.");
            }

            var offset = values[0];
            var count = values[1];
            if (offset < 0 || count <= 0 || offset > sourceIndexCount - count)
            {
                throw new InvalidOperationException("OBJ8 classic CDU label index range is invalid.");
            }

            positions.AddRange(Enumerable.Range(offset, count));
        }

        if (!positions.SequenceEqual(positions.Distinct().Order()))
        {
            throw new InvalidOperationException("OBJ8 classic CDU label index ranges overlap or are unordered.");
        }

        var pointCountMatches = parsed.Prefix
            .Select((line, index) => (line, index))
            .Where(item => item.line.StartsWith("POINT_COUNTS ", StringComparison.Ordinal))
            .ToArray();
        if (pointCountMatches.Length != 1)
        {
            throw new InvalidOperationException("Unexpected OBJ8 POINT_COUNTS declaration.");
        }

        var replaceDraw = payload.RequiredObject("replaceFinalDraw");
        var newDrawLines = replaceDraw.RequiredArray("newLines").StringArray();
        var hasSourceShape = parsed.Vertices.Count == sourceVertexCount
            && parsed.Indices.Count == sourceIndexCount;
        var hasResultShape = parsed.Vertices.Count == resultVertexCount
            && parsed.Indices.Count == resultIndexCount;
        if (hasResultShape)
        {
            ValidateInstalledResult(
                parsed,
                sourceVertexCount,
                sourceIndexCount,
                positions.Count,
                move.RequiredString("sha256"),
                addedVertices,
                addedIndices,
                result.RequiredString("pointCountsLine"),
                pointCountMatches[0],
                newDrawLines);
            return sourceBytes;
        }

        if (!hasSourceShape)
        {
            throw new InvalidOperationException(
                $"Unexpected OBJ8 counts: vertices={parsed.Vertices.Count}, indices={parsed.Indices.Count}.");
        }

        var positionSet = positions.ToHashSet();
        var moved = parsed.Indices.Where((_, index) => positionSet.Contains(index)).ToArray();
        if (!Sha256Indices(moved).Equals(move.RequiredString("sha256"), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("OBJ8 classic CDU label index range does not match.");
        }

        var expectedDraw = replaceDraw.RequiredString("old");
        if (parsed.Commands.Count == 0 || !parsed.Commands[^1].Equals(expectedDraw, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected final OBJ8 draw command '{expectedDraw}'.");
        }

        var resultVertices = parsed.Vertices.Concat(addedVertices).ToList();
        var resultIndices = parsed.Indices.Where((_, index) => !positionSet.Contains(index)).Concat(moved).Concat(addedIndices).ToList();
        if (!pointCountMatches[0].line.Equals(source.RequiredString("pointCountsLine"), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Unexpected OBJ8 POINT_COUNTS declaration.");
        }

        parsed.Prefix[pointCountMatches[0].index] = result.RequiredString("pointCountsLine");
        var commands = parsed.Commands.Take(parsed.Commands.Count - 1)
            .Concat(newDrawLines);
        var lines = parsed.Prefix
            .Concat(resultVertices)
            .Concat(SerializeIndices(resultIndices))
            .Concat(commands)
            .ToArray();
        return text.Encode(lines);
    }

    private static void ValidateInstalledResult(
        Obj8Document parsed,
        int sourceVertexCount,
        int sourceIndexCount,
        int movedIndexCount,
        string movedIndexSha256,
        IReadOnlyList<string> addedVertices,
        IReadOnlyList<int> addedIndices,
        string resultPointCountsLine,
        (string line, int index) pointCounts,
        IReadOnlyList<string> newDrawLines)
    {
        if (!pointCounts.line.Equals(resultPointCountsLine, StringComparison.Ordinal)
            || !parsed.Vertices.Skip(sourceVertexCount).SequenceEqual(addedVertices)
            || !parsed.Indices.Skip(sourceIndexCount).SequenceEqual(addedIndices)
            || newDrawLines.Count == 0
            || parsed.Commands.Count < newDrawLines.Count
            || !parsed.Commands.TakeLast(newDrawLines.Count).SequenceEqual(newDrawLines))
        {
            throw new InvalidOperationException("OBJ8 installed FANS label geometry is incomplete or modified.");
        }

        var movedStart = sourceIndexCount - movedIndexCount;
        if (movedStart < 0
            || !Sha256Indices(parsed.Indices.Skip(movedStart).Take(movedIndexCount))
                .Equals(movedIndexSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("OBJ8 installed classic CDU label indices do not match.");
        }
    }

    private static Obj8Document Parse(IReadOnlyList<string> lines)
    {
        var pointCountsIndex = IndexOf(lines, line => line.StartsWith("POINT_COUNTS ", StringComparison.Ordinal));
        var vertexStart = IndexOf(lines, line => line.StartsWith("VT ", StringComparison.Ordinal));
        var indexStart = IndexOf(lines, line => line.StartsWith("IDX", StringComparison.Ordinal));
        if (pointCountsIndex < 0 || vertexStart < 0 || indexStart < 0 || pointCountsIndex >= vertexStart || vertexStart >= indexStart)
        {
            throw new InvalidOperationException("OBJ8 structure is missing or misorders POINT_COUNTS, VT or IDX data.");
        }

        var commandStart = indexStart;
        var indices = new List<int>();
        while (commandStart < lines.Count && lines[commandStart].StartsWith("IDX", StringComparison.Ordinal))
        {
            var parts = lines[commandStart].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts[0] is not ("IDX" or "IDX10"))
            {
                throw new InvalidOperationException($"Unsupported OBJ8 index directive: {parts[0]}.");
            }

            indices.AddRange(parts.Skip(1).Select(value => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture)));
            commandStart++;
        }

        return new Obj8Document(
            lines.Take(vertexStart).ToList(),
            lines.Skip(vertexStart).Take(indexStart - vertexStart).ToList(),
            indices,
            lines.Skip(commandStart).ToList());
    }

    private static IEnumerable<string> SerializeIndices(IReadOnlyList<int> indices)
    {
        var complete = indices.Count / 10 * 10;
        for (var offset = 0; offset < complete; offset += 10)
        {
            yield return "IDX10 " + string.Join(' ', indices.Skip(offset).Take(10));
        }

        for (var index = complete; index < indices.Count; index++)
        {
            yield return $"IDX {indices[index]}";
        }
    }

    private static int IndexOf(IReadOnlyList<string> lines, Func<string, bool> predicate)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            if (predicate(lines[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static string Sha256Indices(IEnumerable<int> indices)
    {
        var values = indices.ToArray();
        var bytes = new byte[values.Length * sizeof(uint)];
        for (var index = 0; index < values.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(index * sizeof(uint), sizeof(uint)),
                checked((uint)values[index]));
        }

        return Sha256(bytes);
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record Obj8Document(
        List<string> Prefix,
        List<string> Vertices,
        List<int> Indices,
        List<string> Commands);
}
