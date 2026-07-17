using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LevelUp.NavTableUpdater.Core.Upstream;

public sealed class ZiboUpstreamFeedParser
{
    public const string Family = "zibo-737-800x";
    public const string DefaultFeedUrl = "https://skymatixva.com/tfiles/feed.xml";

    private static readonly Regex FullPackagePattern =
        new(@"^B737-800X(?:_XP\d+)?_(\d+)_(\d+)_full\.zip$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex PatchPackagePattern =
        new(@"^B738X(?:_XP\d+)?_(\d+)_(\d+)_(\d+)\.zip$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public AircraftUpdateIndex Parse(string feedXml, string sourceUrl = DefaultFeedUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feedXml);

        var document = XDocument.Parse(feedXml);
        var packages = document
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "item", StringComparison.OrdinalIgnoreCase))
            .Select(ParseItem)
            .Where(package => package is not null)
            .Cast<AircraftUpdatePackage>()
            .OrderBy(package => package.Version)
            .ThenBy(package => package.Kind)
            .ToArray();

        return new AircraftUpdateIndex(Family, sourceUrl, packages);
    }

    public static AircraftUpdatePackage? ParseFileName(string fileName, string sourceUrl = "")
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var normalized = Path.GetFileName(fileName.Trim());

        var fullMatch = FullPackagePattern.Match(normalized);
        if (fullMatch.Success)
        {
            return new AircraftUpdatePackage(
                Family,
                AircraftUpdatePackageKind.FullBaseline,
                AircraftUpstreamVersion.FromParts(fullMatch.Groups[1].Value, fullMatch.Groups[2].Value),
                normalized,
                sourceUrl);
        }

        var patchMatch = PatchPackagePattern.Match(normalized);
        if (patchMatch.Success)
        {
            return new AircraftUpdatePackage(
                Family,
                AircraftUpdatePackageKind.CumulativePatch,
                AircraftUpstreamVersion.FromParts(patchMatch.Groups[1].Value, patchMatch.Groups[2].Value, patchMatch.Groups[3].Value),
                normalized,
                sourceUrl);
        }

        return null;
    }

    private static AircraftUpdatePackage? ParseItem(XElement item)
    {
        var title = item.Elements().FirstOrDefault(element => string.Equals(element.Name.LocalName, "title", StringComparison.OrdinalIgnoreCase))?.Value;
        var link = item.Elements().FirstOrDefault(element => string.Equals(element.Name.LocalName, "link", StringComparison.OrdinalIgnoreCase))?.Value;

        return ParseFileName(title ?? "", link?.Trim() ?? "");
    }
}
