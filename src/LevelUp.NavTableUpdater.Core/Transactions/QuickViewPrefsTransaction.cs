using System.Globalization;

namespace LevelUp.NavTableUpdater.Core.Transactions;

internal static class QuickViewPrefsTransaction
{
    public static QuickViewPrefsRewriteSummary Validate(string prefsPath) =>
        Rewrite(TextDocument.Read(prefsPath), 0, 0).Summary;

    public static QuickViewPrefsRewriteSummary Apply(
        string prefsPath,
        double deltaYMeters,
        double deltaZMeters,
        string backupPath)
    {
        QuickViewPrefsRewriteSummary? summary = null;
        TextFileRewrite.Apply(
            prefsPath,
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
                if (validation.YKeyCount == 0 || validation.ZKeyCount == 0)
                {
                    throw new InvalidOperationException("Rewritten quick-view prefs file has no Y/Z quick-view position keys.");
                }
            });

        return summary ?? throw new InvalidOperationException("Quick-view prefs rewrite did not produce a summary.");
    }

    private static (TextDocument Document, QuickViewPrefsRewriteSummary Summary) Rewrite(
        TextDocument document,
        double deltaYMeters,
        double deltaZMeters)
    {
        var keyCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var yCount = 0;
        var zCount = 0;
        var newLines = new List<TextLine>(document.Lines.Count);

        foreach (var line in document.Lines)
        {
            var parts = line.Body.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && IsQuickViewPositionKey(parts[0], out var axis))
            {
                keyCounts[parts[0]] = keyCounts.GetValueOrDefault(parts[0]) + 1;
                var value = ParseRequiredDouble(parts[1], parts[0]);
                var replacement = axis == 'y'
                    ? value - deltaYMeters
                    : value - deltaZMeters;
                newLines.Add(line with { Body = $"{parts[0]} {Format(replacement)}" });

                if (axis == 'y')
                {
                    yCount++;
                }
                else
                {
                    zCount++;
                }
            }
            else
            {
                newLines.Add(line);
            }
        }

        var duplicates = keyCounts.Where(pair => pair.Value != 1).Select(pair => $"{pair.Key}={pair.Value}").ToArray();
        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException($"Quick-view prefs position keys must be unique before rewriting ({string.Join(", ", duplicates)}).");
        }

        if (yCount == 0 || zCount == 0)
        {
            throw new InvalidOperationException("Quick-view prefs file has no Y/Z quick-view position keys.");
        }

        return (document.WithLines(newLines), new QuickViewPrefsRewriteSummary(yCount, zCount));
    }

    private static bool IsQuickViewPositionKey(string key, out char axis)
    {
        if (key.StartsWith("_iql_pe_y_", StringComparison.Ordinal))
        {
            axis = 'y';
            return true;
        }

        if (key.StartsWith("_iql_pe_z_", StringComparison.Ordinal))
        {
            axis = 'z';
            return true;
        }

        axis = '\0';
        return false;
    }

    private static double ParseRequiredDouble(string value, string key)
    {
        if (double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"Quick-view prefs value for {key} is not a valid number.");
    }

    private static string Format(double value) => value.ToString("0.000000", CultureInfo.InvariantCulture);
}

internal sealed record QuickViewPrefsRewriteSummary(int YKeyCount, int ZKeyCount);
