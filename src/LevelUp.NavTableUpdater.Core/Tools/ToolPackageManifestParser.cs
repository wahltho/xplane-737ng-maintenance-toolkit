using System.Text.Json;

namespace LevelUp.NavTableUpdater.Core.Tools;

public static class ToolPackageManifestParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static ToolPackageManifest Parse(ReadOnlySpan<byte> json)
    {
        ToolPackageManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ToolPackageManifest>(json, JsonOptions)
                ?? throw new InvalidDataException("Tool package manifest is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Tool package manifest JSON is invalid: {ex.Message}", ex);
        }

        Normalize(manifest);
        Validate(manifest);
        return manifest;
    }

    public static bool IsProtectedPath(ToolPackageManifest manifest, string relativePath)
        => IsProtectedPath(manifest.ProtectedPaths, relativePath);

    public static bool IsProtectedPath(IEnumerable<string> protectedPaths, string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        foreach (var pattern in protectedPaths)
        {
            var normalizedPattern = pattern.Replace('\\', '/');
            if (normalizedPattern.EndsWith("/**", StringComparison.Ordinal))
            {
                var prefix = normalizedPattern[..^3].TrimEnd('/');
                if (normalized.Equals(prefix, PathComparison)
                    || normalized.StartsWith(prefix + "/", PathComparison))
                {
                    return true;
                }
            }
            else if (normalized.Equals(normalizedPattern, PathComparison))
            {
                return true;
            }
        }

        return false;
    }

    public static string NormalizeRelativePath(string value)
    {
        ValidateRelativePath(value, "Tool package path");
        return string.Join('/', value.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries));
    }

    public static void ValidateRelativePath(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)
            || Path.IsPathRooted(value)
            || value.Contains(':')
            || value.StartsWith('/')
            || value.EndsWith('/'))
        {
            throw new InvalidDataException($"{label} is unsafe: {value}.");
        }

        var parts = value.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(part => part is "." or ".."))
        {
            throw new InvalidDataException($"{label} is unsafe: {value}.");
        }
    }

    private static void Normalize(ToolPackageManifest manifest)
    {
        manifest.SupportedProducts ??= [];
        manifest.ProtectedPaths ??= [];
        manifest.Files ??= [];
        manifest.Archive ??= new ToolPackageArchive();
        manifest.PackageId = manifest.PackageId.Trim();
        manifest.PackageVersion = manifest.PackageVersion.Trim();
        manifest.ReleaseTag = manifest.ReleaseTag.Trim();
        manifest.Channel = manifest.Channel.Trim().ToLowerInvariant();
        manifest.Repository = manifest.Repository.TrimEnd('/');
        manifest.InstallScope = manifest.InstallScope.Trim();
        manifest.Layout = string.IsNullOrWhiteSpace(manifest.Layout) && manifest.SchemaVersion == 1
            ? "directory"
            : manifest.Layout.Trim();
        manifest.TargetPath = string.IsNullOrWhiteSpace(manifest.TargetPath)
            ? ""
            : NormalizeRelativePath(manifest.TargetPath);
        manifest.Archive.FileName = manifest.Archive.FileName.Trim();
        manifest.Archive.RootPath = manifest.Archive.RootPath.Trim().Trim('/');
        manifest.Archive.Sha256 = manifest.Archive.Sha256.Trim().ToLowerInvariant();
        manifest.SupportedProducts = manifest.SupportedProducts.Select(value => value.Trim()).ToList();
        manifest.ProtectedPaths = manifest.ProtectedPaths.Select(NormalizeProtectedPath).ToList();
        foreach (var file in manifest.Files)
        {
            file.Path = NormalizeRelativePath(file.Path);
            file.Sha256 = file.Sha256.Trim().ToLowerInvariant();
        }
    }

    private static void Validate(ToolPackageManifest manifest)
    {
        var directoryLayout = manifest.SchemaVersion == 1
            && manifest.Layout == "directory"
            && manifest.InstallScope is "xPlaneInstallation" or "aircraftInstallation"
            && !string.IsNullOrWhiteSpace(manifest.TargetPath);
        var overlayLayout = manifest.SchemaVersion == 2
            && manifest.Layout == "xPlaneOverlay"
            && manifest.InstallScope == "xPlaneInstallation"
            && string.IsNullOrWhiteSpace(manifest.TargetPath);
        if (!directoryLayout && !overlayLayout
            || string.IsNullOrWhiteSpace(manifest.PackageId)
            || string.IsNullOrWhiteSpace(manifest.PackageVersion)
            || string.IsNullOrWhiteSpace(manifest.ReleaseTag)
            || manifest.Channel is not "stable" and not "beta"
            || !manifest.RestartRequired)
        {
            throw new InvalidDataException("Tool package manifest identity or lifecycle metadata is incomplete.");
        }

        if (!Uri.TryCreate(manifest.Repository, UriKind.Absolute, out var repository)
            || repository.Scheme != Uri.UriSchemeHttps
            || !repository.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || repository.AbsolutePath.Trim('/').Split('/').Length != 2)
        {
            throw new InvalidDataException("Tool package manifest repository must be a canonical HTTPS GitHub URL.");
        }

        if (manifest.SupportedProducts.Count == 0
            || manifest.SupportedProducts.Count != manifest.SupportedProducts.Distinct(StringComparer.Ordinal).Count())
        {
            throw new InvalidDataException("Tool package manifest has invalid product compatibility metadata.");
        }

        var validArchiveRoot = directoryLayout
            ? !string.IsNullOrWhiteSpace(manifest.Archive.RootPath)
                && !manifest.Archive.RootPath.Contains('/')
                && !manifest.Archive.RootPath.Contains('\\')
            : string.IsNullOrWhiteSpace(manifest.Archive.RootPath);
        if (!IsSafeFileName(manifest.Archive.FileName, ".zip")
            || !validArchiveRoot
            || manifest.Archive.Size <= 0
            || !IsSha256(manifest.Archive.Sha256))
        {
            throw new InvalidDataException("Tool package manifest has invalid archive metadata.");
        }

        if (manifest.Files.Count == 0)
        {
            throw new InvalidDataException("Tool package manifest declares no files.");
        }

        var paths = new HashSet<string>(PathComparer);
        foreach (var file in manifest.Files)
        {
            if (file.Size < 0 || !IsSha256(file.Sha256) || !paths.Add(file.Path))
            {
                throw new InvalidDataException($"Tool package manifest contains invalid or duplicate file metadata: {file.Path}.");
            }
        }

        if (manifest.ProtectedPaths.Count != manifest.ProtectedPaths.Distinct(PathComparer).Count())
        {
            throw new InvalidDataException("Tool package manifest contains duplicate protected paths.");
        }

        if (overlayLayout && manifest.Files.Any(file => IsProtectedPath(manifest, file.Path)))
        {
            throw new InvalidDataException("Overlay package files must not overlap protected generated-data paths.");
        }
    }

    private static string NormalizeProtectedPath(string value)
    {
        var normalized = value.Replace('\\', '/').Trim();
        var recursive = normalized.EndsWith("/**", StringComparison.Ordinal);
        var path = recursive ? normalized[..^3].TrimEnd('/') : normalized;
        ValidateRelativePath(path, "Protected tool path");
        return recursive ? NormalizeRelativePath(path) + "/**" : NormalizeRelativePath(path);
    }

    private static bool IsSafeFileName(string value, string suffix) =>
        !string.IsNullOrWhiteSpace(value)
        && Path.GetFileName(value) == value
        && !value.Contains('/')
        && !value.Contains('\\')
        && value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);

    private static bool IsSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}
