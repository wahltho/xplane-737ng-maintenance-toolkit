namespace LevelUp.NavTableUpdater.Core.Upstream;

internal static class AircraftUpdateLocalContentPolicy
{
    private static readonly string[] ProtectedFileNames =
    [
        "b738_config.txt",
        "b738x.cfg"
    ];

    public static bool IsProtectedLocalFile(string relativePath)
    {
        var fileName = Path.GetFileName(relativePath);
        if (ProtectedFileNames.Any(name => string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (fileName.EndsWith("_prefs.txt", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("_vrconfig.txt", StringComparison.OrdinalIgnoreCase)
            || (fileName.StartsWith("X-Camera_", StringComparison.OrdinalIgnoreCase)
                && fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var normalized = relativePath.Replace('\\', '/');
        return normalized.StartsWith("Output/preferences/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/Output/preferences/", StringComparison.OrdinalIgnoreCase);
    }

    public static string? GetLiveryRoot(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 || !string.Equals(segments[0], "liveries", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return $"{segments[0]}{Path.DirectorySeparatorChar}{segments[1]}";
    }

    public static bool IsLiveryPath(string relativePath) => GetLiveryRoot(relativePath) is not null;

    public static bool CouldContainProtectedLocalContent(string relativePath)
    {
        if (IsProtectedLocalFile(relativePath))
        {
            return true;
        }

        var normalized = relativePath.Replace('\\', '/').Trim('/');
        return normalized.Equals("liveries", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("liveries/", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Output", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Output/", StringComparison.OrdinalIgnoreCase);
    }

    public static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
