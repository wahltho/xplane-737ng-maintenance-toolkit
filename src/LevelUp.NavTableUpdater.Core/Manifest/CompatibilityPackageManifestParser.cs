using System.Text.Json;
using System.Text.Json.Serialization;
using LevelUp.NavTableUpdater.Core.Aircraft;

namespace LevelUp.NavTableUpdater.Core.Manifest;

public static class CompatibilityPackageManifestParser
{
    public const int CurrentSchemaVersion = 3;
    public const string PackageType = "compatibilityPackage";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static CompatibilityPackageManifest Parse(string json)
    {
        CompatibilityPackageManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<CompatibilityPackageManifest>(json, JsonOptions)
                ?? throw new InvalidOperationException("Compatibility package manifest is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Compatibility package manifest JSON is invalid: {ex.Message}", ex);
        }

        Validate(manifest);
        return manifest;
    }

    public static void Validate(CompatibilityPackageManifest manifest)
    {
        manifest.SupportedProducts ??= [];
        manifest.SupportedUpstreamReleases ??= [];
        manifest.Modules ??= [];
        if (manifest.SchemaVersion != CurrentSchemaVersion
            || !manifest.PackageType.Equals(PackageType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Unsupported compatibility package identity: schema {manifest.SchemaVersion}, type '{manifest.PackageType}'.");
        }

        RequireSafeId(manifest.PackageId, "packageId");
        Require(manifest.PackageVersion, "packageVersion");
        Require(manifest.RepositoryUrl, "repositoryUrl");
        Require(manifest.AircraftFamily, "aircraftFamily");
        if (!Uri.TryCreate(manifest.RepositoryUrl, UriKind.Absolute, out var repositoryUri)
            || repositoryUri.Scheme is not ("https" or "http"))
        {
            throw new InvalidOperationException("repositoryUrl must be an absolute HTTP(S) URL.");
        }

        if (manifest.SupportedProducts.Count == 0
            || manifest.SupportedProducts.Count != manifest.SupportedProducts.Distinct(StringComparer.Ordinal).Count()
            || manifest.SupportedProducts.Any(productId => !AircraftProductIds.IsSupported(productId)))
        {
            throw new InvalidOperationException("supportedProducts must contain unique supported product IDs.");
        }

        if (manifest.Modules.Count == 0)
        {
            throw new InvalidOperationException("Compatibility package must declare at least one module.");
        }

        var moduleIds = new HashSet<string>(StringComparer.Ordinal);
        var moduleIdCasing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var installationOrders = new HashSet<int>();
        var targetPathCasing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in manifest.Modules)
        {
            module.Requires ??= [];
            module.ConflictsWith ??= [];
            module.Payloads ??= [];
            module.Targets ??= [];
            RequireSafeId(module.ModuleId, "moduleId");
            Require(module.DisplayName, $"module {module.ModuleId} displayName");
            Require(module.Description, $"module {module.ModuleId} description");
            if (!moduleIds.Add(module.ModuleId)
                || !moduleIdCasing.Add(module.ModuleId)
                || module.InstallationOrder < 0
                || !installationOrders.Add(module.InstallationOrder))
            {
                throw new InvalidOperationException(
                    $"Module {module.ModuleId} has a duplicate identity or installationOrder.");
            }

            if (module.Policy is CompatibilityModulePolicy.Required && !module.DefaultEnabled)
            {
                throw new InvalidOperationException($"Required module {module.ModuleId} must be enabled by default.");
            }

            if (module.Policy is CompatibilityModulePolicy.Optional && module.DefaultEnabled)
            {
                throw new InvalidOperationException($"Optional module {module.ModuleId} must require explicit opt-in.");
            }

            if (module.Payloads.Count == 0 || module.Targets.Count == 0)
            {
                throw new InvalidOperationException($"Module {module.ModuleId} must declare payloads and targets.");
            }

            ValidateModuleContent(module);
            foreach (var target in module.Targets)
            {
                if (targetPathCasing.TryGetValue(target.RelativePath, out var existing)
                    && !existing.Equals(target.RelativePath, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Compatibility package contains case-colliding target paths: {existing} and {target.RelativePath}.");
                }

                targetPathCasing[target.RelativePath] = target.RelativePath;
            }
        }

        foreach (var module in manifest.Modules)
        {
            ValidateRelationships(module, moduleIds);
        }
    }

    private static void ValidateModuleContent(CompatibilityPackageModule module)
    {
        var payloads = new Dictionary<string, DeclarativePatchPayload>(StringComparer.Ordinal);
        foreach (var payload in module.Payloads)
        {
            DeclarativePatchManifestParser.ValidateRelativePath(payload.Path, $"module {module.ModuleId} payload path");
            if (!payloads.TryAdd(payload.Path, payload)
                || payload.Size < 0
                || !IsSha256(payload.Sha256))
            {
                throw new InvalidOperationException($"Module {module.ModuleId} has invalid or duplicate payload metadata.");
            }
        }

        foreach (var target in module.Targets)
        {
            Require(target.Operation, $"module {module.ModuleId} target operation");
            DeclarativePatchManifestParser.ValidateRelativePath(target.RelativePath, $"module {module.ModuleId} target path");
            DeclarativePatchManifestParser.ValidateRelativePath(target.Payload, $"module {module.ModuleId} target payload");
            if (!payloads.ContainsKey(target.Payload))
            {
                throw new InvalidOperationException(
                    $"Module {module.ModuleId} target {target.RelativePath} references undeclared payload {target.Payload}.");
            }

            if (target.SourceSha256.Any(hash => !IsSha256(hash))
                || (target.ResultSha256 is not null && !IsSha256(target.ResultSha256)))
            {
                throw new InvalidOperationException($"Module {module.ModuleId} target {target.RelativePath} has invalid SHA-256 metadata.");
            }

            if (target.Operation.Equals("copy-file-v1", StringComparison.Ordinal)
                && (target.ResultSha256 is null
                    || !target.ResultSha256.Equals(payloads[target.Payload].Sha256, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Module {module.ModuleId} copy target {target.RelativePath} must use the payload SHA-256 as resultSha256.");
            }
        }

        foreach (var group in module.Targets.GroupBy(target => target.Payload, StringComparer.Ordinal))
        {
            var raw = group.Any(target => target.Operation.Equals("copy-file-v1", StringComparison.Ordinal));
            var json = group.Any(target => !target.Operation.Equals("copy-file-v1", StringComparison.Ordinal));
            if (raw && json)
            {
                throw new InvalidOperationException(
                    $"Module {module.ModuleId} payload {group.Key} cannot be both raw file content and a JSON patch definition.");
            }
        }
    }

    private static void ValidateRelationships(CompatibilityPackageModule module, IReadOnlySet<string> moduleIds)
    {
        if (module.Requires.Count != module.Requires.Distinct(StringComparer.Ordinal).Count()
            || module.ConflictsWith.Count != module.ConflictsWith.Distinct(StringComparer.Ordinal).Count()
            || module.Requires.Any(id => !moduleIds.Contains(id) || id.Equals(module.ModuleId, StringComparison.Ordinal))
            || module.ConflictsWith.Any(id => !moduleIds.Contains(id) || id.Equals(module.ModuleId, StringComparison.Ordinal))
            || module.Requires.Intersect(module.ConflictsWith, StringComparer.Ordinal).Any())
        {
            throw new InvalidOperationException($"Module {module.ModuleId} has invalid dependency or conflict metadata.");
        }
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static void RequireSafeId(string value, string name)
    {
        Require(value, name);
        if (value.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '_')))
        {
            throw new InvalidOperationException($"Compatibility package field {name} contains unsafe characters.");
        }
    }

    private static void Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Compatibility package field {name} is required.");
        }
    }
}
