using System.Runtime.InteropServices;

namespace LevelUp.NavTableUpdater.Core.Platform;

public static class PackagePlatform
{
    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        "win-x64", "win-arm64", "osx-x64", "osx-arm64", "linux-x64", "linux-arm64"
    };

    public static string Current => (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx"
        : OperatingSystem.IsLinux() ? "linux" : "unknown") + "-" + RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();

    // Omitted restrictions keep existing cross-platform tools backwards compatible.
    public static bool Supports(IReadOnlyCollection<string> platforms, string? runtimeIdentifier = null) =>
        platforms.Count == 0 || platforms.Contains(runtimeIdentifier ?? Current, StringComparer.Ordinal);

    public static void Validate(IReadOnlyCollection<string> platforms)
    {
        if (platforms.Count != platforms.Distinct(StringComparer.Ordinal).Count() || platforms.Any(p => !Known.Contains(p)))
            throw new InvalidDataException("Invalid supportedPlatforms: use unique OS/architecture identifiers such as linux-x64.");
    }

    public static string Unavailable(string name, IReadOnlyCollection<string> platforms, string runtimeIdentifier) =>
        $"{name} is available for {string.Join(", ", platforms)}; this system is {runtimeIdentifier}.";
}
