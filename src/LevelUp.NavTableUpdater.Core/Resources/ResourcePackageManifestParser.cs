using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Aircraft;

namespace LevelUp.NavTableUpdater.Core.Resources;

public static class ResourcePackageManifestParser
{
    private const int MaximumFiles = 10000;
    private const long MaximumExtractedBytes = 64L * 1024 * 1024 * 1024;
    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static ResourcePackageManifest Parse(ReadOnlySpan<byte> json)
    {
        ResourcePackageManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ResourcePackageManifest>(json, JsonOptions)
                ?? throw new InvalidDataException("Resource package manifest is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Resource package manifest JSON is invalid: {ex.Message}", ex);
        }

        Normalize(manifest);
        Validate(manifest);
        return manifest;
    }

    private static void Normalize(ResourcePackageManifest manifest)
    {
        manifest.PackageType = manifest.PackageType.Trim();
        manifest.PackageId = manifest.PackageId.Trim();
        manifest.PackageVersion = manifest.PackageVersion.Trim();
        manifest.ReleaseTag = manifest.ReleaseTag.Trim();
        manifest.Channel = manifest.Channel.Trim().ToLowerInvariant();
        manifest.Repository = manifest.Repository.TrimEnd('/');
        manifest.SupportedProducts ??= [];
        manifest.SupportedProducts = manifest.SupportedProducts
            .Select(product => product.Trim())
            .ToList();
        manifest.DeliveryMode = manifest.DeliveryMode.Trim().ToLowerInvariant();
        manifest.ArchiveRoot = manifest.ArchiveRoot.Trim();
        manifest.TargetDirectory = manifest.TargetDirectory.Trim();
        manifest.Files ??= [];
        foreach (var file in manifest.Files)
        {
            file.Path = NormalizeRelativePath(file.Path);
            file.Sha256 = file.Sha256.Trim().ToLowerInvariant();
        }

        manifest.Archive ??= new ResourcePackageArchive();
        manifest.Archive.FileName = manifest.Archive.FileName.Trim();
        manifest.Archive.Sha256 = manifest.Archive.Sha256.Trim().ToLowerInvariant();
    }

    private static void Validate(ResourcePackageManifest manifest)
    {
        if (manifest.SchemaVersion != 1
            || !manifest.PackageType.Equals("resource", StringComparison.Ordinal)
            || !IsSafePackageId(manifest.PackageId)
            || string.IsNullOrWhiteSpace(manifest.PackageVersion)
            || manifest.PackageVersion.Length > 64
            || manifest.PackageVersion.Any(char.IsControl)
            || !IsSafeSegment(manifest.ReleaseTag)
            || manifest.Channel is not "stable" and not "beta")
        {
            throw new InvalidDataException("Resource package manifest has an incomplete or unsupported identity.");
        }

        if (!Uri.TryCreate(manifest.Repository, UriKind.Absolute, out var repository)
            || repository.Scheme != Uri.UriSchemeHttps
            || !repository.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || repository.Query.Length > 0
            || repository.Fragment.Length > 0
            || repository.AbsolutePath.Trim('/').Split('/').Length != 2)
        {
            throw new InvalidDataException("Resource package repository must be a canonical HTTPS GitHub repository URL.");
        }

        if (manifest.SupportedProducts.Count == 0
            || manifest.SupportedProducts.Count != manifest.SupportedProducts.Distinct(StringComparer.Ordinal).Count()
            || manifest.SupportedProducts.Any(product => !AircraftProductIds.IsSupported(product)))
        {
            throw new InvalidDataException("Resource package manifest has invalid product compatibility metadata.");
        }

        if (!manifest.DeliveryMode.Equals("extract", StringComparison.Ordinal)
            || !IsSafeDirectoryName(manifest.ArchiveRoot)
            || !IsSafeDirectoryName(manifest.TargetDirectory)
            || manifest.ExtractedSize is < 0 or > MaximumExtractedBytes
            || manifest.Files.Count == 0
            || manifest.Files.Count > MaximumFiles
            || manifest.Files.Count != manifest.Files
                .Select(file => file.Path)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count()
            || manifest.Files.Any(file => !IsSafeRelativePath(file.Path)
                || file.Size is < 0 or > MaximumExtractedBytes
                || !IsSha256(file.Sha256))
            || !TotalSizeMatches(manifest.Files, manifest.ExtractedSize))
        {
            throw new InvalidDataException("Resource package extraction metadata is incomplete or unsafe.");
        }

        if (!IsSafeFileName(manifest.Archive.FileName, ".7z")
            || manifest.Archive.Size <= 0
            || !IsSha256(manifest.Archive.Sha256))
        {
            throw new InvalidDataException("Resource package archive metadata is incomplete or unsafe.");
        }
    }

    internal static bool IsSafeFileName(string value, string suffix) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 255
        && value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
        && Path.GetFileName(value) == value
        && !value.Contains('/')
        && !value.Contains('\\')
        && IsSafePathPart(value);

    internal static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    internal static bool IsSafeSegment(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 128
        && value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '_');

    internal static bool IsSafeDirectoryName(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 255
        && value is not "." and not ".."
        && Path.GetFileName(value) == value
        && !value.Contains('/')
        && !value.Contains('\\')
        && IsSafePathPart(value);

    internal static bool IsSafeRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 1024
            || Path.IsPathRooted(value)
            || value.Contains('\\')
            || value.Contains(':')
            || value.Contains('\0'))
        {
            return false;
        }

        var parts = value.Split('/');
        return parts.Length > 0 && parts.All(IsSafePathPart);
    }

    internal static string NormalizeRelativePath(string value) => value.Trim();

    private static bool IsSafePathPart(string part) =>
        !string.IsNullOrWhiteSpace(part)
        && part.Length <= 255
        && part is not "." and not ".."
        && !part.EndsWith(' ')
        && !part.EndsWith('.')
        && !part.Any(ch => char.IsControl(ch) || ch is '<' or '>' or ':' or '"' or '|' or '?' or '*')
        && !WindowsReservedNames.Contains(part.Split('.')[0]);

    private static bool TotalSizeMatches(IEnumerable<ResourcePackageFile> files, long expected)
    {
        try
        {
            return files.Aggregate(0L, (total, file) => checked(total + file.Size)) == expected;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool IsSafePackageId(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 128
        && value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '_');
}
