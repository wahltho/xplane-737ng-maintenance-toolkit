namespace LevelUp.NavTableUpdater.Core.Upstream;

internal static class AircraftUpdatePath
{
    private static readonly HashSet<string> KnownAircraftRootEntries = new(StringComparer.OrdinalIgnoreCase)
    {
        "cockpit_3d",
        "cockpit",
        "airfoils",
        "fmod",
        "liveries",
        "objects",
        "plugins",
        "sounds"
    };

    public static string? NormalizeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var normalized = path.Replace('\\', '/').Trim();
        if (normalized.Length == 0 || normalized.StartsWith('/') || Path.IsPathRooted(normalized))
        {
            return null;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            return null;
        }

        return string.Join(Path.DirectorySeparatorChar, segments);
    }

    public static string? MapArchivePath(string archivePath, string? contentRoot)
    {
        var normalizedArchivePath = NormalizeRelativePath(archivePath);
        if (normalizedArchivePath is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(contentRoot))
        {
            return normalizedArchivePath;
        }

        var normalizedRoot = NormalizeRelativePath(contentRoot);
        if (normalizedRoot is null)
        {
            return null;
        }

        var archiveSegments = normalizedArchivePath.Split(Path.DirectorySeparatorChar);
        var rootSegments = normalizedRoot.Split(Path.DirectorySeparatorChar);
        if (archiveSegments.Length <= rootSegments.Length)
        {
            return null;
        }

        for (var index = 0; index < rootSegments.Length; index++)
        {
            if (!string.Equals(archiveSegments[index], rootSegments[index], StringComparison.Ordinal))
            {
                return null;
            }
        }

        return string.Join(Path.DirectorySeparatorChar, archiveSegments[rootSegments.Length..]);
    }

    public static string? DetectContentRoot(
        AircraftUpdatePackage package,
        IReadOnlyList<AircraftPackageArchiveEntry> archiveEntries)
    {
        if (!string.IsNullOrWhiteSpace(package.Manifest?.ContentRoot))
        {
            return package.Manifest.ContentRoot;
        }

        var paths = archiveEntries
            .Where(entry => !entry.IsDirectory)
            .Select(entry => NormalizeRelativePath(entry.Path))
            .Where(path => path is not null)
            .Select(path => path!)
            .Where(path => !path.StartsWith("__MACOSX" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (paths.Length == 0)
        {
            return null;
        }

        var splitPaths = paths
            .Select(path => path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            .ToArray();
        if (splitPaths.Any(segments => segments.Length < 2))
        {
            return null;
        }

        var firstSegment = splitPaths[0][0];
        if (KnownAircraftRootEntries.Contains(firstSegment)
            || splitPaths.Any(segments => !string.Equals(segments[0], firstSegment, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return firstSegment;
    }

    public static string ResolveTargetPath(string targetRoot, string relativePath)
    {
        var normalizedPath = NormalizeRelativePath(relativePath)
            ?? throw new InvalidOperationException($"Unsafe aircraft update path: {relativePath}");
        var fullRoot = Path.GetFullPath(targetRoot);
        var targetPath = Path.GetFullPath(Path.Combine(fullRoot, normalizedPath));
        var resolvedRoot = ResolveExistingLinks(fullRoot);
        var resolvedTarget = ResolveExistingLinks(targetPath);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!resolvedTarget.StartsWith(resolvedRoot + Path.DirectorySeparatorChar, comparison)
            && !string.Equals(resolvedTarget, resolvedRoot, comparison))
        {
            throw new InvalidOperationException($"Aircraft update path resolves outside the aircraft folder through a symbolic link: {relativePath}");
        }

        return targetPath;
    }

    public static string ResolvePhysicalPath(string path) => ResolveExistingLinks(Path.GetFullPath(path));

    private static string ResolveExistingLinks(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)
            ?? throw new InvalidOperationException($"Aircraft update path has no filesystem root: {path}");
        var segments = fullPath[root.Length..]
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        var current = root;

        for (var index = 0; index < segments.Length; index++)
        {
            var candidate = Path.Combine(current, segments[index]);
            var fileSystemInfo = GetExistingFileSystemInfo(candidate);
            if (fileSystemInfo is null)
            {
                return Path.GetFullPath(Path.Combine(current, Path.Combine(segments[index..])));
            }

            try
            {
                var resolvedLink = fileSystemInfo.LinkTarget is null
                    ? null
                    : fileSystemInfo.ResolveLinkTarget(returnFinalTarget: true);
                current = resolvedLink is null ? candidate : Path.GetFullPath(resolvedLink.FullName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                throw new InvalidOperationException($"Aircraft update path contains an unreadable symbolic link: {candidate}", ex);
            }
        }

        return Path.GetFullPath(current);
    }

    private static FileSystemInfo? GetExistingFileSystemInfo(string path)
    {
        var directory = new DirectoryInfo(path);
        if (directory.Exists || directory.LinkTarget is not null)
        {
            return directory;
        }

        var file = new FileInfo(path);
        return file.Exists || file.LinkTarget is not null ? file : null;
    }
}
