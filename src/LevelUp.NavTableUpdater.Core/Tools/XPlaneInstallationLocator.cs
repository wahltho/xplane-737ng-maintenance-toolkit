namespace LevelUp.NavTableUpdater.Core.Tools;

public static class XPlaneInstallationLocator
{
    public static string? Resolve(params string?[] pathHints)
    {
        foreach (var hint in pathHints.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(hint!);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            var current = File.Exists(fullPath) ? Path.GetDirectoryName(fullPath) : fullPath;
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (LooksLikeXPlaneRoot(current))
                {
                    return Path.GetFullPath(current);
                }

                var parent = Directory.GetParent(current)?.FullName;
                if (string.IsNullOrWhiteSpace(parent) || PathsEqual(parent, current))
                {
                    break;
                }

                current = parent;
            }
        }

        return null;
    }

    public static bool LooksLikeXPlaneRoot(string path) =>
        Directory.Exists(path)
        && Directory.Exists(Path.Combine(path, "Aircraft"))
        && Directory.Exists(Path.Combine(path, "Resources"));

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
}
