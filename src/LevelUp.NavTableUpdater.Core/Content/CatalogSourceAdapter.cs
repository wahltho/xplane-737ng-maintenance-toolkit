using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Manifest;

namespace LevelUp.NavTableUpdater.Core.Content;

internal static class CatalogSourceAdapter
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static CompatibilityPackageModule Convert(CatalogGroupMember member, ContentPackageCatalogEntry entry,
        ContentPatchRelease release, IReadOnlyDictionary<string, byte[]> archive, string output)
    {
        var matches = archive.Keys.Where(k => k == member.ManifestPath ||
            k.EndsWith("/" + member.ManifestPath, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("Source manifest is missing or ambiguous.");
        var manifestPath = matches[0];
        var prefix = manifestPath[..^member.ManifestPath.Length];
        if (prefix.TrimEnd('/').Contains('/')) throw new InvalidDataException("Unexpected archive nesting.");
        byte[] Read(string path)
        {
            ContentPatchPathSafety.ResolveTarget(output, path, "Source payload");
            return archive.TryGetValue(prefix + path, out var bytes) ? bytes : throw new InvalidDataException($"Missing source payload {path}.");
        }
        var module = new CompatibilityPackageModule
        {
            ModuleId = member.ModuleId, DisplayName = entry.DisplayName, Description = entry.Description,
            Policy = member.Policy, DefaultEnabled = member.Policy == CompatibilityModulePolicy.Required,
            InstallationOrder = member.InstallationOrder
        };
        if (member.SourceFormat == "vnav")
        {
            var manifestText = Encoding.UTF8.GetString(archive[manifestPath]);
            var vnav = ManifestParser.ParsePipeManifest(manifestText);
            ValidateIdentity(entry, release, vnav.PackageId, vnav.RepositoryUrl, vnav.PackageVersion);
            var payloads = new Dictionary<string, byte[]>();
            foreach (var payload in vnav.Payloads)
            {
                var bytes = Read(payload.FileName);
                PackagePayloadValidator.ValidatePayload(payload, bytes, release.AssetUrl);
                payloads.Add(payload.FileName, bytes);
                AddPayload(module, output, payload.FileName, bytes);
                module.Targets.Add(new() { Operation = "copy-file-v1", Payload = payload.FileName,
                    RelativePath = (Path.GetDirectoryName(vnav.TargetRelativePath) + "/" + payload.FileName).Replace('\\', '/'),
                    ResultSha256 = Hash(bytes) });
            }
            var operationBytes = JsonSerializer.SerializeToUtf8Bytes(new { format = "vnav-manifest-v1", manifestText, payloads });
            AddPayload(module, output, "vnav-operation.json", operationBytes);
            module.Targets.Insert(0, new() { Operation = "vnav-manifest-v1", Payload = "vnav-operation.json", RelativePath = vnav.TargetRelativePath });
            return module;
        }
        using var document = JsonDocument.Parse(archive[manifestPath]);
        var root = document.RootElement;
        string version;
        string repository;
        string packageId;
        string payloadPrefix = "";
        if (member.SourceFormat == "moduleSource")
        {
            if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("manifestType").GetString() != "levelup-compatibility-module-source"
                || root.GetProperty("moduleId").GetString() != member.ModuleId)
                throw new InvalidDataException("Unsupported module-source contract.");
            version = root.GetProperty("moduleVersion").GetString()!;
            repository = root.GetProperty("repositoryUrl").GetString()!;
            packageId = entry.PackageId;
            ValidateProducts(root, entry);
            // The module-source contract owns structural/runtime compatibility. Its baseline
            // labels are provenance; it explicitly requires no additional Toolkit ACF gate.
        }
        else if (member.SourceFormat == "declarative")
        {
            var parsed = DeclarativePatchManifestParser.Parse(Encoding.UTF8.GetString(archive[manifestPath]));
            version = parsed.PackageVersion; repository = parsed.RepositoryUrl; packageId = parsed.PackageId;
            var products = DeclarativePatchProductCompatibility.ResolveSupportedProducts(parsed);
            if (!entry.SupportedProducts.All(products.Contains)) throw new InvalidDataException("Source product contract differs from catalog.");
            // Preserve schema-2 planner behavior: product and structural target validation.
        }
        else if (member.SourceFormat == "compatibility")
        {
            var parsed = CompatibilityPackageManifestParser.Parse(Encoding.UTF8.GetString(archive[manifestPath]));
            version = parsed.PackageVersion; repository = parsed.RepositoryUrl; packageId = parsed.PackageId;
            ValidateProducts(root, entry);
            var sourceModule = parsed.Modules.Single(m => m.ModuleId == member.ModuleId);
            root = root.GetProperty("modules").EnumerateArray().Single(m => m.GetProperty("moduleId").GetString() == member.ModuleId);
            module.Requires = sourceModule.Requires; module.ConflictsWith = sourceModule.ConflictsWith;
            module.SupportedUpstreamReleases = parsed.SupportedUpstreamReleases;
            payloadPrefix = "modules/" + member.ModuleId + "/";
        }
        else throw new InvalidDataException("Unsupported source format.");
        ValidateIdentity(entry, release, packageId, repository, version);
        module.Payloads = JsonSerializer.Deserialize<List<DeclarativePatchPayload>>(root.GetProperty("payloads"), Json)!;
        module.Targets = JsonSerializer.Deserialize<List<DeclarativePatchTarget>>(root.GetProperty("targets"), Json)!;
        if (root.TryGetProperty("requires", out var requires)) module.Requires = JsonSerializer.Deserialize<List<string>>(requires, Json)!;
        if (root.TryGetProperty("conflictsWith", out var conflicts)) module.ConflictsWith = JsonSerializer.Deserialize<List<string>>(conflicts, Json)!;
        foreach (var payload in module.Payloads)
        {
            var bytes = Read(payloadPrefix + payload.Path);
            if (bytes.LongLength != payload.Size || Hash(bytes) != payload.Sha256.ToLowerInvariant())
                throw new InvalidDataException($"Invalid source payload checksum: {payload.Path}.");
            Write(output, payload.Path, bytes);
        }
        return module;
    }

    private static void ValidateProducts(JsonElement root, ContentPackageCatalogEntry entry)
    {
        var products = root.GetProperty("supportedProducts").EnumerateArray().Select(p => p.GetString()).ToHashSet();
        if (!entry.SupportedProducts.All(products.Contains)) throw new InvalidDataException("Source product contract differs from catalog.");
    }
    private static void ValidateIdentity(ContentPackageCatalogEntry entry, ContentPatchRelease release, string id, string repository, string version)
    {
        if (id != entry.PackageId || !repository.TrimEnd('/').Equals(entry.RepositoryUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
            || !version.TrimStart('v', 'V').Equals(release.Tag.TrimStart('v', 'V'), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Source identity or version differs from release metadata.");
    }
    private static void AddPayload(CompatibilityPackageModule module, string root, string path, byte[] bytes)
    {
        module.Payloads.Add(new() { Path = path, Size = bytes.LongLength, Sha256 = Hash(bytes) });
        Write(root, path, bytes);
    }
    private static void Write(string root, string path, byte[] bytes)
    {
        var destination = ContentPatchPathSafety.ResolveTarget(root, path, "Prepared catalog module");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllBytes(destination, bytes);
    }
    private static string Hash(byte[] bytes) => System.Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
