using System.Text.RegularExpressions;

namespace LevelUp.NavTableUpdater.Core.Tools;

public static partial class ToolPackageVersion
{
    public static bool IsValid(string value) => TryParse(value, out _);

    public static bool IsValidMinimum(string value) => MinimumVersionPattern().IsMatch(value.Trim().TrimStart('v', 'V'));

    public static bool IsAtLeast(string actual, string minimum) =>
        TryParse(actual, out _)
        && (string.IsNullOrWhiteSpace(minimum) || Compare(actual, minimum) >= 0);

    public static int Compare(string left, string right)
    {
        if (!TryParse(left, out var leftVersion) || !TryParse(right, out var rightVersion))
        {
            throw new InvalidDataException($"Tool package versions cannot be compared: '{left}' and '{right}'.");
        }

        var numeric = leftVersion.Major.CompareTo(rightVersion.Major);
        if (numeric == 0) numeric = leftVersion.Minor.CompareTo(rightVersion.Minor);
        if (numeric == 0) numeric = leftVersion.Patch.CompareTo(rightVersion.Patch);
        if (numeric != 0) return numeric;

        if (leftVersion.Suffix.Length == 0 || rightVersion.Suffix.Length == 0)
        {
            return leftVersion.Suffix.Length == rightVersion.Suffix.Length
                ? 0
                : leftVersion.Suffix.Length == 0 ? 1 : -1;
        }

        return CompareSuffixes(leftVersion.Suffix, rightVersion.Suffix);
    }

    public static bool TryParse(string value, out ComparableVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var match = VersionPattern().Match(value.Trim().TrimStart('v', 'V'));
        if (!match.Success
            || !int.TryParse(match.Groups["major"].Value, out var major)
            || !TryParsePart(match.Groups["minor"].Value, out var minor)
            || !TryParsePart(match.Groups["patch"].Value, out var patch))
        {
            return false;
        }

        version = new ComparableVersion(
            major,
            minor,
            patch,
            match.Groups["suffix"].Value.TrimStart('-', '.', '_'));
        return true;
    }

    private static bool TryParsePart(string value, out int result)
    {
        if (value.Length == 0)
        {
            result = 0;
            return true;
        }

        return int.TryParse(value, out result);
    }

    private static int CompareSuffixes(string left, string right)
    {
        var leftParts = SuffixPartPattern().Matches(left);
        var rightParts = SuffixPartPattern().Matches(right);
        for (var index = 0; index < Math.Min(leftParts.Count, rightParts.Count); index++)
        {
            var leftPart = leftParts[index].Value;
            var rightPart = rightParts[index].Value;
            var leftNumeric = int.TryParse(leftPart, out var leftNumber);
            var rightNumeric = int.TryParse(rightPart, out var rightNumber);
            var numeric = leftNumeric && rightNumeric;
            var comparison = numeric
                ? leftNumber.CompareTo(rightNumber)
                : StringComparer.OrdinalIgnoreCase.Compare(leftPart, rightPart);
            if (comparison != 0) return comparison;
        }

        return leftParts.Count.CompareTo(rightParts.Count);
    }

    [GeneratedRegex(@"^(?<major>\d+)(?:\.(?<minor>\d+))?(?:\.(?<patch>\d+))?(?<suffix>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"^\d+(?:\.\d+){0,2}(?:[-_]?[A-Za-z][0-9A-Za-z.-]*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex MinimumVersionPattern();

    [GeneratedRegex(@"\d+|\D+", RegexOptions.CultureInvariant)]
    private static partial Regex SuffixPartPattern();

    public readonly record struct ComparableVersion(int Major, int Minor, int Patch, string Suffix);
}
