using System.Globalization;

namespace LevelUp.NavTableUpdater.Core.Transactions;

internal static class XCameraTransaction
{
    public static XCameraRewriteSummary Validate(string csvPath) =>
        Rewrite(TextDocument.Read(csvPath), 0, 0).Summary;

    public static XCameraRewriteSummary Apply(
        string csvPath,
        double deltaYMeters,
        double deltaZMeters,
        string backupPath)
    {
        XCameraRewriteSummary? summary = null;
        TextFileRewrite.Apply(
            csvPath,
            backupPath,
            document =>
            {
                var result = Rewrite(document, deltaYMeters, deltaZMeters);
                summary = result.Summary;
                return result.Document;
            },
            tempPath =>
            {
                var validation = Rewrite(TextDocument.Read(tempPath), 0, 0).Summary;
                if (!validation.HasCameraColumns)
                {
                    throw new InvalidOperationException("Rewritten X-Camera CSV does not contain Y/Z camera columns.");
                }
            });

        return summary ?? throw new InvalidOperationException("X-Camera rewrite did not produce a summary.");
    }

    private static (TextDocument Document, XCameraRewriteSummary Summary) Rewrite(
        TextDocument document,
        double deltaYMeters,
        double deltaZMeters)
    {
        if (document.Lines.Count == 0)
        {
            throw new InvalidOperationException("X-Camera CSV is empty.");
        }

        var header = CsvRow.Parse(document.Lines[0].Body);
        var yIndex = header.IndexOf("Y");
        var zIndex = header.IndexOf("Z");
        if (yIndex < 0 || zIndex < 0)
        {
            throw new InvalidOperationException("X-Camera CSV must contain Y and Z columns.");
        }

        var originIndex = header.IndexOf("Camera Origin");
        var categoryIndex = header.IndexOf("Category Name");
        var cgYIndex = header.IndexOf("CGY Offset");
        var cgZIndex = header.IndexOf("CGZ Offset");

        var newLines = new List<TextLine>(document.Lines.Count)
        {
            document.Lines[0]
        };
        var changedRows = 0;
        var skippedRows = 0;

        foreach (var line in document.Lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line.Body))
            {
                newLines.Add(line);
                continue;
            }

            var row = CsvRow.Parse(line.Body);
            if (row.Fields.Count <= Math.Max(yIndex, zIndex))
            {
                skippedRows++;
                newLines.Add(line);
                continue;
            }

            if (!IsCockpitOrigin(row, originIndex, categoryIndex))
            {
                skippedRows++;
                newLines.Add(line);
                continue;
            }

            var y = ParseRequiredDouble(row.Fields[yIndex], "Y");
            var z = ParseRequiredDouble(row.Fields[zIndex], "Z");
            row.Fields[yIndex] = Format(y - deltaYMeters);
            row.Fields[zIndex] = Format(z - deltaZMeters);
            ResetMatchingCgOffset(row, cgYIndex, -deltaYMeters);
            ResetMatchingCgOffset(row, cgZIndex, -deltaZMeters);
            newLines.Add(line with { Body = row.ToCsv() });
            changedRows++;
        }

        if (changedRows == 0)
        {
            throw new InvalidOperationException("X-Camera CSV was found, but no cockpit-origin camera rows could be adjusted.");
        }

        return (document.WithLines(newLines), new XCameraRewriteSummary(
            HasCameraColumns: true,
            ChangedRows: changedRows,
            SkippedRows: skippedRows));
    }

    private static bool IsCockpitOrigin(CsvRow row, int originIndex, int categoryIndex)
    {
        if (originIndex >= 0 && row.Fields.Count > originIndex)
        {
            var origin = row.Fields[originIndex].Trim();
            return origin.Length == 0 || origin == "0";
        }

        if (categoryIndex >= 0 && row.Fields.Count > categoryIndex)
        {
            return string.Equals(row.Fields[categoryIndex].Trim(), "Cockpit", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static void ResetMatchingCgOffset(CsvRow row, int index, double expectedValue)
    {
        if (index < 0 || row.Fields.Count <= index)
        {
            return;
        }

        if (double.TryParse(row.Fields[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            && Math.Abs(parsed - expectedValue) <= 0.000001)
        {
            row.Fields[index] = Format(0);
        }
    }

    private static double ParseRequiredDouble(string value, string column)
    {
        if (double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"X-Camera column {column} is not a valid number.");
    }

    private static string Format(double value) => value.ToString("0.000000", CultureInfo.InvariantCulture);

    private sealed class CsvRow
    {
        private CsvRow(List<string> fields)
        {
            Fields = fields;
        }

        public List<string> Fields { get; }

        public static CsvRow Parse(string line)
        {
            var fields = new List<string>();
            var current = new List<char>();
            var inQuotes = false;

            for (var i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (ch == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Add('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (ch == ',' && !inQuotes)
                {
                    fields.Add(new string(current.ToArray()));
                    current.Clear();
                }
                else
                {
                    current.Add(ch);
                }
            }

            fields.Add(new string(current.ToArray()));
            return new CsvRow(fields);
        }

        public int IndexOf(string header)
        {
            for (var i = 0; i < Fields.Count; i++)
            {
                if (string.Equals(Fields[i].Trim(), header, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        public string ToCsv() => string.Join(",", Fields.Select(Escape));

        private static string Escape(string value)
        {
            return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
                ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
                : value;
        }
    }
}

internal sealed record XCameraRewriteSummary(bool HasCameraColumns, int ChangedRows, int SkippedRows);
