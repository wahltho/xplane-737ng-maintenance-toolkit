using System.Text;

namespace LevelUp.NavTableUpdater.Core.Content.PatchHandlers;

internal sealed record Utf8PatchText(IReadOnlyList<string> Lines, string LineEnding, bool HasFinalEnding, bool HasBom)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    public static Utf8PatchText Decode(byte[] bytes)
    {
        var hasBom = bytes.AsSpan().StartsWith(Utf8Bom);
        var text = StrictUtf8.GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));
        var crlf = CountOccurrences(text, "\r\n");
        var lfOnly = text.Count(ch => ch == '\n') - crlf;
        var ending = crlf > lfOnly ? "\r\n" : "\n";
        var hasFinal = text.EndsWith('\n') || text.EndsWith('\r');
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').ToList();
        if (hasFinal && lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return new Utf8PatchText(lines, ending, hasFinal, hasBom);
    }

    public byte[] Encode(IReadOnlyList<string> lines)
    {
        var text = string.Join(LineEnding, lines) + (HasFinalEnding ? LineEnding : "");
        var content = StrictUtf8.GetBytes(text);
        if (!HasBom)
        {
            return content;
        }

        return [0xEF, 0xBB, 0xBF, .. content];
    }

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        for (var index = 0; (index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0; index += needle.Length)
        {
            count++;
        }

        return count;
    }
}
