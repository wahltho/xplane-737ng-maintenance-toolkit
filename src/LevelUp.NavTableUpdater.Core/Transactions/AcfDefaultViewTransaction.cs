using System.Globalization;
using LevelUp.NavTableUpdater.Core.Aircraft;

namespace LevelUp.NavTableUpdater.Core.Transactions;

internal static class AcfDefaultViewTransaction
{
    public static void Apply(string acfPath, DefaultView defaultView, string backupPath)
    {
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["acf/_pe_xyz/0"] = Format(defaultView.XFeet),
            ["acf/_pe_xyz/1"] = Format(defaultView.YFeet),
            ["acf/_pe_xyz/2"] = Format(defaultView.ZFeet),
            ["acf/_ang_offset/0,1"] = Format(defaultView.PitchDegrees)
        };

        TextFileRewrite.Apply(
            acfPath,
            backupPath,
            document => Rewrite(document, replacements),
            tempPath =>
            {
                var validation = AircraftFileParser.ReadAcfMetadata(tempPath).DefaultView
                    ?? throw new InvalidOperationException("Rewritten ACF default-view fields could not be validated.");
                if (!NearlyEqual(validation.XFeet, defaultView.XFeet)
                    || !NearlyEqual(validation.YFeet, defaultView.YFeet)
                    || !NearlyEqual(validation.ZFeet, defaultView.ZFeet)
                    || !NearlyEqual(validation.PitchDegrees, defaultView.PitchDegrees))
                {
                    throw new InvalidOperationException("Rewritten ACF default-view values do not match the requested values.");
                }
            });
    }

    private static string Format(double value) => value.ToString("0.000000000", CultureInfo.InvariantCulture);

    private static bool NearlyEqual(double left, double right) => Math.Abs(left - right) <= 0.000001;

    private static TextDocument Rewrite(TextDocument document, IReadOnlyDictionary<string, string> replacements)
    {
        var counts = replacements.Keys.ToDictionary(key => key, _ => 0, StringComparer.Ordinal);
        var newLines = new List<TextLine>(document.Lines.Count);

        foreach (var line in document.Lines)
        {
            var parts = line.Body.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 3 && parts[0] == "P" && replacements.TryGetValue(parts[1], out var replacement))
            {
                counts[parts[1]]++;
                newLines.Add(line with { Body = $"P {parts[1]} {replacement}" });
            }
            else
            {
                newLines.Add(line);
            }
        }

        var invalidKeys = counts.Where(pair => pair.Value != 1).Select(pair => $"{pair.Key}={pair.Value}").ToArray();
        if (invalidKeys.Length > 0)
        {
            throw new InvalidOperationException($"Default-view ACF keys must be unique before rewriting ({string.Join(", ", invalidKeys)}).");
        }

        return document.WithLines(newLines);
    }
}
