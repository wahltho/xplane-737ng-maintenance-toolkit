using System.Globalization;
using System.Text.RegularExpressions;

namespace LevelUp.NavTableUpdater.Core.Upstream;

public readonly record struct AircraftUpstreamVersion(int Major, int Minor, int Patch)
    : IComparable<AircraftUpstreamVersion>
{
    public AircraftUpstreamVersion Baseline => new(Major, Minor, 0);

    public int CompareTo(AircraftUpstreamVersion other)
    {
        var majorComparison = Major.CompareTo(other.Major);
        if (majorComparison != 0)
        {
            return majorComparison;
        }

        var minorComparison = Minor.CompareTo(other.Minor);
        if (minorComparison != 0)
        {
            return minorComparison;
        }

        return Patch.CompareTo(other.Patch);
    }

    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Major}.{Minor:00}.{Patch:00}");

    public string ToBaselineString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Major}.{Minor:00}");

    public static AircraftUpstreamVersion FromParts(string major, string minor, string? patch = null) =>
        new(
            int.Parse(major, CultureInfo.InvariantCulture),
            int.Parse(minor, CultureInfo.InvariantCulture),
            string.IsNullOrWhiteSpace(patch) ? 0 : int.Parse(patch, CultureInfo.InvariantCulture));

    public static bool TryParse(string? value, out AircraftUpstreamVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = Regex.Match(value, @"(?<!\d)(\d+)\.(\d+)(?:\.(\d+))?(?!\d)");
        if (!match.Success)
        {
            return false;
        }

        version = FromParts(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value);
        return true;
    }

    public static bool operator >(AircraftUpstreamVersion left, AircraftUpstreamVersion right) =>
        left.CompareTo(right) > 0;

    public static bool operator <(AircraftUpstreamVersion left, AircraftUpstreamVersion right) =>
        left.CompareTo(right) < 0;

    public static bool operator >=(AircraftUpstreamVersion left, AircraftUpstreamVersion right) =>
        left.CompareTo(right) >= 0;

    public static bool operator <=(AircraftUpstreamVersion left, AircraftUpstreamVersion right) =>
        left.CompareTo(right) <= 0;
}
