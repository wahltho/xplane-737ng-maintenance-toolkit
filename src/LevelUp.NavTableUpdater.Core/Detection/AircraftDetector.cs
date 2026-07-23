using LevelUp.NavTableUpdater.Core.Aircraft;

namespace LevelUp.NavTableUpdater.Core.Detection;

public sealed class AircraftDetector
{
    private const string TargetRelativePath = "plugins/xlua/scripts/B738.a_fms/B738.a_fms.lua";
    private readonly string? homeDirectory;

    public AircraftDetector()
        : this(homeDirectory: null)
    {
    }

    public AircraftDetector(string? homeDirectory)
    {
        this.homeDirectory = homeDirectory;
    }

    public IReadOnlyList<AircraftCandidate> FindCandidates(IEnumerable<string>? additionalSearchRoots = null)
    {
        var candidates = new Dictionary<string, AircraftCandidate>(PathKeyComparer);

        foreach (var root in (additionalSearchRoots ?? []).Concat(CommonAircraftRoots(homeDirectory)))
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var searchRoot in ExpandSearchRoot(root))
            {
                if (!Directory.Exists(searchRoot))
                {
                    continue;
                }

                foreach (var directory in EnumerateLikelyAircraftFolders(searchRoot, maxDepth: 3))
                {
                    var targetScript = Path.Combine(directory, TargetRelativePath);
                    var hasLuaTarget = File.Exists(targetScript);
                    var hasPortNoLuaLayout = LooksLikePortNoLuaLevelUp(directory);
                    var recognizedReferences = FindRecognizedReferences(directory);
                    if (!hasLuaTarget && !hasPortNoLuaLayout && recognizedReferences.Length == 0)
                    {
                        continue;
                    }

                    var fullPath = Path.GetFullPath(directory);
                    var canonicalPath = ResolveCanonicalDirectoryPath(fullPath);
                    var reason = hasLuaTarget
                        ? "Found XLua B738.a_fms target script."
                        : hasPortNoLuaLayout
                            ? "Found LevelUp port/no-Lua layout; Lua package is not applicable."
                            : $"Found supported 737NG aircraft: {string.Join(", ", recognizedReferences)}.";
                    candidates.TryAdd(
                        canonicalPath,
                        new AircraftCandidate(
                            Name: Path.GetFileName(fullPath),
                            Path: fullPath,
                            Reason: reason));
                }
            }
        }

        return candidates.Values.OrderBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<string> CommonAircraftRoots(string? configuredHomeDirectory)
    {
        var home = string.IsNullOrWhiteSpace(configuredHomeDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : configuredHomeDirectory;
        if (string.IsNullOrWhiteSpace(home))
        {
            yield break;
        }

        foreach (var installRoot in ReadXPlaneInstallRoots(home))
        {
            yield return installRoot;
        }

        yield return Path.Combine(home, "X-Plane 12", "Aircraft");
        yield return Path.Combine(home, "X-Plane 11", "Aircraft");
        yield return Path.Combine(home, ".steam", "steam", "steamapps", "common", "X-Plane 12", "Aircraft");
        yield return Path.Combine(home, ".steam", "steam", "steamapps", "common", "X-Plane 11", "Aircraft");
        yield return Path.Combine(home, ".local", "share", "Steam", "steamapps", "common", "X-Plane 12", "Aircraft");
        yield return Path.Combine(home, ".local", "share", "Steam", "steamapps", "common", "X-Plane 11", "Aircraft");
        yield return Path.Combine(home, "Library", "Application Support", "Steam", "steamapps", "common", "X-Plane 12", "Aircraft");
        yield return Path.Combine(home, "Library", "Application Support", "Steam", "steamapps", "common", "X-Plane 11", "Aircraft");
    }

    private static IEnumerable<string> ReadXPlaneInstallRoots(string home)
    {
        var installFiles = new[]
        {
            Path.Combine(home, ".x-plane", "x-plane_install_12.txt"),
            Path.Combine(home, ".x-plane", "x-plane_install_11.txt"),
            Path.Combine(home, "Library", "Preferences", "x-plane_install_12.txt"),
            Path.Combine(home, "Library", "Preferences", "x-plane_install_11.txt")
        };

        foreach (var installFile in installFiles)
        {
            if (!File.Exists(installFile))
            {
                continue;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(installFile);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var line in lines)
            {
                var path = line.Trim();
                if (path.Length == 0 || path.StartsWith('#'))
                {
                    continue;
                }

                yield return path;
            }
        }
    }

    private static IEnumerable<string> ExpandSearchRoot(string root)
    {
        yield return root;

        var aircraftRoot = Path.Combine(root, "Aircraft");
        if (!PathsEqual(root, aircraftRoot))
        {
            yield return aircraftRoot;
        }
    }

    private static IEnumerable<string> EnumerateLikelyAircraftFolders(string root, int maxDepth)
    {
        var pending = new Queue<(string Path, int Depth)>();
        pending.Enqueue((root, 0));

        while (pending.Count > 0)
        {
            var (path, depth) = pending.Dequeue();
            yield return path;

            if (depth >= maxDepth)
            {
                continue;
            }

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(path);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children)
            {
                pending.Enqueue((child, depth + 1));
            }
        }
    }

    private static bool LooksLikePortNoLuaLevelUp(string aircraftPath)
    {
        var hasLevelUpAcfs = File.Exists(Path.Combine(aircraftPath, "737_70NG.acf"))
            || File.Exists(Path.Combine(aircraftPath, "737_9ENG.acf"));
        var hasPortPlugin = Directory.Exists(Path.Combine(aircraftPath, "plugins", "zibomod"));

        return hasLevelUpAcfs && hasPortPlugin;
    }

    private static string[] FindRecognizedReferences(string aircraftPath)
    {
        var recognized = new List<string>();

        foreach (var reference in AircraftReferenceCatalog.All)
        {
            var acfPath = Path.Combine(aircraftPath, reference.AcfFileName);
            if (!File.Exists(acfPath))
            {
                continue;
            }

            try
            {
                var metadata = AircraftFileParser.ReadAcfMetadata(acfPath);
                var maintenanceMetadata = AircraftFileParser.ReadMaintenanceMetadata(aircraftPath, out _);
                if (AircraftReferenceCatalog.MatchesProductIdentity(reference, metadata, maintenanceMetadata))
                {
                    recognized.Add(reference.DisplayName);
                }
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
        }

        return recognized.ToArray();
    }

    private static StringComparer PathKeyComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static string ResolveCanonicalDirectoryPath(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!Directory.Exists(fullPath))
            {
                return fullPath;
            }

            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(root))
            {
                return fullPath;
            }

            var current = root;
            var relativePath = Path.GetRelativePath(root, fullPath);
            if (relativePath == ".")
            {
                return Path.GetFullPath(current);
            }

            foreach (var segment in relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            {
                if (string.IsNullOrWhiteSpace(segment) || segment == ".")
                {
                    continue;
                }

                var next = Path.Combine(current, segment);
                var directory = new DirectoryInfo(next);
                if (directory.LinkTarget is not null)
                {
                    var target = directory.ResolveLinkTarget(returnFinalTarget: true);
                    if (target is DirectoryInfo targetDirectory)
                    {
                        next = targetDirectory.FullName;
                    }
                }

                current = Path.GetFullPath(next);
            }

            return Path.GetFullPath(current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Path.GetFullPath(path);
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(left, right, StringComparison.Ordinal);
        }
    }
}
