using System.Globalization;
using LevelUp.NavTableUpdater.Core.Aircraft;

namespace LevelUp.NavTableUpdater.Core.Transactions;

internal static class QuickViewPrefsTransferTransaction
{
    public static QuickViewPrefsTransferPlan Plan(
        string sourcePrefsPath,
        string targetPrefsPath,
        double deltaYMeters,
        double deltaZMeters)
    {
        var template = ReadTemplate(TextDocument.Read(sourcePrefsPath), deltaYMeters, deltaZMeters);
        var target = TextDocument.Read(targetPrefsPath);
        var rewritten = RewriteTarget(target, template.Lines);
        return new QuickViewPrefsTransferPlan(
            template.QuickView0,
            template.Lines.Count,
            template.YKeyCount,
            template.ZKeyCount,
            Changed: !target.ToBytes().SequenceEqual(rewritten.ToBytes()));
    }

    public static QuickViewPrefsTransferPlan Apply(
        string sourcePrefsPath,
        string targetPrefsPath,
        double deltaYMeters,
        double deltaZMeters,
        string backupPath)
    {
        var template = ReadTemplate(TextDocument.Read(sourcePrefsPath), deltaYMeters, deltaZMeters);
        QuickViewPrefsTransferPlan? plan = null;

        TextFileRewrite.Apply(
            targetPrefsPath,
            backupPath,
            target =>
            {
                var rewritten = RewriteTarget(target, template.Lines);
                plan = new QuickViewPrefsTransferPlan(
                    template.QuickView0,
                    template.Lines.Count,
                    template.YKeyCount,
                    template.ZKeyCount,
                    Changed: !target.ToBytes().SequenceEqual(rewritten.ToBytes()));
                return rewritten;
            },
            tempPath => ValidateWrittenTemplate(TextDocument.Read(tempPath), template));

        return plan ?? throw new InvalidOperationException("Quick View transfer did not produce a plan.");
    }

    private static QuickViewTemplate ReadTemplate(
        TextDocument source,
        double deltaYMeters,
        double deltaZMeters)
    {
        var lines = new List<QuickViewTemplateLine>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var yCount = 0;
        var zCount = 0;

        foreach (var line in source.Lines.Where(line => IsQuickViewLine(line.Body)))
        {
            var parts = line.Body.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                throw new InvalidOperationException("Source Quick View prefs contain an invalid _iql_ entry.");
            }

            var key = parts[0];
            if (!keys.Add(key))
            {
                throw new InvalidOperationException($"Source Quick View key must be unique: {key}.");
            }

            var value = parts[1].Trim();
            if (key.StartsWith("_iql_pe_y_", StringComparison.Ordinal))
            {
                value = Format(ParseRequiredDouble(value, key) - deltaYMeters);
                yCount++;
            }
            else if (key.StartsWith("_iql_pe_z_", StringComparison.Ordinal))
            {
                value = Format(ParseRequiredDouble(value, key) - deltaZMeters);
                zCount++;
            }

            lines.Add(new QuickViewTemplateLine(key, value));
            values[key] = value;
        }

        if (lines.Count == 0 || yCount == 0 || zCount == 0)
        {
            throw new InvalidOperationException("Source prefs contain no complete Quick View position set.");
        }

        var quickView0 = new QuickView0(
            ParseRequiredTemplateValue(values, "_iql_pe_x_0"),
            ParseRequiredTemplateValue(values, "_iql_pe_y_0"),
            ParseRequiredTemplateValue(values, "_iql_pe_z_0"),
            ParseRequiredTemplateValue(values, "_iql_look_os_the_0"));

        return new QuickViewTemplate(lines, quickView0, yCount, zCount);
    }

    private static TextDocument RewriteTarget(
        TextDocument target,
        IReadOnlyList<QuickViewTemplateLine> template)
    {
        var firstQuickViewIndex = -1;
        for (var i = 0; i < target.Lines.Count; i++)
        {
            if (IsQuickViewLine(target.Lines[i].Body))
            {
                firstQuickViewIndex = i;
                break;
            }
        }

        var lineEnding = target.Lines
            .Select(line => line.Ending)
            .FirstOrDefault(ending => ending.Length > 0)
            ?? Environment.NewLine;
        var rewritten = new List<TextLine>(target.Lines.Count + template.Count);
        var inserted = false;

        for (var i = 0; i < target.Lines.Count; i++)
        {
            if (i == firstQuickViewIndex)
            {
                AddTemplate(rewritten, template, lineEnding);
                inserted = true;
            }

            if (!IsQuickViewLine(target.Lines[i].Body))
            {
                rewritten.Add(target.Lines[i]);
            }
        }

        if (!inserted)
        {
            if (rewritten.Count > 0 && rewritten[^1].Ending.Length == 0)
            {
                rewritten[^1] = rewritten[^1] with { Ending = lineEnding };
            }

            AddTemplate(rewritten, template, lineEnding);
        }

        return target.WithLines(rewritten);
    }

    private static void ValidateWrittenTemplate(TextDocument document, QuickViewTemplate expected)
    {
        var actual = ReadTemplate(document, 0, 0);
        if (actual.Lines.Count != expected.Lines.Count
            || actual.YKeyCount != expected.YKeyCount
            || actual.ZKeyCount != expected.ZKeyCount)
        {
            throw new InvalidOperationException("Written Quick View key counts do not match the source template.");
        }

        for (var i = 0; i < expected.Lines.Count; i++)
        {
            if (!actual.Lines[i].Equals(expected.Lines[i]))
            {
                throw new InvalidOperationException($"Written Quick View value does not match the source template: {expected.Lines[i].Key}.");
            }
        }
    }

    private static void AddTemplate(
        ICollection<TextLine> target,
        IEnumerable<QuickViewTemplateLine> template,
        string lineEnding)
    {
        foreach (var line in template)
        {
            target.Add(new TextLine($"{line.Key} {line.Value}", lineEnding));
        }
    }

    private static bool IsQuickViewLine(string body) =>
        body.TrimStart().StartsWith("_iql_", StringComparison.Ordinal);

    private static double ParseRequiredTemplateValue(
        IReadOnlyDictionary<string, string> values,
        string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            throw new InvalidOperationException($"Source Quick View 0 is incomplete; missing {key}.");
        }

        return ParseRequiredDouble(value, key);
    }

    private static double ParseRequiredDouble(string value, string key)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"Quick View value for {key} is not a valid number.");
    }

    private static string Format(double value) =>
        value.ToString("0.000000", CultureInfo.InvariantCulture);

    private sealed record QuickViewTemplate(
        IReadOnlyList<QuickViewTemplateLine> Lines,
        QuickView0 QuickView0,
        int YKeyCount,
        int ZKeyCount);

    private sealed record QuickViewTemplateLine(string Key, string Value);
}

internal sealed record QuickViewPrefsTransferPlan(
    QuickView0 QuickView0,
    int KeyCount,
    int YKeyCount,
    int ZKeyCount,
    bool Changed);
