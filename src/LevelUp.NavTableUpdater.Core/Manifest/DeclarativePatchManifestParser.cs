using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Aircraft;

namespace LevelUp.NavTableUpdater.Core.Manifest;

public static class DeclarativePatchManifestParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static DeclarativePatchManifest Parse(string json)
    {
        DeclarativePatchManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<DeclarativePatchManifest>(json, JsonOptions)
                ?? throw new InvalidOperationException("Patch manifest is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Patch manifest JSON is invalid: {ex.Message}", ex);
        }

        Validate(manifest);
        return manifest;
    }

    public static void Validate(DeclarativePatchManifest manifest)
    {
        manifest.SupportedProducts ??= [];
        manifest.SupportedUpstreamReleases ??= [];
        manifest.Payloads ??= [];
        manifest.Targets ??= [];
        if (manifest.SchemaVersion != 2)
        {
            throw new InvalidOperationException($"Unsupported declarative patch manifest schema {manifest.SchemaVersion}.");
        }

        Require(manifest.PackageId, "packageId");
        Require(manifest.PackageVersion, "packageVersion");
        Require(manifest.RepositoryUrl, "repositoryUrl");
        Require(manifest.AircraftFamily, "aircraftFamily");
        if (!Uri.TryCreate(manifest.RepositoryUrl, UriKind.Absolute, out var repositoryUri)
            || repositoryUri.Scheme is not ("https" or "http"))
        {
            throw new InvalidOperationException("repositoryUrl must be an absolute HTTP(S) URL.");
        }

        if (manifest.SupportedProducts.Count != manifest.SupportedProducts.Distinct(StringComparer.Ordinal).Count())
        {
            throw new InvalidOperationException("supportedProducts must not contain duplicate product IDs.");
        }

        foreach (var productId in manifest.SupportedProducts)
        {
            if (!AircraftProductIds.IsSupported(productId))
            {
                throw new InvalidOperationException($"Unsupported product ID in supportedProducts: {productId}.");
            }
        }

        if (manifest.Payloads.Count == 0 || manifest.Targets.Count == 0)
        {
            throw new InvalidOperationException("Declarative patch manifest must declare payloads and targets.");
        }

        var payloads = new Dictionary<string, DeclarativePatchPayload>(StringComparer.Ordinal);
        foreach (var payload in manifest.Payloads)
        {
            ValidateRelativePath(payload.Path, "payload path");
            if (!payloads.TryAdd(payload.Path, payload))
            {
                throw new InvalidOperationException($"Duplicate payload path: {payload.Path}.");
            }

            if (payload.Size < 0 || !IsSha256(payload.Sha256))
            {
                throw new InvalidOperationException($"Payload {payload.Path} has invalid size or SHA-256 metadata.");
            }
        }

        var targetPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in manifest.Targets)
        {
            Require(target.Operation, "target operation");
            ValidateRelativePath(target.RelativePath, "target path");
            ValidateRelativePath(target.Payload, "target payload path");
            if (!targetPaths.Add(target.RelativePath))
            {
                throw new InvalidOperationException($"Duplicate patch target path: {target.RelativePath}.");
            }

            if (!payloads.ContainsKey(target.Payload))
            {
                throw new InvalidOperationException($"Target {target.RelativePath} references undeclared payload {target.Payload}.");
            }

            if (target.SourceSha256.Any(hash => !IsSha256(hash)))
            {
                throw new InvalidOperationException($"Target {target.RelativePath} declares an invalid source SHA-256 value.");
            }

            if (target.ResultSha256 is not null && !IsSha256(target.ResultSha256))
            {
                throw new InvalidOperationException($"Target {target.RelativePath} has an invalid result SHA-256.");
            }
        }
    }

    internal static void ValidateRelativePath(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value))
        {
            throw new InvalidOperationException($"Unsafe {label}: {value}.");
        }

        var parts = value.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(part => part is "." or ".."))
        {
            throw new InvalidOperationException($"Unsafe {label}: {value}.");
        }
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static void Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Patch manifest field {name} is required.");
        }
    }
}
