using System.Security.Cryptography;
using System.Text;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.State;
using LevelUp.NavTableUpdater.Core.Tools;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class XPlaneOverlayPackageManagerTests
{
    [Fact]
    public void InstallAndRestore_PreserveUnownedProfilesAndGeneratedLogs()
    {
        using var fixture = new Fixture();
        fixture.WriteXPlaneFile("Resources/plugins/DataRefMonitor/profiles/personal.cfg", "personal profile");
        fixture.WriteXPlaneFile("Output/DataRefMonitor/flight.log", "user flight log");
        var package = fixture.CreatePackage("0.1.3", "new plugin", "realbench profile", "logger prefs");

        var installed = fixture.Manager.Apply(fixture.Catalog, package, fixture.XPlaneRoot, ToolPackageAction.Install);
        var inspected = fixture.Manager.Inspect(fixture.Catalog, fixture.XPlaneRoot, package.Release);
        var restored = fixture.Manager.Restore(fixture.Catalog, fixture.XPlaneRoot);

        Assert.True(installed.Succeeded);
        Assert.Equal(ToolPackageInstallState.Current, inspected.State);
        Assert.True(restored.Succeeded);
        Assert.False(File.Exists(fixture.XPlaneFile("Resources/plugins/DataRefMonitor/64/mac.xpl")));
        Assert.False(File.Exists(fixture.XPlaneFile("Output/preferences/DataRefMonitor.prf")));
        Assert.Equal("personal profile", fixture.ReadXPlaneFile("Resources/plugins/DataRefMonitor/profiles/personal.cfg"));
        Assert.Equal("user flight log", fixture.ReadXPlaneFile("Output/DataRefMonitor/flight.log"));
        Assert.Equal(
            ToolPackageInstallState.NotInstalled,
            fixture.Manager.Inspect(fixture.Catalog, fixture.XPlaneRoot, package.Release).State);
    }

    [Fact]
    public void UpdateAndRestore_RestoreExactPreviousManagedFilesOnly()
    {
        using var fixture = new Fixture();
        fixture.WriteXPlaneFile("Resources/plugins/DataRefMonitor/64/mac.xpl", "old plugin");
        fixture.WriteXPlaneFile("Resources/plugins/DataRefMonitor/profiles/zibomod-realbench.cfg", "old profile");
        fixture.WriteXPlaneFile("Output/preferences/DataRefMonitor.prf", "old user prefs");
        fixture.WriteXPlaneFile("Output/DataRefMonitor/keep.log", "keep log");
        var package = fixture.CreatePackage("0.1.3", "new plugin", "new profile", "new prefs");

        var applied = fixture.Manager.Apply(fixture.Catalog, package, fixture.XPlaneRoot, ToolPackageAction.Update);
        var restored = fixture.Manager.Restore(fixture.Catalog, fixture.XPlaneRoot);

        Assert.True(applied.Succeeded);
        Assert.True(restored.Succeeded);
        Assert.Equal("old plugin", fixture.ReadXPlaneFile("Resources/plugins/DataRefMonitor/64/mac.xpl"));
        Assert.Equal("old profile", fixture.ReadXPlaneFile("Resources/plugins/DataRefMonitor/profiles/zibomod-realbench.cfg"));
        Assert.Equal("old user prefs", fixture.ReadXPlaneFile("Output/preferences/DataRefMonitor.prf"));
        Assert.Equal("keep log", fixture.ReadXPlaneFile("Output/DataRefMonitor/keep.log"));
    }

    [Fact]
    public void Repair_ReplacesChangedOwnedFileWithoutTouchingGeneratedLogs()
    {
        using var fixture = new Fixture();
        var package = fixture.CreatePackage("0.1.3", "new plugin", "new profile", "new prefs");
        fixture.Manager.Apply(fixture.Catalog, package, fixture.XPlaneRoot, ToolPackageAction.Install);
        fixture.WriteXPlaneFile("Resources/plugins/DataRefMonitor/64/mac.xpl", "corrupt");
        fixture.WriteXPlaneFile("Output/DataRefMonitor/keep.log", "keep log");

        var inspection = fixture.Manager.Inspect(fixture.Catalog, fixture.XPlaneRoot, package.Release);
        var repaired = fixture.Manager.Apply(fixture.Catalog, package, fixture.XPlaneRoot, ToolPackageAction.Repair);

        Assert.Equal(ToolPackageInstallState.RepairRequired, inspection.State);
        Assert.True(repaired.Succeeded);
        Assert.Equal("new plugin", fixture.ReadXPlaneFile("Resources/plugins/DataRefMonitor/64/mac.xpl"));
        Assert.Equal("keep log", fixture.ReadXPlaneFile("Output/DataRefMonitor/keep.log"));
    }

    [Fact]
    public void Restore_WhenOwnedFileChangedAfterInstall_IsBlocked()
    {
        using var fixture = new Fixture();
        var package = fixture.CreatePackage("0.1.3", "new plugin", "new profile", "new prefs");
        fixture.Manager.Apply(fixture.Catalog, package, fixture.XPlaneRoot, ToolPackageAction.Install);
        fixture.WriteXPlaneFile("Output/preferences/DataRefMonitor.prf", "user changed after install");

        var restored = fixture.Manager.Restore(fixture.Catalog, fixture.XPlaneRoot);

        Assert.False(restored.Succeeded);
        Assert.Equal("user changed after install", fixture.ReadXPlaneFile("Output/preferences/DataRefMonitor.prf"));
    }

    [Fact]
    public void Apply_WhenXPlaneRuns_IsBlockedBeforeChanges()
    {
        using var fixture = new Fixture(xPlaneRunning: true);
        var package = fixture.CreatePackage("0.1.3", "new plugin", "new profile", "new prefs");

        var result = fixture.Manager.Apply(fixture.Catalog, package, fixture.XPlaneRoot, ToolPackageAction.Install);

        Assert.False(result.Succeeded);
        Assert.False(File.Exists(fixture.XPlaneFile("Resources/plugins/DataRefMonitor/64/mac.xpl")));
    }

    [Fact]
    public void ManifestParser_RejectsPackageFileInsideProtectedGeneratedData()
    {
        var json = """
        {
          "schemaVersion": 2,
          "packageId": "wahltho.737ng-realbench-logger",
          "packageVersion": "0.1.3",
          "releaseTag": "v0.1.3",
          "channel": "stable",
          "repository": "https://github.com/wahltho/Zibo-LevelUp-Realbench-Logger",
          "installScope": "xPlaneInstallation",
          "layout": "xPlaneOverlay",
          "targetPath": "",
          "supportedProducts": ["zibo-737ng", "levelup-737ng"],
          "restartRequired": true,
          "archive": { "fileName": "logger.zip", "rootPath": "", "size": 1, "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
          "protectedPaths": ["Output/DataRefMonitor/**"],
          "files": [
            { "path": "Output/DataRefMonitor/owned.log", "size": 1, "sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" }
          ]
        }
        """;

        Assert.Throws<InvalidDataException>(() => ToolPackageManifestParser.Parse(Encoding.UTF8.GetBytes(json)));
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture(bool xPlaneRunning = false)
        {
            Root = Path.Combine(Path.GetTempPath(), $"xplane-overlay-package-tests-{Guid.NewGuid():N}");
            XPlaneRoot = Path.Combine(Root, "X-Plane 12");
            Directory.CreateDirectory(Path.Combine(XPlaneRoot, "Aircraft"));
            Directory.CreateDirectory(Path.Combine(XPlaneRoot, "Resources"));
            StateStore = TestToolStateStore.Create(Root);
            Manager = new XPlaneOverlayPackageManager(StateStore, () => xPlaneRunning);
        }

        public string Root { get; }

        public string XPlaneRoot { get; }

        public ToolStateStore StateStore { get; }

        public XPlaneOverlayPackageManager Manager { get; }

        public ContentPackageCatalogEntry Catalog { get; } = new()
        {
            PackageId = "wahltho.737ng-realbench-logger",
            DisplayName = "737NG Realbench Logger",
            Description = "Test overlay tool",
            Category = ContentPackageCategory.Tool,
            Activation = ContentPatchActivation.ExplicitOptIn,
            SupportedProducts = ["zibo-737ng", "levelup-737ng"],
            RepositoryUrl = "https://github.com/wahltho/Zibo-LevelUp-Realbench-Logger",
            RestartRequired = true,
            InstallScope = "xPlaneInstallation",
            TargetPath = "",
            SupportedChannels = ["stable"],
            Distribution = new ContentPackageDistribution
            {
                Kind = ContentPackageDistributionKind.GitHubXPlaneOverlayRelease,
                ManifestAssetNamePattern = "737NGRealbenchLogger-*-manifest.json",
                ManifestSchemaVersion = 2
            }
        };

        public ToolPackageProvisionResult CreatePackage(
            string version,
            string plugin,
            string profile,
            string preferences)
        {
            var packageRoot = Path.Combine(Root, "package", Guid.NewGuid().ToString("N"));
            var files = new Dictionary<string, string>
            {
                ["Resources/plugins/DataRefMonitor/64/mac.xpl"] = plugin,
                ["Resources/plugins/DataRefMonitor/profiles/zibomod-realbench.cfg"] = profile,
                ["Output/preferences/DataRefMonitor.prf"] = preferences
            };
            foreach (var file in files)
            {
                Write(Path.Combine(packageRoot, file.Key.Replace('/', Path.DirectorySeparatorChar)), file.Value);
            }

            var manifest = new ToolPackageManifest
            {
                SchemaVersion = 2,
                PackageId = Catalog.PackageId,
                PackageVersion = version,
                ReleaseTag = "v" + version,
                Channel = "stable",
                Repository = Catalog.RepositoryUrl,
                InstallScope = "xPlaneInstallation",
                Layout = "xPlaneOverlay",
                TargetPath = "",
                SupportedProducts = [.. Catalog.SupportedProducts],
                RestartRequired = true,
                Archive = new ToolPackageArchive
                {
                    FileName = $"737NGRealbenchLogger-{version}-toolkit.zip",
                    RootPath = "",
                    Size = 1,
                    Sha256 = new string('a', 64)
                },
                ProtectedPaths = ["Output/DataRefMonitor/**"],
                Files = files.Select(file => FileManifest(file.Key, file.Value)).ToList()
            };
            var release = new ToolPackageRelease(
                ToolReleaseChannel.Stable,
                manifest.ReleaseTag,
                $"{Catalog.RepositoryUrl}/releases/tag/{manifest.ReleaseTag}",
                $"737NGRealbenchLogger-{version}-manifest.json",
                $"{Catalog.RepositoryUrl}/releases/download/{manifest.ReleaseTag}/737NGRealbenchLogger-{version}-manifest.json",
                1,
                new string('b', 64),
                $"{Catalog.RepositoryUrl}/releases/download/{manifest.ReleaseTag}/{manifest.Archive.FileName}",
                manifest);
            return new ToolPackageProvisionResult(release, packageRoot, Downloaded: false);
        }

        public string XPlaneFile(string relativePath) =>
            Path.Combine(XPlaneRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        public void WriteXPlaneFile(string relativePath, string content) => Write(XPlaneFile(relativePath), content);

        public string ReadXPlaneFile(string relativePath) => File.ReadAllText(XPlaneFile(relativePath));

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static ToolPackageFile FileManifest(string path, string content)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            return new ToolPackageFile
            {
                Path = path,
                Size = bytes.LongLength,
                Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
            };
        }

        private static void Write(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }
    }
}
