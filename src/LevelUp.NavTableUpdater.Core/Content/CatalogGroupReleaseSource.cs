using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LevelUp.NavTableUpdater.Core.Manifest;

namespace LevelUp.NavTableUpdater.Core.Content;

public sealed record CatalogGroupResolution(ContentPackageCatalogEntry Group,
    IReadOnlyList<(CatalogGroupMember Member, ContentPackageCatalogEntry Entry, ContentPatchRelease Release)> Sources,
    string Identity)
{
    // Internal selection identity, never a separately published release.
    public ContentPatchRelease Selection => new(Identity, Group.RepositoryUrl, "", "", 0, "");
}

public sealed partial class GitHubContentPatchReleaseSource
{
    private static readonly JsonSerializerOptions GroupJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task<CatalogGroupResolution> ResolveGroupAsync(ContentPackageCatalog catalog,
        ContentPackageCatalogEntry group, CancellationToken cancellationToken = default)
    {
        if (group.Distribution.Kind is not ContentPackageDistributionKind.CatalogGroup)
            throw new InvalidOperationException("Expected a catalog group.");
        var sources = new List<(CatalogGroupMember, ContentPackageCatalogEntry, ContentPatchRelease)>();
        foreach (var member in group.Members.OrderBy(m => m.InstallationOrder))
        {
            var entry = catalog.Packages.Single(p => p.PackageId == member.PackageId);
            var download = DownloadEntry(entry, member);
            var release = await GetLatestAsync(download, cancellationToken).ConfigureAwait(false);
            sources.Add((member, entry, release));
        }
        var identityBytes = JsonSerializer.SerializeToUtf8Bytes(new { group, releases = sources.Select(s => s.Item3) }, GroupJson);
        var identity = "catalog-" + Convert.ToHexString(SHA256.HashData(identityBytes)).ToLowerInvariant();
        return new(group, sources, identity);
    }

    private static ContentPackageCatalogEntry DownloadEntry(ContentPackageCatalogEntry entry, CatalogGroupMember member) => new()
    {
        PackageId = entry.PackageId, RepositoryUrl = entry.RepositoryUrl,
        Category = ContentPackageCategory.CompatibilityPackage, SupportedProducts = entry.SupportedProducts,
        Distribution = new() { Kind = ContentPackageDistributionKind.GitHubReleaseArchive,
            AssetNamePattern = member.AssetNamePattern, ManifestSchemaVersion = 3 }
    };

    public async Task<CompatibilityPackageProvisionResult> ProvisionGroupAsync(CatalogGroupResolution resolved,
        CancellationToken cancellationToken = default)
    {
        var root = Path.Combine(_cacheRoot, "catalog-groups", SanitizeSegment(resolved.Group.PackageId));
        CreateSafeCacheDirectory(root);
        var staging = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var manifest = new CompatibilityPackageManifest
            {
                SchemaVersion = 3, PackageType = "compatibilityPackage", PackageId = resolved.Group.PackageId,
                PackageVersion = resolved.Identity, RepositoryUrl = resolved.Group.RepositoryUrl,
                AircraftFamily = resolved.Group.DisplayName, SupportedProducts = resolved.Group.SupportedProducts,
                RestartRequired = resolved.Group.RestartRequired
            };
            foreach (var (member, entry, release) in resolved.Sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var archivePath = Path.Combine(staging, member.ModuleId + ".zip");
                await DownloadArchiveAsync(release, archivePath, cancellationToken).ConfigureAwait(false);
                var files = ReadGroupArchive(archivePath, cancellationToken);
                var moduleRoot = Path.Combine(staging, "modules", member.ModuleId);
                Directory.CreateDirectory(moduleRoot);
                var module = CatalogSourceAdapter.Convert(member, entry, release, files, moduleRoot);
                manifest.Modules.Add(module);
                manifest.Sources.Add(new() { PackageId = entry.PackageId, ModuleId = member.ModuleId,
                    ReleaseTag = release.Tag, AssetSha256 = release.AssetSha256, RepositoryUrl = entry.RepositoryUrl });
                File.Delete(archivePath);
            }
            File.WriteAllText(Path.Combine(staging, "package-manifest.json"), JsonSerializer.Serialize(manifest, GroupJson));
            var package = CompatibilityPackageLoader.LoadDirectory(staging);
            return new(package, staging, resolved.Selection, true);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException)
        {
            Directory.Delete(staging, recursive: true);
            throw new InvalidDataException("A catalog source has malformed manifest metadata.", ex);
        }
        catch
        {
            Directory.Delete(staging, recursive: true);
            throw;
        }
    }

    private static IReadOnlyDictionary<string, byte[]> ReadGroupArchive(string path, CancellationToken token)
    {
        using var archive = ZipFile.OpenRead(path);
        if (archive.Entries.Count > MaximumArchiveEntries) throw new InvalidDataException("Too many archive entries.");
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long size = 0;
        foreach (var entry in archive.Entries)
        {
            var name = NormalizeArchivePath(entry.FullName);
            if (!names.Add(name) || IsSymbolicLink(entry)) throw new InvalidDataException("Unsafe or duplicate archive entry.");
            size += entry.Length;
            if (size > MaximumExpandedBytes || entry.Length > MaximumArchiveBytes) throw new InvalidDataException("Archive exceeds size limit.");
            if (entry.Name.Length != 0) files.Add(name, ReadEntry(entry, (int)MaximumArchiveBytes, token));
        }
        return files;
    }
}
