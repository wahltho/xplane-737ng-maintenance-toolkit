using System.Text.Json;
using System.Text.RegularExpressions;

namespace LevelUp.NavTableUpdater.Core.Upstream;

public sealed record AircraftUpdatePackageManifest(
    int SchemaVersion,
    string ProductId,
    AircraftUpdatePackageKind PackageKind,
    string ReleaseVersion,
    string? BaselineVersion,
    IReadOnlyList<string> BaselineAliases,
    long? ReleaseSequence,
    string ContentRoot,
    IReadOnlyList<AircraftUpdateManifestFile> Files,
    IReadOnlyList<string> DeletedPaths,
    AircraftUpdateManifestArchive Archive,
    string ManifestPath);

public sealed record AircraftUpdateManifestFile(
    string Path,
    string Operation,
    long Size,
    string Sha256);

public sealed record AircraftUpdateManifestArchive(
    string FileName,
    long Size,
    string Sha256);

public static partial class AircraftUpdatePackageManifestParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static AircraftUpdatePackageManifest Load(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        var fullPath = Path.GetFullPath(manifestPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Aircraft update package manifest was not found.", fullPath);
        }

        return Parse(File.ReadAllText(fullPath), fullPath);
    }

    public static AircraftUpdatePackageManifest Parse(string manifestJson, string manifestSource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestSource);

        PackageManifestDocument document;
        try
        {
            document = JsonSerializer.Deserialize<PackageManifestDocument>(manifestJson, JsonOptions)
                ?? throw new InvalidDataException("Aircraft update package manifest is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Aircraft update package manifest is invalid JSON: {ex.Message}", ex);
        }

        return Validate(document, manifestSource);
    }

    private static AircraftUpdatePackageManifest Validate(PackageManifestDocument document, string manifestPath)
    {
        if (document.SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported aircraft update manifest schemaVersion {document.SchemaVersion}.");
        }

        if (!string.Equals(document.ProductId, "levelup-737ng", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Unsupported aircraft update productId '{document.ProductId}'.");
        }

        var packageKind = document.PackageType?.Trim().ToLowerInvariant() switch
        {
            "full" => AircraftUpdatePackageKind.FullBaseline,
            "cumulativepatch" => AircraftUpdatePackageKind.CumulativePatch,
            _ => throw new InvalidDataException($"Unsupported aircraft update packageType '{document.PackageType}'.")
        };

        var releaseVersion = packageKind == AircraftUpdatePackageKind.CumulativePatch
            ? document.TargetVersion
            : document.ReleaseVersion ?? document.TargetVersion;
        if (string.IsNullOrWhiteSpace(releaseVersion))
        {
            throw new InvalidDataException("Aircraft update manifest has no releaseVersion/targetVersion.");
        }

        if (packageKind == AircraftUpdatePackageKind.CumulativePatch
            && string.IsNullOrWhiteSpace(document.BaselineVersion))
        {
            throw new InvalidDataException("Cumulative aircraft update manifest has no baselineVersion.");
        }

        var contentRoot = AircraftUpdatePath.NormalizeRelativePath(document.ContentRoot)
            ?? throw new InvalidDataException("Aircraft update manifest contentRoot is unsafe or missing.");
        var archive = document.Archive
            ?? throw new InvalidDataException("Aircraft update manifest has no archive record.");
        var archiveFileName = Path.GetFileName(archive.FileName);
        if (string.IsNullOrWhiteSpace(archive.FileName)
            || !string.Equals(archiveFileName, archive.FileName, StringComparison.Ordinal)
            || archive.Size < 0
            || !IsSha256(archive.Sha256))
        {
            throw new InvalidDataException("Aircraft update manifest archive record is invalid.");
        }

        var files = new List<AircraftUpdateManifestFile>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in document.Files ?? [])
        {
            var path = AircraftUpdatePath.NormalizeRelativePath(file.Path)
                ?? throw new InvalidDataException($"Aircraft update manifest contains unsafe file path '{file.Path}'.");
            if (!paths.Add(path))
            {
                throw new InvalidDataException($"Aircraft update manifest contains duplicate file path '{file.Path}'.");
            }

            var operation = file.Operation?.Trim().ToLowerInvariant();
            if (operation is not ("add" or "replace" or "full"))
            {
                throw new InvalidDataException($"Aircraft update manifest contains unsupported operation '{file.Operation}' for '{file.Path}'.");
            }

            if (file.Size < 0 || !IsSha256(file.Sha256))
            {
                throw new InvalidDataException($"Aircraft update manifest size/hash is invalid for '{file.Path}'.");
            }

            files.Add(new AircraftUpdateManifestFile(path, operation, file.Size, file.Sha256!.ToLowerInvariant()));
        }

        var deletedPaths = new List<string>();
        foreach (var deletedPath in document.DeletedPaths ?? [])
        {
            var path = AircraftUpdatePath.NormalizeRelativePath(deletedPath)
                ?? throw new InvalidDataException($"Aircraft update manifest contains unsafe deleted path '{deletedPath}'.");
            if (!paths.Add(path))
            {
                throw new InvalidDataException($"Aircraft update manifest path is both written and deleted: '{deletedPath}'.");
            }

            deletedPaths.Add(path);
        }

        var aliases = (document.BaselineAliases ?? [])
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Select(alias => alias.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new AircraftUpdatePackageManifest(
            document.SchemaVersion,
            document.ProductId!,
            packageKind,
            releaseVersion.Trim(),
            document.BaselineVersion?.Trim(),
            aliases,
            document.ReleaseSequence,
            contentRoot,
            files,
            deletedPaths,
            new AircraftUpdateManifestArchive(archiveFileName!, archive.Size, archive.Sha256!.ToLowerInvariant()),
            manifestPath);
    }

    private static bool IsSha256(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Sha256Pattern().IsMatch(value);

    [GeneratedRegex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    private sealed class PackageManifestDocument
    {
        public int SchemaVersion { get; set; }
        public string? ProductId { get; set; }
        public string? PackageType { get; set; }
        public string? ReleaseVersion { get; set; }
        public string? TargetVersion { get; set; }
        public string? BaselineVersion { get; set; }
        public List<string>? BaselineAliases { get; set; }
        public long? ReleaseSequence { get; set; }
        public string? ContentRoot { get; set; }
        public List<ManifestFileDocument>? Files { get; set; }
        public List<string>? DeletedPaths { get; set; }
        public ManifestArchiveDocument? Archive { get; set; }
    }

    private sealed class ManifestFileDocument
    {
        public string? Path { get; set; }
        public string? Operation { get; set; }
        public long Size { get; set; }
        public string? Sha256 { get; set; }
    }

    private sealed class ManifestArchiveDocument
    {
        public string? FileName { get; set; }
        public long Size { get; set; }
        public string? Sha256 { get; set; }
    }
}
