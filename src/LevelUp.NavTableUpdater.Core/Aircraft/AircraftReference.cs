using LevelUp.NavTableUpdater.Core.Upstream;

namespace LevelUp.NavTableUpdater.Core.Aircraft;

public sealed record AircraftReference(
    string AircraftId,
    string DisplayName,
    string Family,
    string AcfFileName,
    string PrefsFileName,
    string Source,
    string SourceRef,
    string SourceVersion,
    string ExpectedName,
    string ExpectedDescription,
    string ExpectedStudioContains,
    string? ExpectedVersionTxt,
    string? ExpectedAcfVersion,
    string? ExpectedFileWriterVersion,
    double ReferenceCgYFeet,
    double ReferenceCgZFeet);

public sealed record AircraftReferenceCgRange(
    string AircraftId,
    AircraftUpstreamVersion? FromVersion,
    AircraftUpstreamVersion? ToVersion,
    string SourceRef,
    string SourceVersion,
    double ReferenceCgYFeet,
    double ReferenceCgZFeet,
    IReadOnlyList<string> SourceRefs,
    IReadOnlyList<string> ReleaseTags,
    IReadOnlyList<string> AcfVersions)
{
    public bool Contains(AircraftUpstreamVersion version) =>
        FromVersion is not null
        && ToVersion is not null
        && version >= FromVersion
        && version <= ToVersion;

    public bool MatchesSourceRef(string? sourceRef) =>
        MatchesToken(sourceRef, SourceRefs);

    public bool MatchesReleaseTag(string? releaseTag) =>
        MatchesToken(releaseTag, ReleaseTags);

    public bool MatchesAcfVersion(string? acfVersion) =>
        MatchesToken(acfVersion, AcfVersions);

    private static bool MatchesToken(string? value, IReadOnlyList<string> candidates)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var valueTokens = ExpandToken(value);
        foreach (var candidate in candidates.SelectMany(ExpandToken))
        {
            if (valueTokens.Any(token => TokenEquals(token, candidate)))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> ExpandToken(string value)
    {
        var normalized = value.Trim();
        var tokens = new List<string> { normalized };

        var atIndex = normalized.LastIndexOf('@');
        if (atIndex >= 0 && atIndex < normalized.Length - 1)
        {
            tokens.Add(normalized[(atIndex + 1)..]);
        }

        var separators = new[] { ' ', '\t', '/', '\\' };
        foreach (var separator in separators)
        {
            var index = normalized.LastIndexOf(separator);
            if (index >= 0 && index < normalized.Length - 1)
            {
                tokens.Add(normalized[(index + 1)..]);
            }
        }

        return tokens
            .Select(token => token.Trim())
            .Where(token => token.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TokenEquals(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IsCommitToken(left) || !IsCommitToken(right))
        {
            return false;
        }

        return left.StartsWith(right, StringComparison.OrdinalIgnoreCase)
            || right.StartsWith(left, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCommitToken(string token) =>
        token.Length >= 7 && token.All(Uri.IsHexDigit);
}
