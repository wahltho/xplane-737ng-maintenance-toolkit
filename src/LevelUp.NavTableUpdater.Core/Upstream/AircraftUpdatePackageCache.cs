using System.Security.Cryptography;
using LevelUp.NavTableUpdater.Core.Platform;

namespace LevelUp.NavTableUpdater.Core.Upstream;

public sealed class AircraftUpdatePackageCache
{
    private const string MarkerFileName = ".xplane-737ng-aircraft-update-cache";

    public AircraftUpdatePackageCache(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        RootPath = Path.GetFullPath(rootPath);
    }

    public string RootPath { get; }

    public static string DefaultRootPath => ToolkitPaths.DefaultAircraftUpdateCacheRootPath;

    public void EnsureRoot()
    {
        Directory.CreateDirectory(RootPath);
        File.WriteAllText(Path.Combine(RootPath, MarkerFileName), "X-Plane 737NG Maintenance Toolkit aircraft update cache\n");
    }

    public int Clear()
    {
        if (!Directory.Exists(RootPath))
        {
            EnsureRoot();
            return 0;
        }

        if (!IsSafeToClear())
        {
            throw new InvalidOperationException("Cache folder is not marked as a toolkit aircraft update cache. Save the cache folder setting first before clearing it.");
        }

        var removed = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(RootPath))
        {
            if (string.Equals(Path.GetFileName(entry), MarkerFileName, StringComparison.Ordinal))
            {
                continue;
            }

            if (Directory.Exists(entry))
            {
                Directory.Delete(entry, recursive: true);
            }
            else
            {
                File.Delete(entry);
            }

            removed++;
        }

        EnsureRoot();
        return removed;
    }

    public AircraftUpdatePackageCacheEntry Inspect(AircraftUpdatePackage package)
    {
        var cachePath = GetPackagePath(package);
        if (!File.Exists(cachePath))
        {
            return new AircraftUpdatePackageCacheEntry(
                package,
                cachePath,
                AircraftUpdatePackageCacheState.Missing,
                SizeBytes: null,
                Sha256: null);
        }

        var info = new FileInfo(cachePath);
        return new AircraftUpdatePackageCacheEntry(
            package,
            cachePath,
            AircraftUpdatePackageCacheState.Cached,
            info.Length,
            ComputeSha256(cachePath));
    }

    public IReadOnlyList<AircraftUpdatePackageCacheEntry> InspectRequiredPackages(AircraftUpdatePlan plan) =>
        plan.RequiredPackages.Select(Inspect).ToArray();

    public AircraftUpdatePackageCacheEntry ImportZip(string zipPath, AircraftUpdatePackage expectedPackage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        ArgumentNullException.ThrowIfNull(expectedPackage);

        var sourcePath = Path.GetFullPath(zipPath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Aircraft update ZIP was not found.", sourcePath);
        }

        var sourceFileName = Path.GetFileName(sourcePath);
        if (!string.Equals(sourceFileName, expectedPackage.FileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Selected ZIP '{sourceFileName}' does not match expected package '{expectedPackage.FileName}'.");
        }

        EnsureRoot();
        var destinationPath = GetPackagePath(expectedPackage);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        var tempPath = destinationPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.Copy(sourcePath, tempPath, overwrite: false);
            File.Move(tempPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        var info = new FileInfo(destinationPath);
        return new AircraftUpdatePackageCacheEntry(
            expectedPackage,
            destinationPath,
            AircraftUpdatePackageCacheState.Imported,
            info.Length,
            ComputeSha256(destinationPath));
    }

    public string GetPackagePath(AircraftUpdatePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var fileName = Path.GetFileName(package.FileName);
        if (!string.Equals(fileName, package.FileName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Package filename is not safe: {package.FileName}");
        }

        var family = SanitizePathSegment(package.Family);
        var version = package.Version.ToString();
        return Path.Combine(RootPath, family, version, fileName);
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) || ch is '/' or '\\' ? '_' : ch).ToArray();
        var sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private bool IsSafeToClear()
    {
        if (File.Exists(Path.Combine(RootPath, MarkerFileName)))
        {
            return true;
        }

        return string.Equals(RootPath, Path.GetFullPath(DefaultRootPath), StringComparison.OrdinalIgnoreCase);
    }
}
