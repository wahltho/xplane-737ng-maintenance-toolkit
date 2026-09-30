using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LevelUp.NavTableUpdater.Core.Diagnostics;

internal sealed class DiagnosticPrivacyFilter(bool anonymizePaths)
{
    private readonly Dictionary<string, string> _paths = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);
    private static readonly Regex Url = new(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase, RegexTimeout);
    private static readonly Regex AbsolutePath = new(
        @"(?<![\w/\]])(?:[A-Za-z]:[\\/]|\\\\[^\s\\/]+[\\/]|/)[^\r\n""'<>|;,]*",
        RegexOptions.None, RegexTimeout);
    private static readonly Regex Credential = new(
        @"(?i)(\b(?:authorization|access_token|token|api_key|apikey|password|secret|logon)[""']?\s*[:=]\s*)(?:""[^""\r\n]*""|'[^'\r\n]*'|(?:Bearer\s+)?[^\s&,;]+)",
        RegexOptions.None, RegexTimeout);

    public void AddPath(string? path, string alias)
    {
        if (string.IsNullOrWhiteSpace(path) || path is "-" or "/" or "\\") return;
        var trimmed = path.TrimEnd('/', '\\');
        if (trimmed.Length < 3) return;
        _paths.TryAdd(trimmed, alias);
        _paths.TryAdd(trimmed.Replace('\\', '/'), alias);
    }

    public string Filter(string text)
    {
        // Credentials are removed even when the user chooses to keep local paths.
        text = Credential.Replace(text, "$1[REDACTED]");
        var urls = new List<string>();
        text = Url.Replace(text, match =>
        {
            var url = match.Value;
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.UserInfo))
                url = url.Replace(uri.UserInfo + "@", "[REDACTED]@", StringComparison.Ordinal);
            urls.Add(url);
            return $"\u0001URL{urls.Count - 1}\u0001";
        });
        if (anonymizePaths)
        {
            foreach (var pair in _paths.OrderByDescending(p => p.Key.Length))
            {
                // The boundary avoids confusing /Aircraft/LU with /Aircraft/LU-copy.
                text = Regex.Replace(text, Regex.Escape(pair.Key) + @"(?=$|[\\/\s""'<>|;,:.)\]])",
                    _ => pair.Value, RegexOptions.IgnoreCase, RegexTimeout);
            }
            // Cover paths mentioned only in exception messages or pasted log text.
            text = AbsolutePath.Replace(text, "[LOCAL_PATH]");
        }
        for (var i = 0; i < urls.Count; i++)
            text = text.Replace($"\u0001URL{i}\u0001", urls[i], StringComparison.Ordinal);
        return text;
    }

    public void FilterJson(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(p => p.Key).ToArray())
            {
                if (obj[key] is JsonValue value && value.TryGetValue<string>(out var text)) obj[key] = Filter(text);
                else if (obj[key] is { } child) FilterJson(child);
            }
        }
        else if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] is JsonValue value && value.TryGetValue<string>(out var text)) array[i] = Filter(text);
                else if (array[i] is { } child) FilterJson(child);
            }
        }
    }
}
