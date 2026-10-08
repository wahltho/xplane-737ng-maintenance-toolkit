using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Manifest;
using LevelUp.NavTableUpdater.Core.Aircraft;

namespace LevelUp.NavTableUpdater.Core.Content;

// Package identities and recognition rules are catalog data, never a code registry.
public sealed class PatchOwnershipPolicy
{
    public string PackageId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string RepositoryUrl { get; set; } = "";
    public List<string> SupportedProducts { get; set; } = [];
    public List<string> ModuleIds { get; set; } = [];
    public List<string> TargetPaths { get; set; } = [];
    public List<string> StandaloneEvidencePaths { get; set; } = [];
    public List<string> PayloadPaths { get; set; } = [];
    public List<PatchMarkerNamespace> MarkerNamespaces { get; set; } = [];
    public List<PatchOwnershipSignature> Signatures { get; set; } = [];
    public List<PatchOwnershipFileHash> ResultFiles { get; set; } = [];
    public List<PatchOwnershipFileHash> OriginalFiles { get; set; } = [];
    public string RecoveryInstruction { get; set; } = "";

    internal void Validate()
    {
        if (!ContentPackageCatalog.IsSafePackageId(PackageId)
            || string.IsNullOrWhiteSpace(DisplayName) || string.IsNullOrWhiteSpace(RecoveryInstruction)
            || !Uri.TryCreate(RepositoryUrl, UriKind.Absolute, out var repository)
            || repository.Scheme != "https" || repository.Host != "github.com"
            || repository.UserInfo.Length != 0 || !repository.IsDefaultPort
            || repository.Query.Length != 0 || repository.Fragment.Length != 0
            || repository.AbsolutePath.Trim('/').Split('/').Length != 2
            || SupportedProducts is not { Count: > 0 }
            || SupportedProducts.Any(product => string.IsNullOrWhiteSpace(product) || !AircraftProductIds.IsSupported(product))
            || SupportedProducts.Distinct(StringComparer.Ordinal).Count() != SupportedProducts.Count
            || ModuleIds is null || ModuleIds.Any(id => !ContentPackageCatalog.IsSafePackageId(id))
            || TargetPaths is not { Count: > 0 } || StandaloneEvidencePaths is null
            || PayloadPaths is null || MarkerNamespaces is null || Signatures is null
            || ResultFiles is null || OriginalFiles is null)
            throw new InvalidDataException("Incomplete catalog patch ownership policy.");
        foreach (var path in TargetPaths.Concat(PayloadPaths)) ValidatePattern(path);
        foreach (var path in StandaloneEvidencePaths)
        {
            ValidatePattern(path);
            if (path.Contains('*')) throw new InvalidDataException("Standalone evidence must name an exact path.");
        }
        foreach (var marker in MarkerNamespaces)
        {
            if (marker is null) throw new InvalidDataException("Null marker namespace in ownership policy.");
            DeclarativePatchManifestParser.ValidateRelativePath(marker.RelativePath, "Marker namespace target");
            if (string.IsNullOrWhiteSpace(marker.Namespace) || marker.Namespace.Contains('\n')
                || marker.Namespace.Contains('\r') || marker.Blocks is null || marker.AllowedCommentLines is null
                || marker.Blocks.Any(block => block is null || string.IsNullOrWhiteSpace(block.BeginMarker)
                    || string.IsNullOrWhiteSpace(block.EndMarker) || block.BeginMarker == block.EndMarker
                    || !block.BeginMarker.Contains(marker.Namespace, StringComparison.Ordinal)
                    || !block.EndMarker.Contains(marker.Namespace, StringComparison.Ordinal)
                    || block.BeginMarker.Contains('\n') || block.EndMarker.Contains('\n')
                    || block.BeginMarker.Contains('\r') || block.EndMarker.Contains('\r')
                    || !block.BeginMarker.StartsWith("--", StringComparison.Ordinal)
                    || !block.EndMarker.StartsWith("--", StringComparison.Ordinal))
                || marker.Blocks.SelectMany(block => new[] { block.BeginMarker, block.EndMarker })
                    .Distinct(StringComparer.Ordinal).Count() != marker.Blocks.Count * 2
                || marker.AllowedCommentLines.Any(line => string.IsNullOrWhiteSpace(line)
                    || !line.Contains(marker.Namespace, StringComparison.Ordinal)
                    || !line.StartsWith("--", StringComparison.Ordinal)
                    || line.Contains('\n') || line.Contains('\r')
                    || marker.Blocks.Any(block => block.BeginMarker == line || block.EndMarker == line)))
                throw new InvalidDataException($"Invalid marker namespace for {PackageId}.");
        }
        foreach (var signature in Signatures)
        {
            if (signature is null) throw new InvalidDataException("Null signature in ownership policy.");
            DeclarativePatchManifestParser.ValidateRelativePath(signature.RelativePath, "Patch signature target");
            if (string.IsNullOrWhiteSpace(signature.Text))
                throw new InvalidDataException($"Empty patch signature for {PackageId}.");
        }
        foreach (var file in ResultFiles.Concat(OriginalFiles))
        {
            if (file is null) throw new InvalidDataException("Null file hash in ownership policy.");
            DeclarativePatchManifestParser.ValidateRelativePath(file.RelativePath, "Ownership hash target");
            if (file.Sha256 is not { Count: > 0 } || file.Sha256.Any(hash => hash is null || hash.Length != 64 || !hash.All(char.IsAsciiHexDigit)))
                throw new InvalidDataException($"Invalid ownership file hashes for {PackageId}.");
        }
        if (StandaloneEvidencePaths.Count + PayloadPaths.Count + MarkerNamespaces.Count + Signatures.Count == 0)
            throw new InvalidDataException($"Patch policy has no ownership evidence: {PackageId}.");
        foreach (var path in PayloadPaths.Concat(MarkerNamespaces.Select(marker => marker.RelativePath))
            .Concat(Signatures.Select(signature => signature.RelativePath))
            .Concat(ResultFiles.Concat(OriginalFiles).Select(file => file.RelativePath)))
            if (!TargetPaths.Any(target => PatternCovers(target, path)))
                throw new InvalidDataException($"Ownership policy omits its evidence target: {PackageId}/{path}.");
    }

    internal static void ValidatePattern(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\'))
            throw new InvalidDataException("Ownership paths must use relative forward-slash paths.");
        // A single filename wildcard or a flat exclusive directory; no recursive globbing.
        var flatScope = path.EndsWith("/**", StringComparison.Ordinal);
        var literal = flatScope ? path[..^3] : path.Replace("*", "wildcard", StringComparison.Ordinal);
        if (flatScope && literal.Contains('*'))
            throw new InvalidDataException("A flat ownership scope must name an exact directory.");
        DeclarativePatchManifestParser.ValidateRelativePath(literal, "Ownership path");
        if (!flatScope && (path.Count(ch => ch == '*') > 1 || (path.Contains('*') && !path.Contains('/'))
            || path[..Math.Max(0, path.LastIndexOf('/'))].Contains('*')))
            throw new InvalidDataException($"Unsupported ownership path pattern: {path}.");
    }

    internal static bool PatternCovers(string pattern, string path)
    {
        if (pattern.EndsWith("/**", StringComparison.Ordinal))
            return path.StartsWith(pattern[..^2], StringComparison.OrdinalIgnoreCase)
                || path.Equals(pattern[..^3], StringComparison.OrdinalIgnoreCase);
        var star = pattern.IndexOf('*');
        if (star < 0) return pattern.Equals(path, StringComparison.OrdinalIgnoreCase);
        return path.Length >= pattern.Length - 1
            && path.StartsWith(pattern[..star], StringComparison.OrdinalIgnoreCase)
            && path.EndsWith(pattern[(star + 1)..], StringComparison.OrdinalIgnoreCase)
            && !path[star..(path.Length - (pattern.Length - star - 1))].Contains('/');
    }

    internal static PatchOwnershipPolicy Copy(PatchOwnershipPolicy policy) =>
        JsonSerializer.Deserialize<PatchOwnershipPolicy>(JsonSerializer.Serialize(policy))!;
}

public sealed class PatchMarkerNamespace
{
    public string RelativePath { get; set; } = "";
    public string Namespace { get; set; } = "";
    public List<PatchMarkerPair> Blocks { get; set; } = [];
    public List<string> AllowedCommentLines { get; set; } = [];
}

public sealed class PatchMarkerPair
{
    public string BeginMarker { get; set; } = "";
    public string EndMarker { get; set; } = "";
}

public sealed class PatchOwnershipSignature
{
    public string RelativePath { get; set; } = "";
    public string Text { get; set; } = "";
}

public sealed class PatchOwnershipFileHash
{
    public string RelativePath { get; set; } = "";
    public List<string> Sha256 { get; set; } = [];
}

// A copy of the catalog contract used at installation, retained for offline Restore.
public sealed class ContentPatchOwnershipSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public string CatalogVersion { get; set; } = "";
    public List<PatchOwnershipPolicy> Policies { get; set; } = [];
}

public sealed record ContentPatchOwnershipCheck(
    ContentPatchOwnershipSnapshot Snapshot,
    IReadOnlyDictionary<string, string?> EvidenceHashes,
    IReadOnlyDictionary<string, string> BackupHashes,
    string StateFingerprint,
    string CatalogFingerprint);
