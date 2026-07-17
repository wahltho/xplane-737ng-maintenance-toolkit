using System.Text;

namespace LevelUp.NavTableUpdater.Core.Transactions;

internal sealed class TextDocument
{
    private TextDocument(IReadOnlyList<TextLine> lines, bool hasUtf8Bom)
    {
        Lines = lines;
        HasUtf8Bom = hasUtf8Bom;
    }

    public IReadOnlyList<TextLine> Lines { get; }

    public bool HasUtf8Bom { get; }

    public static TextDocument Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var offset = hasBom ? 3 : 0;
        var text = Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);
        return new TextDocument(SplitLines(text), hasBom);
    }

    public void Write(string path)
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: HasUtf8Bom);
        File.WriteAllText(path, string.Concat(Lines.Select(line => line.Body + line.Ending)), encoding);
    }

    public TextDocument WithLines(IReadOnlyList<TextLine> lines) => new(lines, HasUtf8Bom);

    private static IReadOnlyList<TextLine> SplitLines(string text)
    {
        var lines = new List<TextLine>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                var ending = i + 1 < text.Length && text[i + 1] == '\n' ? "\r\n" : "\r";
                lines.Add(new TextLine(text[start..i], ending));
                i += ending.Length - 1;
                start = i + 1;
            }
            else if (text[i] == '\n')
            {
                lines.Add(new TextLine(text[start..i], "\n"));
                start = i + 1;
            }
        }

        if (start < text.Length)
        {
            lines.Add(new TextLine(text[start..], ""));
        }
        else if (text.Length == 0)
        {
            lines.Add(new TextLine("", ""));
        }

        return lines;
    }
}

internal sealed record TextLine(string Body, string Ending);
