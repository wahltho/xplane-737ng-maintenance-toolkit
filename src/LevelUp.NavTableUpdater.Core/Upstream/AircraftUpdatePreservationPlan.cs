using System.Security.Cryptography;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.State;
using LevelUp.NavTableUpdater.Core.Tools;

namespace LevelUp.NavTableUpdater.Core.Upstream;

public sealed record AircraftUpdatePreservedFile(
    string RelativePath,
    byte[] Bytes,
    FileAttributes Attributes,
    UnixFileMode? UnixMode);

public sealed record AircraftUpdatePreservationPlan(
    string PackageId,
    string PackageVersion,
    IReadOnlyList<AircraftUpdatePreservedFile> Files)
{
    public IReadOnlySet<string> RelativePaths => Files
        .Select(file => file.RelativePath)
        .ToHashSet(AircraftUpdateLocalContentPolicy.PathComparer);

    public void ApplyTo(string aircraftRoot, ICollection<string> log)
    {
        foreach (var file in Files)
        {
            var targetPath = AircraftUpdatePath.ResolveTargetPath(aircraftRoot, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            var tempPath = targetPath + $".tmp-{Guid.NewGuid():N}";
            try
            {
                File.WriteAllBytes(tempPath, file.Bytes);
                File.Move(tempPath, targetPath, overwrite: true);
                File.SetAttributes(targetPath, file.Attributes);
                TrySetUnixFileMode(targetPath, file.UnixMode);
                if (new FileInfo(targetPath).Length != file.Bytes.LongLength
                    || !HashFile(targetPath).Equals(
                        Convert.ToHexString(SHA256.HashData(file.Bytes)).ToLowerInvariant(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Managed aircraft component failed staged verification: {file.RelativePath}");
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }

            log.Add($"[PRESERVE] Restored managed component file {file.RelativePath} ({PackageId} {PackageVersion}).");
        }
    }

    public static AircraftUpdatePreservationPlan? Capture(
        ContentPackageCatalogEntry catalogEntry,
        string aircraftRoot,
        ToolStateStore stateStore)
    {
        ArgumentNullException.ThrowIfNull(catalogEntry);
        ArgumentNullException.ThrowIfNull(stateStore);
        if (catalogEntry.Category is not ContentPackageCategory.AircraftComponent
            || catalogEntry.InstallScope != "aircraftInstallation")
        {
            throw new InvalidOperationException($"{catalogEntry.PackageId} is not an aircraft-scoped component.");
        }

        var fullRoot = AircraftUpdatePath.ResolvePhysicalPath(Path.GetFullPath(aircraftRoot));
        var state = stateStore.TryGetToolInstallation(aircraftRoot, catalogEntry.PackageId);
        if (state is null)
        {
            return null;
        }

        var expectedTarget = AircraftUpdatePath.ResolveTargetPath(fullRoot, catalogEntry.TargetPath);
        var recordedTarget = AircraftUpdatePath.ResolvePhysicalPath(state.TargetPath);
        if (!PathsEqual(expectedTarget, recordedTarget)
            || string.IsNullOrWhiteSpace(state.InstalledVersion)
            || state.InstalledFiles.Count == 0)
        {
            throw new InvalidDataException(
                $"Managed component {catalogEntry.DisplayName} has incomplete installation state. Repair it before updating the aircraft.");
        }

        var files = new List<AircraftUpdatePreservedFile>();
        var seenPaths = new HashSet<string>(AircraftUpdateLocalContentPolicy.PathComparer);
        foreach (var recorded in state.InstalledFiles.Where(file => !file.Protected))
        {
            var componentRelativePath = ToolPackageManifestParser.NormalizeRelativePath(recorded.RelativePath);
            var aircraftRelativePath = AircraftUpdatePath.NormalizeRelativePath(
                $"{catalogEntry.TargetPath}/{componentRelativePath}")
                ?? throw new InvalidDataException(
                    $"Managed component {catalogEntry.DisplayName} records an unsafe file path: {recorded.RelativePath}.");
            if (!seenPaths.Add(aircraftRelativePath))
            {
                throw new InvalidDataException(
                    $"Managed component {catalogEntry.DisplayName} records duplicate file state for {aircraftRelativePath}. Repair it before updating the aircraft.");
            }

            var sourcePath = AircraftUpdatePath.ResolveTargetPath(fullRoot, aircraftRelativePath);
            var sourceInfo = new FileInfo(sourcePath);
            if (!sourceInfo.Exists && sourceInfo.LinkTarget is null)
            {
                throw new InvalidDataException(
                    $"Managed component {catalogEntry.DisplayName} is missing or changed at {aircraftRelativePath}. Repair it before updating the aircraft.");
            }

            RejectLink(sourcePath);
            if (sourceInfo.Length != recorded.Size
                || !HashFile(sourcePath).Equals(recorded.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Managed component {catalogEntry.DisplayName} is missing or changed at {aircraftRelativePath}. Repair it before updating the aircraft.");
            }

            files.Add(new AircraftUpdatePreservedFile(
                aircraftRelativePath,
                File.ReadAllBytes(sourcePath),
                File.GetAttributes(sourcePath),
                TryGetUnixFileMode(sourcePath)));
        }

        return new AircraftUpdatePreservationPlan(
            catalogEntry.PackageId,
            state.InstalledVersion,
            files);
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void RejectLink(string path)
    {
        var info = new FileInfo(path);
        if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidDataException($"Managed aircraft component path is a symbolic link: {path}");
        }
    }

    private static bool PathsEqual(string left, string right) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)).Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    private static UnixFileMode? TryGetUnixFileMode(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return null;
        }

#pragma warning disable CA1416
        return File.GetUnixFileMode(path);
#pragma warning restore CA1416
    }

    private static void TrySetUnixFileMode(string path, UnixFileMode? mode)
    {
        if (OperatingSystem.IsWindows() || mode is null)
        {
            return;
        }

#pragma warning disable CA1416
        File.SetUnixFileMode(path, mode.Value);
#pragma warning restore CA1416
    }
}
