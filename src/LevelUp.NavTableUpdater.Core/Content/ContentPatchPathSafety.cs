using LevelUp.NavTableUpdater.Core.Manifest;
using System.Security.Cryptography;

namespace LevelUp.NavTableUpdater.Core.Content;

internal static class ContentPatchPathSafety
{
    public static ContentPatchScopeSnapshot CaptureFlatScope(string rootPath, string relativePath)
    {
        var directory = ResolveTarget(rootPath, relativePath, "Managed scope");
        if (File.Exists(directory))
            throw new InvalidOperationException($"Managed scope is occupied by a file: {relativePath}.");
        if (!Directory.Exists(directory))
            return new(relativePath, false, new Dictionary<string, string>(StringComparer.Ordinal));

        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var casing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            var name = Path.GetFileName(entry);
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0
                || !casing.Add(name))
                throw new InvalidOperationException($"Managed scope contains a link, directory or case-colliding entry: {relativePath}/{name}.");
            var bytes = File.ReadAllBytes(entry);
            files.Add(name, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
        return new(relativePath, true, files);
    }

    public static bool ScopeMatches(ContentPatchScopeSnapshot actual, ContentPatchScopeSnapshot expected) =>
        actual.DirectoryExisted == expected.DirectoryExisted
        && actual.FileHashes.Count == expected.FileHashes.Count
        && actual.FileHashes.All(pair => expected.FileHashes.TryGetValue(pair.Key, out var hash)
            && string.Equals(pair.Value, hash, StringComparison.OrdinalIgnoreCase));

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
            var linked = new FileInfo(current).LinkTarget is not null
                || new DirectoryInfo(current).LinkTarget is not null;
            if (linked)
            {
                throw new InvalidOperationException($"{label} traverses a nested symbolic link: {relativePath}.");
            }
        }

        return target;
    }
}
