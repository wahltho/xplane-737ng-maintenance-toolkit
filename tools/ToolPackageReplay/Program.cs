using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.State;
using LevelUp.NavTableUpdater.Core.Tools;

// Offline integration replay: exact published archive, prepared manifest, real production engines.
// Never loads the plugin or changes a real X-Plane installation.
if (args.Length != 4)
    throw new ArgumentException("Usage: ToolPackageReplay <catalog.json> <manifest.json> <archive.zip> <new-evidence-directory>");
var output = Path.GetFullPath(args[3]);
if (Directory.Exists(output)) throw new IOException("Evidence directory must not already exist.");
var manifestBytes = File.ReadAllBytes(args[1]);
var archiveBytes = File.ReadAllBytes(args[2]);
var manifest = ToolPackageManifestParser.Parse(manifestBytes);
var entry = ContentPackageCatalog.Parse(File.ReadAllText(args[0]), new Version(0, 14, 0))
    .Packages.Single(p => p.PackageId == manifest.PackageId);
if (manifest.Layout != "directory" || manifest.InstallScope != "xPlaneInstallation")
    throw new InvalidDataException("Replay supports directory tools installed into X-Plane only.");
var platform = manifest.SupportedPlatforms.Single();
var manifestName = Path.GetFileName(args[1]);
var repository = entry.RepositoryUrl;
var tag = manifest.ReleaseTag;
var manifestUrl = $"{repository}/releases/download/{tag}/{manifestName}";
var archiveUrl = $"{repository}/releases/download/{tag}/{manifest.Archive.FileName}";
var metadata = JsonSerializer.SerializeToUtf8Bytes(new
{
    tag_name = tag, html_url = $"{repository}/releases/tag/{tag}", draft = false, prerelease = false,
    assets = new[]
    {
        new { name = manifestName, browser_download_url = manifestUrl, size = manifestBytes.LongLength, digest = "sha256:" + Hash(manifestBytes) },
        new { name = manifest.Archive.FileName, browser_download_url = archiveUrl, size = archiveBytes.LongLength, digest = "sha256:" + Hash(archiveBytes) }
    }
});
using var http = new HttpClient(new ReplayHandler(new Dictionary<string, byte[]>
{
    [$"https://api.github.com/repos/{new Uri(repository).AbsolutePath.Trim('/')}/releases/latest"] = metadata,
    [manifestUrl] = manifestBytes,
    [archiveUrl] = archiveBytes
}));
var source = new GitHubToolPackageReleaseSource(http, Path.Combine(output, "cache"), platform);
var release = await source.GetLatestAsync(entry, ToolReleaseChannel.Stable) ?? throw new Exception("Release missing");
var package = await source.ProvisionAsync(entry, release);
var xplane = Path.Combine(output, "X-Plane-test");
Directory.CreateDirectory(Path.Combine(xplane, "Aircraft"));
Directory.CreateDirectory(Path.Combine(xplane, "Resources"));
var store = new ToolStateStore(Path.Combine(output, "state"), Path.Combine(output, "backups"));
var manager = new ToolPackageManager(store, () => false, platform);
var target = Path.Combine(xplane, manifest.TargetPath);
var checks = new List<string>();
void Check(bool passed, string description)
{
    if (!passed) throw new Exception("FAILED: " + description);
    checks.Add(description);
    Console.WriteLine("PASS " + description);
}
void VerifyFiles()
{
    foreach (var file in manifest.Files)
        Check(Hash(File.ReadAllBytes(Path.Combine(target, file.Path))) == file.Sha256, "Installed hash: " + file.Path);
}
Check(manager.Apply(entry, package, xplane, ToolPackageAction.Install).Succeeded, "Install exact released payload");
VerifyFiles();
Check(manager.Inspect(entry, xplane, release).State == ToolPackageInstallState.Current, "Detect current version without version marker");
if (!OperatingSystem.IsWindows())
{
    var plugin = manifest.Files.Single(f => f.Path.EndsWith(".xpl", StringComparison.Ordinal));
    Check((File.GetUnixFileMode(Path.Combine(target, plugin.Path)) & UnixFileMode.UserExecute) != 0, "Preserve plugin executable mode");
}
Check(manager.Restore(entry, xplane).Succeeded && !Directory.Exists(target), "Restore absent installation");
// Model an existing manual install with unknown version; update must back up its original bytes.
Directory.CreateDirectory(target);
foreach (var file in manifest.Files)
{
    var path = Path.Combine(target, file.Path);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, "pre-existing bytes: " + file.Path);
}
File.WriteAllText(Path.Combine(target, "personal-notes.txt"), "user data");
Check(manager.Apply(entry, package, xplane, ToolPackageAction.Update).Succeeded, "Update an existing unmanaged installation");
VerifyFiles();
Check(manager.Restore(entry, xplane).Succeeded, "Restore previous installation");
foreach (var file in manifest.Files)
    Check(File.ReadAllText(Path.Combine(target, file.Path)) == "pre-existing bytes: " + file.Path, "Restore original bytes: " + file.Path);
Check(manager.Apply(entry, package, xplane, ToolPackageAction.Update).Succeeded, "Reapply update");
File.WriteAllText(Path.Combine(target, manifest.Files[0].Path), "damaged");
Check(manager.Apply(entry, package, xplane, ToolPackageAction.Repair).Succeeded, "Repair damaged payload");
VerifyFiles();
Check(File.ReadAllText(Path.Combine(target, "personal-notes.txt")) == "user data", "Preserve unowned user file throughout lifecycle");
var report = new
{
    package = manifest.PackageId, version = manifest.PackageVersion,
    archiveSha256 = Hash(archiveBytes), simulatedPlatform = platform,
    host = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
    checks, limitation = "Offline release metadata; filesystem integration on this host only. No Linux X-Plane/Piper runtime test."
};
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
sealed class ReplayHandler(IReadOnlyDictionary<string, byte[]> responses) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (!responses.TryGetValue(request.RequestUri!.AbsoluteUri, out var bytes))
            throw new IOException("Unexpected request: " + request.RequestUri);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    }
}
