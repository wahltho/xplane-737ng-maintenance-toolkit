using LevelUp.NavTableUpdater.Core.Tools;

namespace LevelUp.NavTableUpdater.Core.Upstream;

public static class AircraftFreshInstallDestination
{
    public static string? GetValidationError(string xPlaneRoot, string targetFolder)
    {
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(xPlaneRoot));
            if (!XPlaneInstallationLocator.LooksLikeXPlaneRoot(root))
                return "The selected path is not a structurally valid X-Plane installation.";

            var aircraftRoot = Path.Combine(root, "Aircraft");
            var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetFolder));
            if (!IsDescendant(aircraftRoot, target))
                return "The new destination must be a folder under X-Plane 12/Aircraft.";

            var parent = Path.GetDirectoryName(target)!;
            if (!Directory.Exists(parent))
                return "The destination parent folder must already exist. Create grouping folders such as Aircraft/Boeing first.";

            var error = GetParentValidationError(aircraftRoot, parent);
            if (error is not null) return error;

            var destination = new DirectoryInfo(target);
            if (destination.LinkTarget is not null)
                return "The destination cannot be a symbolic link or junction.";
            if (Directory.Exists(target) || File.Exists(target))
                return "The destination already exists. Select and update that aircraft instead, or choose a new folder name.";

            return null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException
            or UnauthorizedAccessException or InvalidOperationException)
        {
            return $"The aircraft destination cannot be validated: {ex.Message}";
        }
    }

    internal static string? GetParentValidationError(string aircraftRoot, string parent)
    {
        if (!PathsEqual(aircraftRoot, parent) && !IsDescendant(aircraftRoot, parent))
            return "The destination parent must be under X-Plane 12/Aircraft.";

        for (var current = parent; !PathsEqual(current, aircraftRoot); current = Path.GetDirectoryName(current)!)
        {
            var directory = new DirectoryInfo(current);
            if (!directory.Exists || directory.LinkTarget is not null
                || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                return "Destination grouping folders must exist and cannot be symbolic links or junctions.";
            if (Directory.EnumerateFiles(current).Any(path =>
                    Path.GetExtension(path).Equals(".acf", StringComparison.OrdinalIgnoreCase)))
                return "A new aircraft cannot be installed inside an existing aircraft folder. Choose a separate grouping folder.";
        }

        var physicalRoot = AircraftUpdatePath.ResolvePhysicalPath(aircraftRoot);
        var physicalParent = AircraftUpdatePath.ResolvePhysicalPath(parent);
        return PathsEqual(physicalRoot, physicalParent) || IsDescendant(physicalRoot, physicalParent)
            ? null
            : "The destination resolves outside X-Plane 12/Aircraft.";
    }

    internal static bool PathsEqual(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(left), Path.TrimEndingDirectorySeparator(right), Comparison);

    private static bool IsDescendant(string root, string path) =>
        path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, Comparison);

    private static StringComparison Comparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
