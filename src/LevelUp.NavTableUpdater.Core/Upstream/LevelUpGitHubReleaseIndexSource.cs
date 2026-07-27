using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LevelUp.NavTableUpdater.Core.Upstream;

public sealed class LevelUpGitHubReleaseIndexSource : IAircraftUpdateIndexSource
{
    public const string Family = "levelup-737ng";
    public const string Repository = "petrolpram/737NG-Updates";
    public const string DefaultIndexUrl =
        "https://github.com/petrolpram/737NG-Updates/releases/latest/download/release-index.json";

    private const int MaximumIndexBytes = 1024 * 1024;
    private const int MaximumManifestBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly Version _toolkitVersion;

    public LevelUpGitHubReleaseIndexSource(
        HttpClient httpClient,
        Version toolkitVersion,
        string indexUrl = DefaultIndexUrl)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(toolkitVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(indexUrl);
        if (!Uri.TryCreate(indexUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("LevelUp release index URL must use HTTPS.", nameof(indexUrl));
        }

        _httpClient = httpClient;
        _toolkitVersion = toolkitVersion;
        IndexUrl = indexUrl;
    }

    public string IndexUrl { get; }

    public async Task<AircraftUpdateIndex> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var indexBytes = await DownloadBytesAsync(
            IndexUrl,
            MaximumIndexBytes,
            cancellationToken);
        var document = Deserialize<ReleaseIndexDocument>(indexBytes, "release index");
        ValidateIndex(document);

        var releaseAssetBaseUrl =
            $"https://github.com/{Repository}/releases/download/"
            + Uri.EscapeDataString(document.ReleaseTag!);
        var packages = new List<AircraftUpdatePackage>();
        var packageTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var packageDocument in document.Packages!)
        {
            ValidatePackageDocument(packageDocument, document, packageTypes);
            var manifestUrl =
                $"{releaseAssetBaseUrl}/{Uri.EscapeDataString(packageDocument.ManifestFile!)}";
            var manifestBytes = await DownloadBytesAsync(
                manifestUrl,
                MaximumManifestBytes,
                cancellationToken);
            VerifySha256(
                manifestBytes,
                packageDocument.ManifestSha256!,
                packageDocument.ManifestFile!);
            var manifest = AircraftUpdatePackageManifestParser.Parse(
                Encoding.UTF8.GetString(manifestBytes),
                manifestUrl);
            ValidateManifestMatchesIndex(manifest, packageDocument, document);

            var kind = packageDocument.PackageType!.Equals(
                "full",
                StringComparison.OrdinalIgnoreCase)
                ? AircraftUpdatePackageKind.FullBaseline
                : AircraftUpdatePackageKind.CumulativePatch;
            var archiveUrl =
                $"{releaseAssetBaseUrl}/{Uri.EscapeDataString(packageDocument.ArchiveFile!)}";
            packages.Add(
                new AircraftUpdatePackage(
                    Family,
                    kind,
                    new AircraftUpstreamVersion(
                        0,
                        0,
                        checked((int)document.ReleaseSequence)),
                    packageDocument.ArchiveFile!,
                    archiveUrl,
                    packageDocument.ReleaseVersion,
                    packageDocument.BaselineVersion,
                    packageDocument.ArchiveSize,
                    packageDocument.ArchiveSha256,
                    manifest));
        }

        return new AircraftUpdateIndex(Family, IndexUrl, packages);
    }

    private async Task<byte[]> DownloadBytesAsync(
        string url,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 0
            && response.Content.Headers.ContentLength > maximumBytes)
        {
            throw new InvalidDataException($"Release metadata exceeds the size limit: {url}");
        }

        await using var input = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > maximumBytes)
            {
                throw new InvalidDataException($"Release metadata exceeds the size limit: {url}");
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static T Deserialize<T>(byte[] bytes, string description)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(bytes, JsonOptions)
                ?? throw new InvalidDataException($"LevelUp {description} is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"LevelUp {description} is invalid JSON: {ex.Message}",
                ex);
        }
    }

    private void ValidateIndex(ReleaseIndexDocument document)
    {
        if (document.SchemaVersion != 1
            || document.ProductId != Family
            || document.Repository != Repository)
        {
            throw new InvalidDataException("Unsupported LevelUp release index identity.");
        }

        if (!Version.TryParse(document.MinimumToolkitVersion, out var minimumToolkitVersion))
        {
            throw new InvalidDataException("LevelUp release index has no valid minimumToolkitVersion.");
        }

        if (_toolkitVersion < minimumToolkitVersion)
        {
            throw new InvalidDataException(
                $"LevelUp release {document.ReleaseVersion} requires toolkit {minimumToolkitVersion} or newer. "
                + $"Installed toolkit version: {_toolkitVersion}.");
        }

        if (document.ReleaseSequence is < 1 or > int.MaxValue
            || string.IsNullOrWhiteSpace(document.ReleaseVersion)
            || string.IsNullOrWhiteSpace(document.ReleaseTag)
            || document.ReleaseTag.Contains('/')
            || document.ReleaseTag.Contains('\\')
            || document.ReleaseChannel is not ("stable" or "beta")
            || document.Packages is null
            || document.Packages.Count == 0)
        {
            throw new InvalidDataException("LevelUp release index metadata is incomplete or unsafe.");
        }
    }

    private static void ValidatePackageDocument(
        ReleasePackageDocument package,
        ReleaseIndexDocument index,
        ISet<string> packageTypes)
    {
        if (package.PackageType is not ("full" or "cumulativePatch")
            || !packageTypes.Add(package.PackageType)
            || package.ReleaseVersion != index.ReleaseVersion
            || !IsSafeAssetFileName(package.ManifestFile)
            || !IsSha256(package.ManifestSha256)
            || !IsSafeAssetFileName(package.ArchiveFile)
            || package.ArchiveSize < 0
            || !IsSha256(package.ArchiveSha256))
        {
            throw new InvalidDataException("LevelUp release package metadata is incomplete or unsafe.");
        }

        if (package.PackageType == "cumulativePatch"
            && (string.IsNullOrWhiteSpace(package.BaselineVersion)
                || package.BaselineAliases is null))
        {
            throw new InvalidDataException("LevelUp cumulative patch baseline metadata is missing.");
        }
    }

    private static void ValidateManifestMatchesIndex(
        AircraftUpdatePackageManifest manifest,
        ReleasePackageDocument package,
        ReleaseIndexDocument index)
    {
        var expectedKind = package.PackageType == "full"
            ? AircraftUpdatePackageKind.FullBaseline
            : AircraftUpdatePackageKind.CumulativePatch;
        if (manifest.ProductId != Family
            || manifest.PackageKind != expectedKind
            || manifest.ReleaseVersion != package.ReleaseVersion
            || manifest.ReleaseSequence != index.ReleaseSequence
            || manifest.Archive.FileName != package.ArchiveFile
            || manifest.Archive.Size != package.ArchiveSize
            || !string.Equals(
                manifest.Archive.Sha256,
                package.ArchiveSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"LevelUp manifest does not match release-index.json: {package.ManifestFile}");
        }

        if (expectedKind == AircraftUpdatePackageKind.CumulativePatch
            && (!VersionsEqual(manifest.BaselineVersion, package.BaselineVersion)
                || !manifest.BaselineAliases.SequenceEqual(
                    package.BaselineAliases!,
                    StringComparer.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                $"LevelUp patch baseline does not match release-index.json: {package.ManifestFile}");
        }
    }

    private static bool IsSafeAssetFileName(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Contains('/')
        && !value.Contains('\\')
        && Path.GetFileName(value) == value
        && value is not "." and not "..";

    private static bool IsSha256(string? value) =>
        value?.Length == 64 && value.All(Uri.IsHexDigit);

    private static void VerifySha256(
        byte[] bytes,
        string expectedSha256,
        string fileName)
    {
        var actual = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"LevelUp release manifest SHA-256 mismatch: {fileName}");
        }
    }

    private static bool VersionsEqual(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(
            left.Trim().TrimStart('v', 'V'),
            right.Trim().TrimStart('v', 'V'),
            StringComparison.OrdinalIgnoreCase);

    private sealed class ReleaseIndexDocument
    {
        public int SchemaVersion { get; set; }
        public string? ProductId { get; set; }
        public string? Repository { get; set; }
        public string? ReleaseVersion { get; set; }
        public long ReleaseSequence { get; set; }
        public string? ReleaseTag { get; set; }
        public string? ReleaseChannel { get; set; }
        public string? MinimumToolkitVersion { get; set; }
        public List<ReleasePackageDocument>? Packages { get; set; }
    }

    private sealed class ReleasePackageDocument
    {
        public string? PackageType { get; set; }
        public string? ReleaseVersion { get; set; }
        public string? BaselineVersion { get; set; }
        public List<string>? BaselineAliases { get; set; }
        public string? ManifestFile { get; set; }
        public string? ManifestSha256 { get; set; }
        public string? ArchiveFile { get; set; }
        public long ArchiveSize { get; set; }
        public string? ArchiveSha256 { get; set; }
    }
}
