using LevelUp.NavTableUpdater.Core.Manifest;

namespace LevelUp.NavTableUpdater.Core.Content;

internal static class ContentPatchPathSafety
{
    public static string ResolveTarget(string rootPath, string relativePath, string label)
    {
        DeclarativePatchManifestParser.ValidateRelativePath(relativePath, label);
        var root = Path.GetFullPath(rootPath);
        var parts = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var target = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!target.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, comparison))
        {
            throw new InvalidOperationException($"{label} escapes its root: {relativePath}.");
        }

        var current = root;
        foreach (var part in parts)
        {
            current = Path.Combine(current, part);
            FileSystemInfo? item = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : File.Exists(current) ? new FileInfo(current) : null;
            if (item?.LinkTarget is not null)
            {
                throw new InvalidOperationException($"{label} traverses a nested symbolic link: {relativePath}.");
            }
        }

        return target;
    }
}
