namespace LevelUp.NavTableUpdater.Core.Detection;

public sealed class AircraftDetector
{
    private const string TargetRelativePath = "plugins/xlua/scripts/B738.a_fms/B738.a_fms.lua";

    public IReadOnlyList<AircraftCandidate> FindCandidates()
    {
        var candidates = new Dictionary<string, AircraftCandidate>(StringComparer.Ordinal);

        foreach (var root in CommonAircraftRoots())
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var directory in EnumerateLikelyAircraftFolders(root, maxDepth: 3))
            {
                var targetScript = Path.Combine(directory, TargetRelativePath);
                var hasLuaTarget = File.Exists(targetScript);
                var hasPortNoLuaLayout = LooksLikePortNoLuaLevelUp(directory);
                if (!hasLuaTarget && !hasPortNoLuaLayout)
                {
                    continue;
                }

                var fullPath = Path.GetFullPath(directory);
                candidates.TryAdd(
                    fullPath,
                    new AircraftCandidate(
                        Name: Path.GetFileName(fullPath),
                        Path: fullPath,
                        Reason: hasLuaTarget
                            ? "Found XLua B738.a_fms target script."
                            : "Found LevelUp port/no-Lua layout; Lua package is not applicable."));
            }
        }

        return candidates.Values.OrderBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<string> CommonAircraftRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            yield break;
        }

        yield return Path.Combine(home, "X-Plane 12", "Aircraft");
        yield return Path.Combine(home, "X-Plane 11", "Aircraft");
        yield return Path.Combine(home, "Library", "Application Support", "Steam", "steamapps", "common", "X-Plane 12", "Aircraft");
        yield return Path.Combine(home, "Library", "Application Support", "Steam", "steamapps", "common", "X-Plane 11", "Aircraft");
    }

    private static IEnumerable<string> EnumerateLikelyAircraftFolders(string root, int maxDepth)
    {
        var pending = new Queue<(string Path, int Depth)>();
        pending.Enqueue((root, 0));

        while (pending.Count > 0)
        {
            var (path, depth) = pending.Dequeue();
            if (depth > 0)
            {
                yield return path;
            }

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
}
