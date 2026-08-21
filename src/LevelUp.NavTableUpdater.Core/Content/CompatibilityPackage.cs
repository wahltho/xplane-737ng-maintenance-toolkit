using System.Security.Cryptography;
using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Manifest;

namespace LevelUp.NavTableUpdater.Core.Content;

public sealed record CompatibilityModulePackage(
    CompatibilityPackageModule Module,
    IReadOnlyDictionary<string, DeclarativePatchPayloadContent> Payloads);

public sealed record CompatibilityPackage(
    CompatibilityPackageManifest Manifest,
    IReadOnlyDictionary<string, CompatibilityModulePackage> Modules);

public static class CompatibilityPackageLoader
{
    public static CompatibilityPackage LoadDirectory(string packageDirectory)
    {
        var root = Path.GetFullPath(packageDirectory);
        var manifestPath = Path.Combine(root, "package-manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Compatibility package has no package-manifest.json.", manifestPath);
        }

        var manifest = CompatibilityPackageManifestParser.Parse(File.ReadAllText(manifestPath));
        var modules = new Dictionary<string, CompatibilityModulePackage>(StringComparer.Ordinal);
        foreach (var module in manifest.Modules)
        {
            var rawPayloads = module.Targets
                .Where(target => target.Operation.Equals("copy-file-v1", StringComparison.Ordinal))
                .Select(target => target.Payload)
                .ToHashSet(StringComparer.Ordinal);
            var payloads = new Dictionary<string, DeclarativePatchPayloadContent>(StringComparer.Ordinal);
            foreach (var definition in module.Payloads)
            {
                var relativePath = Path.Combine("modules", module.ModuleId, definition.Path);
                var payloadPath = ContentPatchPathSafety.ResolveTarget(root, relativePath, "Compatibility payload");
                if (!File.Exists(payloadPath))
                {
                    throw new FileNotFoundException(
                        $"Compatibility payload is missing: {module.ModuleId}/{definition.Path}.",
                        payloadPath);
                }

                var bytes = File.ReadAllBytes(payloadPath);
                var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                if (bytes.LongLength != definition.Size
                    || !hash.Equals(definition.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Compatibility payload failed size/SHA-256 validation: {module.ModuleId}/{definition.Path}.");
                }

                JsonDocument? json = null;
                if (!rawPayloads.Contains(definition.Path))
                {
                    try
                    {
                        json = JsonDocument.Parse(bytes);
                    }
                    catch (JsonException ex)
                    {
                        throw new InvalidOperationException(
                            $"Compatibility payload is not valid JSON: {module.ModuleId}/{definition.Path}.",
                            ex);
                    }
                }

                payloads[definition.Path] = new DeclarativePatchPayloadContent(
                    definition.Path,
                    bytes,
                    json,
                    payloadPath);
            }

            modules.Add(module.ModuleId, new CompatibilityModulePackage(module, payloads));
        }

        return new CompatibilityPackage(manifest, modules);
    }

    public static bool IsCompatibilityPackage(string packageDirectory)
    {
        try
        {
            var manifestPath = Path.Combine(Path.GetFullPath(packageDirectory), "package-manifest.json");
            using var json = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
            var root = json.RootElement;
            return root.TryGetProperty("schemaVersion", out var schema)
                && schema.TryGetInt32(out var schemaVersion)
                && schemaVersion == CompatibilityPackageManifestParser.CurrentSchemaVersion
                && root.TryGetProperty("packageType", out var packageType)
                && packageType.ValueEquals(CompatibilityPackageManifestParser.PackageType);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }
}
