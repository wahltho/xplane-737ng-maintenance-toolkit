using System.Security.Cryptography;
using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Manifest;

namespace LevelUp.NavTableUpdater.Core.Content;

public sealed record DeclarativePatchPayloadContent(
    string Path,
    byte[] Bytes,
    JsonDocument Json,
    string Source);

public sealed record DeclarativePatchPackage(
    DeclarativePatchManifest Manifest,
    IReadOnlyDictionary<string, DeclarativePatchPayloadContent> Payloads);

public static class DeclarativePatchPackageLoader
{
    public static DeclarativePatchPackage LoadDirectory(string packageDirectory)
    {
        var root = Path.GetFullPath(packageDirectory);
        var manifestPath = Path.Combine(root, "package-manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Declarative patch package has no package-manifest.json.", manifestPath);
        }

        var manifest = DeclarativePatchManifestParser.Parse(File.ReadAllText(manifestPath));
        var payloads = new Dictionary<string, DeclarativePatchPayloadContent>(StringComparer.Ordinal);
        foreach (var definition in manifest.Payloads)
        {
            var payloadPath = ResolveContained(root, definition.Path);
            if (!File.Exists(payloadPath))
            {
                throw new FileNotFoundException($"Patch payload is missing: {definition.Path}.", payloadPath);
            }

            var bytes = File.ReadAllBytes(payloadPath);
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (bytes.LongLength != definition.Size || !hash.Equals(definition.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Patch payload failed size/SHA-256 validation: {definition.Path}.");
            }

            JsonDocument json;
            try
            {
                json = JsonDocument.Parse(bytes);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Patch payload is not valid JSON: {definition.Path}.", ex);
            }

            payloads[definition.Path] = new DeclarativePatchPayloadContent(definition.Path, bytes, json, payloadPath);
        }

        return new DeclarativePatchPackage(manifest, payloads);
    }

    private static string ResolveContained(string root, string relativePath)
    {
        return ContentPatchPathSafety.ResolveTarget(root, relativePath, "Patch payload");
    }
}
