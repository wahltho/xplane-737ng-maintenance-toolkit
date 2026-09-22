using System.Text.Json;
using System.Text.Json.Serialization;
using LevelUp.NavTableUpdater.Core.Aircraft;

namespace LevelUp.NavTableUpdater.Core.Manifest;

public static class CompatibilityPackageManifestParser
{
    public const int CurrentSchemaVersion = 4;
    public const int LegacySchemaVersion = 3;
    public static bool SupportsSchema(int version) => version is LegacySchemaVersion or CurrentSchemaVersion;
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
            using var document = JsonDocument.Parse(json);
            if (TryProperty(document.RootElement, "schemaVersion", out var schema)
                && schema.TryGetInt32(out var version) && version == LegacySchemaVersion
                && TryProperty(document.RootElement, "modules", out var modules)
                && modules.ValueKind == JsonValueKind.Array
                && modules.EnumerateArray().Any(module =>
                    (TryProperty(module, "retiredFiles", out var retired)
                        && retired.ValueKind == JsonValueKind.Array && retired.GetArrayLength() > 0)
                    || (TryProperty(module, "managedScopes", out var scopes)
                        && scopes.ValueKind == JsonValueKind.Array && scopes.GetArrayLength() > 0)))
                throw new InvalidOperationException("Schema 4 is required for retiredFiles and managedScopes.");
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
        manifest.Sources ??= [];
        if (!SupportsSchema(manifest.SchemaVersion)
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
        var scopePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in manifest.Modules)
        {
            module.SupportedUpstreamReleases ??= [];
            module.Requires ??= [];
            module.ConflictsWith ??= [];
            module.Payloads ??= [];
            module.Targets ??= [];
            module.RetiredFiles ??= [];
            module.ManagedScopes ??= [];
            if (manifest.SchemaVersion == LegacySchemaVersion
                && (module.RetiredFiles.Count != 0 || module.ManagedScopes.Count != 0))
                throw new InvalidOperationException("Schema 4 is required for retiredFiles and managedScopes.");
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
            ValidateManagedContent(module, scopePaths);
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

        if (manifest.SchemaVersion == CurrentSchemaVersion)
        {
            var allRetirements = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allTargets = manifest.Modules.SelectMany(module => module.Targets)
                .Select(target => target.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var module in manifest.Modules)
                foreach (var retired in module.RetiredFiles)
                    if (!allRetirements.Add(retired.RelativePath) || allTargets.Contains(retired.RelativePath))
                        throw new InvalidOperationException($"Compatibility package has conflicting ownership for retired file {retired.RelativePath}.");

            foreach (var scope in manifest.Modules.SelectMany(module => module.ManagedScopes))
            {
                var prefix = scope.RelativePath.TrimEnd('/') + "/";
                foreach (var module in manifest.Modules)
                {
                    if (module.ManagedScopes.Any(owned => owned.RelativePath == scope.RelativePath)) continue;
                    if (module.Targets.Any(target => target.RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        || module.RetiredFiles.Any(retired => retired.RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException($"Managed scope {scope.RelativePath} has paths owned by another module.");
                }
            }
        }
    }

    private static bool TryProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        value = default;
        return false;
    }

    private static void ValidateManagedContent(CompatibilityPackageModule module, HashSet<string> scopePaths)
    {
        var retirements = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var retired in module.RetiredFiles)
        {
            ValidateCanonicalPath(retired.RelativePath, $"module {module.ModuleId} retired path");
            if (!retirements.Add(retired.RelativePath) || retired.SourceSha256 is null
                || retired.SourceSha256.Count == 0
                || retired.SourceSha256.Any(hash => !IsSha256(hash)))
                throw new InvalidOperationException($"Module {module.ModuleId} has invalid retired-file metadata.");
        }

        foreach (var scope in module.ManagedScopes)
        {
            ValidateCanonicalPath(scope.RelativePath, $"module {module.ModuleId} managed scope");
            if (scope.Mode != "flatExclusive" || !scopePaths.Add(scope.RelativePath))
                throw new InvalidOperationException($"Module {module.ModuleId} has an invalid or overlapping managed scope.");
            var prefix = scope.RelativePath.TrimEnd('/') + "/";
            if (scopePaths.Any(path => path != scope.RelativePath
                && (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    || scope.RelativePath.StartsWith(path.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))))
                throw new InvalidOperationException("Managed scopes must not overlap.");
            var copies = module.Targets.Where(t => t.RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (copies.Any(t => t.Operation != "copy-file-v1"
                    || !t.RelativePath.StartsWith(prefix, StringComparison.Ordinal)
                    || t.RelativePath[prefix.Length..].Contains('/'))
                || module.RetiredFiles.Where(r => r.RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .Any(r => !r.RelativePath.StartsWith(prefix, StringComparison.Ordinal)
                        || r.RelativePath[prefix.Length..].Contains('/')))
                throw new InvalidOperationException($"Managed scope {scope.RelativePath} accepts only flat copy targets and retirements.");
            if (copies.Length == 0)
                throw new InvalidOperationException($"Managed scope {scope.RelativePath} has no copy targets.");
            foreach (var copy in copies)
                ValidateCanonicalPath(copy.RelativePath, $"module {module.ModuleId} scoped copy path");
        }

        var targetPaths = module.Targets.Select(t => t.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (module.RetiredFiles.Any(r => targetPaths.Contains(r.RelativePath)))
            throw new InvalidOperationException($"Module {module.ModuleId} cannot copy and retire the same path.");
        if (module.RetiredFiles.Any(r => !module.ManagedScopes.Any(s =>
                r.RelativePath.StartsWith(s.RelativePath.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)
                && !r.RelativePath[(s.RelativePath.TrimEnd('/').Length + 1)..].Contains('/'))))
            throw new InvalidOperationException($"Module {module.ModuleId} retirement must be inside a flat managed scope.");
    }

    private static void ValidateCanonicalPath(string path, string label)
    {
        DeclarativePatchManifestParser.ValidateRelativePath(path, label);
        if (path.Contains('\\') || path.Split('/').Any(part => part.Length == 0 || part is "." or ".."))
            throw new InvalidOperationException($"Unsafe or noncanonical {label}: {path}.");
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
