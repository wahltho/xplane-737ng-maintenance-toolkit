using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Tools;

namespace LevelUp.NavTableUpdater.Core.State;

internal static class AircraftMoveStateRebaser
{
    internal static StringComparison Comparison => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    internal static bool Within(string path, string root) => !string.IsNullOrWhiteSpace(path)
        && (Same(path, root) || FullPath(path).StartsWith(Path.EndsInDirectorySeparator(FullPath(root))
            ? FullPath(root) : FullPath(root) + Path.DirectorySeparatorChar, Comparison));
    internal static bool Same(string a, string b) => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
        && string.Equals(FullPath(a), FullPath(b), Comparison);

    internal static string FullPath(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        // macOS exposes these system paths through aliases. User-created links remain forbidden.
        if (OperatingSystem.IsMacOS())
            foreach (var alias in new[] { "/var", "/tmp" })
                if (full == alias || full.StartsWith(alias + "/", StringComparison.Ordinal))
                    return "/private" + full;
        return full;
    }

    public static ToolStateDocument Rebase(ToolStateDocument original, string source, string destination)
    {
        var state = JsonSerializer.Deserialize<ToolStateDocument>(JsonSerializer.Serialize(original))!;
        string Map(string path) => Within(path, source)
            ? Path.GetFullPath(Path.Combine(destination, Path.GetRelativePath(source, FullPath(path)))) : path;
        void Components(IEnumerable<ContentComponentState> components)
        {
            foreach (var component in components)
            foreach (var file in component.Files)
            {
                file.TargetPath = Map(file.TargetPath);
                file.BackupPath = Map(file.BackupPath);
            }
        }
        void Backups(IEnumerable<BackupRecord> backups)
        {
            foreach (var backup in backups)
            {
                backup.SourcePath = Map(backup.SourcePath);
                backup.BackupPath = Map(backup.BackupPath);
                if (backup.AircraftContentGeneration is not { } generation) continue;
                Components(generation.InstallationComponents.Values);
                Components(generation.ProductComponents.Values);
            }
        }
        static Dictionary<string, T> Remap<T>(Dictionary<string, T> items, Func<string, T, string> map)
        {
            var result = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (var pair in items)
                if (!result.TryAdd(map(pair.Key, pair.Value), pair.Value))
                    throw new InvalidOperationException("The destination already has a Toolkit ownership record.");
            return result;
        }
        // Reject orphaned ownership at the destination as well as key collisions.
        var paths = state.Aircraft.Values.SelectMany(a => new[] { a.AircraftFolder, a.AcfPath, a.PrefsPath })
            .Concat(state.ContentInstallations.Values.Select(a => a.AircraftFolder))
            .Concat(state.ToolInstallations.Values.SelectMany(t => new[] { t.XPlaneRoot, t.TargetPath }))
            .Concat(state.ResourceInstallations.Values.Concat(state.LiveryInstallations.Values)
                .SelectMany(r => new[] { r.TargetPath, r.DestinationDirectory }));
        if (paths.Any(p => Within(p, destination)))
            throw new InvalidOperationException("The destination has recorded installation history. Choose an unused folder.");

        var movedTools = state.ToolInstallations.Values.Where(t => Within(t.TargetPath, source) || Within(t.XPlaneRoot, source)).ToArray();
        var sourceXPlane = XPlaneInstallationLocator.Resolve(source);
        var targetXPlane = XPlaneInstallationLocator.Resolve(Path.GetDirectoryName(destination));
        static IEnumerable<ToolInstalledDependencyState> Dependencies(ToolInstallationState t) =>
            t.Dependencies.Concat(t.Backups.SelectMany(b => b.PreviousDependencies));
        if (!Same(sourceXPlane ?? "", targetXPlane ?? "") &&
            (movedTools.Any(t => Dependencies(t).Any(d => !Within(d.InstallationRoot, source)))
             || state.ToolInstallations.Values.Except(movedTools)
                 .Any(t => Dependencies(t).Any(d => Within(d.InstallationRoot, source)))))
            throw new InvalidOperationException("This component depends on a plugin outside the aircraft folder. Move within the same X-Plane installation.");

        state.Aircraft = Remap(state.Aircraft, (key, a) =>
        {
            var folder = string.IsNullOrWhiteSpace(a.AircraftFolder) ? Path.GetDirectoryName(a.AcfPath) ?? "" : a.AircraftFolder;
            var moved = Within(folder, source);
            a.AircraftFolder = Map(a.AircraftFolder); a.AcfPath = Map(a.AcfPath); a.PrefsPath = Map(a.PrefsPath);
            Components(a.ContentComponents.Values); Backups(a.Backups);
            if (!moved) return key;
            if (!string.IsNullOrWhiteSpace(a.AcfPath)) return ToolStateStore.PathKey(a.AcfPath);
            var family = AircraftProductIds.Normalize(a.AircraftId)
                ?? throw new InvalidDataException("The recorded aircraft product is unknown.");
            return ToolStateStore.ProductTargetKey(family, Map(folder));
        });
        state.ContentInstallations = Remap(state.ContentInstallations, (key, i) =>
        {
            var moved = Within(i.AircraftFolder, source);
            i.AircraftFolder = Map(i.AircraftFolder); Components(i.ContentComponents.Values); Backups(i.Backups);
            return moved ? ToolStateStore.PathKey(i.AircraftFolder) : key;
        });
        state.ToolInstallations = Remap(state.ToolInstallations, (key, t) =>
        {
            var movedRoot = Within(t.XPlaneRoot, source);
            t.XPlaneRoot = Map(t.XPlaneRoot); t.TargetPath = Map(t.TargetPath);
            foreach (var d in t.Dependencies) d.InstallationRoot = Map(d.InstallationRoot);
            foreach (var b in t.Backups)
            {
                b.BackupPath = Map(b.BackupPath);
                foreach (var d in b.PreviousDependencies) d.InstallationRoot = Map(d.InstallationRoot);
            }
            return movedRoot ? ToolStateStore.ToolKey(t.XPlaneRoot, t.PackageId) : key;
        });
        foreach (var r in state.ResourceInstallations.Values)
        { r.DestinationDirectory = Map(r.DestinationDirectory); r.TargetPath = Map(r.TargetPath); }
        state.LiveryInstallations = Remap(state.LiveryInstallations, (key, r) =>
        {
            var root = Path.GetDirectoryName(r.DestinationDirectory) ?? "";
            var moved = Within(root, source);
            r.DestinationDirectory = Map(r.DestinationDirectory); r.TargetPath = Map(r.TargetPath);
            return moved ? ToolStateStore.LiveryKey(Map(root), r.PackageId) : key;
        });
        return state;
    }
}
