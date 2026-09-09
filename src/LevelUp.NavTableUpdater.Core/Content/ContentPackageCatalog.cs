using System.Text.Json;
using System.Text.Json.Serialization;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Manifest;

namespace LevelUp.NavTableUpdater.Core.Content;

public enum ContentPackageCategory
{
    Unknown,
    ManagedContent,
    CompatibilityPackage,
    OptionalPatch,
    AircraftComponent,
    Tool,
    Resource
}

public enum ContentPackageDistributionKind
{
    Unknown,
    ExistingVnav,
    GitHubReleaseArchive,
    GitHubToolRelease,
    GitHubXPlaneOverlayRelease,
    GitHubResourceRelease,
    CatalogGroup,
    GitHubModuleSource
}

public sealed class ContentPackageCatalogDocument
{
    public int SchemaVersion { get; set; }

    public string CatalogVersion { get; set; } = "";

    public string MinimumToolkitVersion { get; set; } = "";

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

    public List<CatalogGroupMember> Members { get; set; } = [];
}

public sealed class CatalogGroupMember
{
    public string PackageId { get; set; } = "";
    public string ModuleId { get; set; } = "";
    public CompatibilityModulePolicy Policy { get; set; }
    public int InstallationOrder { get; set; }
    public string SourceFormat { get; set; } = "";
    public string ManifestPath { get; set; } = "";
    public string AssetNamePattern { get; set; } = "";
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
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    private ContentPackageCatalog(
        string catalogVersion,
        string minimumToolkitVersion,
        IReadOnlyList<ContentPackageCatalogEntry> packages)
    {
        CatalogVersion = catalogVersion;
        MinimumToolkitVersion = minimumToolkitVersion;
        Packages = packages;
    }

    public string CatalogVersion { get; }

    public string MinimumToolkitVersion { get; }

    public IReadOnlyList<ContentPackageCatalogEntry> Packages { get; }

    public static ContentPackageCatalog Parse(string json, Version? toolkitVersion = null)
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

        Validate(document, toolkitVersion);
        return new ContentPackageCatalog(
            document.CatalogVersion,
            document.MinimumToolkitVersion,
            document.Packages);
    }

    public IReadOnlyList<ContentPackageCatalogEntry> ForProduct(string productId) =>
        Packages.Where(package => package.SupportedProducts.Contains(productId, StringComparer.Ordinal)).ToArray();

    private static void Validate(ContentPackageCatalogDocument document, Version? toolkitVersion)
    {
        document.Packages ??= [];
        if (document.SchemaVersion != 1
            || !TryParseThreePartVersion(document.CatalogVersion, out _))
        {
            throw new InvalidDataException("Unsupported or incomplete content package catalog identity.");
        }

        if (!string.IsNullOrWhiteSpace(document.MinimumToolkitVersion))
        {
            if (!TryParseThreePartVersion(document.MinimumToolkitVersion, out var minimumToolkitVersion))
            {
                throw new InvalidDataException("Content package catalog has an invalid minimumToolkitVersion.");
            }

            if (toolkitVersion is not null && toolkitVersion < minimumToolkitVersion)
            {
                throw new InvalidDataException(
                    $"Content package catalog {document.CatalogVersion} requires toolkit "
                    + $"{minimumToolkitVersion} or newer; current toolkit is {toolkitVersion}.");
            }
        }

        if (document.Packages.Count == 0)
        {
            throw new InvalidDataException("Content package catalog declares no packages.");
        }

        var packageIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var package in document.Packages)
        {
            package.Members ??= [];
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

        var groupedProducts = new HashSet<(string Product, string Package)>();
        foreach (var group in document.Packages.Where(p => p.Distribution.Kind is ContentPackageDistributionKind.CatalogGroup))
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var orders = new HashSet<int>();
            var sources = new HashSet<string>(StringComparer.Ordinal);
            if (group.Members.Count == 0) throw new InvalidDataException("Catalog group is empty.");
            foreach (var member in group.Members)
            {
                foreach (var product in group.SupportedProducts)
                    if (!groupedProducts.Add((product, member.PackageId)))
                        throw new InvalidDataException("A source cannot have multiple catalog group owners for the same product.");
                var source = document.Packages.SingleOrDefault(p => p.PackageId == member.PackageId);
                if (source is null || source.Distribution.Kind is ContentPackageDistributionKind.CatalogGroup
                    || !IsSafePackageId(member.ModuleId) || !ids.Add(member.ModuleId)
                    || !sources.Add(member.PackageId) || member.InstallationOrder < 0 || !orders.Add(member.InstallationOrder)
                    || !group.SupportedProducts.All(source.SupportedProducts.Contains)
                    || member.Policy is not (CompatibilityModulePolicy.Required or CompatibilityModulePolicy.Optional)
                    || member.SourceFormat is not ("vnav" or "declarative" or "moduleSource" or "compatibility")
                    || !IsSafeRelativePath(member.ManifestPath)
                    || string.IsNullOrWhiteSpace(member.AssetNamePattern) || !member.AssetNamePattern.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || member.AssetNamePattern.Count(ch => ch == '*') > 1
                    || member.AssetNamePattern.Contains('/') || member.AssetNamePattern.Contains('\\'))
                    throw new InvalidDataException($"Invalid source or policy in catalog group {group.PackageId}.");
            }
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
            || (package.Category is ContentPackageCategory.CompatibilityPackage
                && package.Activation is not ContentPatchActivation.Managed)
            || (package.Category is ContentPackageCategory.Tool
                && package.Activation is not ContentPatchActivation.ExplicitOptIn)
            || (package.Category is ContentPackageCategory.AircraftComponent
                && package.Activation is not ContentPatchActivation.ExplicitOptIn)
            || (package.Category is ContentPackageCategory.Resource
                && package.Activation is not ContentPatchActivation.ExplicitOptIn))
        {
            throw new InvalidDataException($"Content package {package.PackageId} has inconsistent category and activation metadata.");
        }
    }

    private static void ValidateDistribution(ContentPackageCatalogEntry package)
    {
        if (package.Distribution.Kind is ContentPackageDistributionKind.CatalogGroup)
        {
            if (package.Category is not ContentPackageCategory.CompatibilityPackage)
                throw new InvalidDataException("Catalog groups require compatibilityPackage lifecycle.");
            return;
        }
        if (package.Members.Count != 0) throw new InvalidDataException("Only catalog groups may declare members.");
        if (package.Distribution.Kind is ContentPackageDistributionKind.GitHubModuleSource)
        {
            if (package.Category is not ContentPackageCategory.CompatibilityPackage)
                throw new InvalidDataException("Module sources require compatibilityPackage lifecycle.");
            return;
        }
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
            var supportedCategory = package.Category is ContentPackageCategory.Tool
                or ContentPackageCategory.AircraftComponent;
            var expectedScope = package.Category is ContentPackageCategory.AircraftComponent
                ? "aircraftInstallation"
                : "xPlaneInstallation";
            if (!supportedCategory
                || package.Distribution.ManifestSchemaVersion != 1
                || !IsSafeAssetPattern(package.Distribution.ManifestAssetNamePattern, ".json")
                || !string.Equals(package.InstallScope, expectedScope, StringComparison.Ordinal)
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

        if (package.Distribution.Kind is ContentPackageDistributionKind.GitHubXPlaneOverlayRelease)
        {
            if (package.Category is not ContentPackageCategory.Tool
                || package.Distribution.ManifestSchemaVersion != 2
                || !IsSafeAssetPattern(package.Distribution.ManifestAssetNamePattern, ".json")
                || !string.Equals(package.InstallScope, "xPlaneInstallation", StringComparison.Ordinal)
                || !string.IsNullOrWhiteSpace(package.TargetPath)
                || !string.IsNullOrWhiteSpace(package.VersionMarkerPath)
                || package.SupportedChannels.Count == 0
                || package.SupportedChannels.Count != package.SupportedChannels.Distinct(StringComparer.Ordinal).Count()
                || package.SupportedChannels.Any(channel => channel is not "stable" and not "beta"))
            {
                throw new InvalidDataException($"Content package {package.PackageId} has unsafe X-Plane overlay release metadata.");
            }

            return;
        }

        if (package.Distribution.Kind is ContentPackageDistributionKind.GitHubResourceRelease)
        {
            if (package.Category is not ContentPackageCategory.Resource
                || package.Distribution.ManifestSchemaVersion != 1
                || !IsSafeAssetPattern(package.Distribution.AssetNamePattern, ".7z")
                || !IsSafeAssetPattern(package.Distribution.ManifestAssetNamePattern, ".json")
                || !string.Equals(package.InstallScope, "userSelectedDirectory", StringComparison.Ordinal)
                || !string.IsNullOrWhiteSpace(package.TargetPath)
                || !string.IsNullOrWhiteSpace(package.VersionMarkerPath)
                || package.RestartRequired
                || package.SupportedChannels.Count == 0
                || package.SupportedChannels.Count != package.SupportedChannels.Distinct(StringComparer.Ordinal).Count()
                || package.SupportedChannels.Any(channel => channel is not "stable" and not "beta"))
            {
                throw new InvalidDataException($"Content package {package.PackageId} has unsafe GitHub resource release metadata.");
            }

            return;
        }

        var supportedPatchArchive = package.Category switch
        {
            ContentPackageCategory.OptionalPatch => package.Activation is ContentPatchActivation.ExplicitOptIn
                && package.Distribution.ManifestSchemaVersion == 2,
            ContentPackageCategory.CompatibilityPackage => package.Activation is ContentPatchActivation.Managed
                && package.Distribution.ManifestSchemaVersion == CompatibilityPackageManifestParser.CurrentSchemaVersion,
            _ => false
        };
        if (!supportedPatchArchive
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

    private static bool TryParseThreePartVersion(string value, out Version version)
    {
        version = new Version();
        var parts = value.Split('.', StringSplitOptions.None);
        if (parts.Length != 3
            || parts.Any(part => part.Length == 0 || !part.All(char.IsAsciiDigit))
            || !int.TryParse(parts[0], out var major)
            || !int.TryParse(parts[1], out var minor)
            || !int.TryParse(parts[2], out var build))
        {
            return false;
        }

        version = new Version(major, minor, build);
        return true;
    }
}
