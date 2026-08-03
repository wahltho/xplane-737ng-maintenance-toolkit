using System.Text.Json;
using System.Text.Json.Serialization;
using LevelUp.NavTableUpdater.Core.Aircraft;

namespace LevelUp.NavTableUpdater.Core.Content;

public enum ContentPackageCategory
{
    Unknown,
    ManagedContent,
    OptionalPatch,
    Tool
}

public enum ContentPackageDistributionKind
{
    Unknown,
    ExistingVnav,
    GitHubReleaseArchive,
    GitHubToolRelease
}

public sealed class ContentPackageCatalogDocument
{
    public int SchemaVersion { get; set; }

    public string CatalogVersion { get; set; } = "";

    public List<ContentPackageCatalogEntry> Packages { get; set; } = [];
}

public sealed class ContentPackageCatalogEntry
{
    public string PackageId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Description { get; set; } = "";

    public ContentPackageCategory Category { get; set; }

    public ContentPatchActivation? Activation { get; set; }

    public List<string> SupportedProducts { get; set; } = [];

    public string RepositoryUrl { get; set; } = "";

    public bool RestartRequired { get; set; }

    public string InstallScope { get; set; } = "";

    public string TargetPath { get; set; } = "";

    public string VersionMarkerPath { get; set; } = "";

    public List<string> SupportedChannels { get; set; } = [];

    public ContentPackageDistribution Distribution { get; set; } = new();
}

public sealed class ContentPackageDistribution
{
    public ContentPackageDistributionKind Kind { get; set; }

    public string AssetNamePattern { get; set; } = "";

    public string ManifestAssetNamePattern { get; set; } = "";

    public int? ManifestSchemaVersion { get; set; }
}

public sealed class ContentPackageCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    private ContentPackageCatalog(string catalogVersion, IReadOnlyList<ContentPackageCatalogEntry> packages)
    {
        CatalogVersion = catalogVersion;
        Packages = packages;
    }

    public string CatalogVersion { get; }

    public IReadOnlyList<ContentPackageCatalogEntry> Packages { get; }

    public static ContentPackageCatalog Parse(string json)
    {
        ContentPackageCatalogDocument document;
        try
        {
            document = JsonSerializer.Deserialize<ContentPackageCatalogDocument>(json, JsonOptions)
                ?? throw new InvalidDataException("Content package catalog is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Content package catalog JSON is invalid: {ex.Message}", ex);
        }

        Validate(document);
        return new ContentPackageCatalog(document.CatalogVersion, document.Packages);
    }

    public IReadOnlyList<ContentPackageCatalogEntry> ForProduct(string productId) =>
        Packages.Where(package => package.SupportedProducts.Contains(productId, StringComparer.Ordinal)).ToArray();

    private static void Validate(ContentPackageCatalogDocument document)
    {
        document.Packages ??= [];
        if (document.SchemaVersion != 1 || string.IsNullOrWhiteSpace(document.CatalogVersion))
        {
            throw new InvalidDataException("Unsupported or incomplete content package catalog identity.");
        }

        if (document.Packages.Count == 0)
        {
            throw new InvalidDataException("Content package catalog declares no packages.");
        }

        var packageIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var package in document.Packages)
        {
            package.SupportedProducts ??= [];
            package.SupportedChannels ??= [];
            package.Distribution ??= new ContentPackageDistribution();
            if (!IsSafePackageId(package.PackageId)
                || !packageIds.Add(package.PackageId)
                || string.IsNullOrWhiteSpace(package.DisplayName)
                || string.IsNullOrWhiteSpace(package.Description))
            {
                throw new InvalidDataException("Content package catalog contains an incomplete or duplicate package identity.");
            }

            if (package.Category is ContentPackageCategory.Unknown
                || package.Distribution.Kind is ContentPackageDistributionKind.Unknown
                || package.SupportedProducts.Count == 0
                || package.SupportedProducts.Count != package.SupportedProducts.Distinct(StringComparer.Ordinal).Count()
                || package.SupportedProducts.Any(productId => !AircraftProductIds.IsSupported(productId)))
            {
                throw new InvalidDataException($"Content package {package.PackageId} has invalid category or product compatibility metadata.");
            }

            ValidateRepository(package);
            ValidateLifecycle(package);
            ValidateDistribution(package);
        }
    }

    private static void ValidateRepository(ContentPackageCatalogEntry package)
    {
        if (!Uri.TryCreate(package.RepositoryUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0
            || uri.AbsolutePath.Trim('/').Split('/').Length != 2)
        {
            throw new InvalidDataException($"Content package {package.PackageId} must use a canonical HTTPS GitHub repository URL.");
        }
    }

    private static void ValidateLifecycle(ContentPackageCatalogEntry package)
    {
        if ((package.Category is ContentPackageCategory.ManagedContent
                && package.Activation is not ContentPatchActivation.Managed)
            || (package.Category is ContentPackageCategory.OptionalPatch
                && package.Activation is not ContentPatchActivation.ExplicitOptIn)
            || (package.Category is ContentPackageCategory.Tool
                && package.Activation is not ContentPatchActivation.ExplicitOptIn))
        {
            throw new InvalidDataException($"Content package {package.PackageId} has inconsistent category and activation metadata.");
        }
    }

    private static void ValidateDistribution(ContentPackageCatalogEntry package)
    {
        if (package.Distribution.Kind is ContentPackageDistributionKind.ExistingVnav)
        {
            if (package.Category is not ContentPackageCategory.ManagedContent)
            {
                throw new InvalidDataException($"Content package {package.PackageId} uses the VNAV distribution outside managed content.");
            }

            return;
        }

        if (package.Distribution.Kind is ContentPackageDistributionKind.GitHubToolRelease)
        {
            if (package.Category is not ContentPackageCategory.Tool
                || package.Distribution.ManifestSchemaVersion != 1
                || !IsSafeAssetPattern(package.Distribution.ManifestAssetNamePattern, ".json")
                || !string.Equals(package.InstallScope, "xPlaneInstallation", StringComparison.Ordinal)
                || !IsSafeRelativePath(package.TargetPath)
                || (!string.IsNullOrWhiteSpace(package.VersionMarkerPath)
                    && !IsSafeRelativePath(package.VersionMarkerPath))
                || package.SupportedChannels.Count == 0
                || package.SupportedChannels.Count != package.SupportedChannels.Distinct(StringComparer.Ordinal).Count()
                || package.SupportedChannels.Any(channel => channel is not "stable" and not "beta"))
            {
                throw new InvalidDataException($"Content package {package.PackageId} has unsafe GitHub tool release metadata.");
            }

            return;
        }

        if (package.Category is not ContentPackageCategory.OptionalPatch
            || package.Distribution.ManifestSchemaVersion != 2
            || !IsSafeAssetPattern(package.Distribution.AssetNamePattern, ".zip"))
        {
            throw new InvalidDataException($"Content package {package.PackageId} has unsafe GitHub release archive metadata.");
        }
    }

    private static bool IsSafePackageId(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '_');

    private static bool IsSafeAssetPattern(string value, string suffix) =>
        !string.IsNullOrWhiteSpace(value)
        && value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
        && !value.Contains('/')
        && !value.Contains('\\')
        && value.Count(ch => ch == '*') == 1
        && Path.GetFileName(value) == value;

    private static bool IsSafeRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) || value.Contains(':'))
        {
            return false;
        }

        var parts = value.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 && parts.All(part => part is not "." and not "..");
    }
}
