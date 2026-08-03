using System.Security.Cryptography;
using System.Text;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.State;
using LevelUp.NavTableUpdater.Core.Tools;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class ToolPackageManagerTests
{
    [Fact]
    public void InstallThenRestore_RestoresAbsentState()
    {
        using var fixture = new Fixture();
        var package = fixture.CreatePackage("4.7", "new plugin", "default config");

        var result = fixture.Manager.Apply(fixture.Catalog, package, fixture.XPlaneRoot, ToolPackageAction.Install);
        var installed = fixture.Manager.Inspect(fixture.Catalog, fixture.XPlaneRoot, package.Release);
        var restored = fixture.Manager.Restore(fixture.Catalog, fixture.XPlaneRoot);

        Assert.True(result.Succeeded);
        Assert.Equal(ToolPackageInstallState.Current, installed.State);
        Assert.True(restored.Succeeded);
        Assert.False(Directory.Exists(fixture.TargetPath));
    }

    [Fact]
    public void Update_PreservesProtectedAndUnownedFiles()
    {
        using var fixture = new Fixture();
        fixture.WriteInstalled("4.6", "old plugin", "user config");
        fixture.WriteTarget("data/output/session.txt", "user output");
        fixture.WriteTarget("local-notes.txt", "keep me");
        var package = fixture.CreatePackage("4.7", "new plugin", "default config");

        var result = fixture.Manager.Apply(fixture.Catalog, package, fixture.XPlaneRoot, ToolPackageAction.Update);

        Assert.True(result.Succeeded);
        Assert.Equal("new plugin", fixture.ReadTarget("data/modules/main.lua"));
        Assert.Equal("user config", fixture.ReadTarget("data/modules/configuration/configuration.ini"));
        Assert.Equal("user output", fixture.ReadTarget("data/output/session.txt"));
        Assert.Equal("keep me", fixture.ReadTarget("local-notes.txt"));
        Assert.NotEmpty(result.BackupPaths);
    }

    [Fact]
    public void Inspect_WhenOwnedFileIsChanged_RequiresRepair()
    {
        using var fixture = new Fixture();
        var package = fixture.CreatePackage("4.7", "new plugin", "default config");
        fixture.Manager.Apply(fixture.Catalog, package, fixture.XPlaneRoot, ToolPackageAction.Install);
        fixture.WriteTarget("data/modules/main.lua", "corrupt");

        var inspection = fixture.Manager.Inspect(fixture.Catalog, fixture.XPlaneRoot, package.Release);

        Assert.Equal(ToolPackageInstallState.RepairRequired, inspection.State);
        Assert.Contains(inspection.Findings, finding => finding.Contains("data/modules/main.lua", StringComparison.Ordinal));
    }

    [Fact]
    public void Inspect_MarkerlessManagedTool_UsesRecordedPackageState()
    {
        using var fixture = new Fixture();
        fixture.Catalog.VersionMarkerPath = "";
        var package = fixture.CreatePackage("2.1", "helper plugin", "helper docs", includeVersionFile: false);

        fixture.Manager.Apply(fixture.Catalog, package, fixture.XPlaneRoot, ToolPackageAction.Install);
        var inspection = fixture.Manager.Inspect(fixture.Catalog, fixture.XPlaneRoot, package.Release);

        Assert.Equal(ToolPackageInstallState.Current, inspection.State);
        Assert.Equal("2.1", inspection.InstalledVersion);
    }

    [Fact]
    public void Inspect_MarkerlessUnmanagedToolMatchingRelease_InfersVersionFromHashes()
    {
        using var fixture = new Fixture();
        fixture.Catalog.VersionMarkerPath = "";
        var package = fixture.CreatePackage("2.1", "helper plugin", "helper docs", includeVersionFile: false);
        fixture.WriteTarget("data/modules/main.lua", "helper plugin");
        fixture.WriteTarget("data/modules/configuration/configuration.ini", "helper docs");

        var inspection = fixture.Manager.Inspect(fixture.Catalog, fixture.XPlaneRoot, package.Release);

        Assert.Equal(ToolPackageInstallState.Current, inspection.State);
        Assert.Equal("2.1", inspection.InstalledVersion);
    }

    [Fact]
    public void Repair_RestoresOwnedFileAndPreservesProtectedConfiguration()
    {
        using var fixture = new Fixture();
        var package = fixture.CreatePackage("4.7", "new plugin", "default config");
        fixture.Manager.Apply(fixture.Catalog, package, fixture.XPlaneRoot, ToolPackageAction.Install);
        fixture.WriteTarget("data/modules/main.lua", "corrupt");
        fixture.WriteTarget("data/modules/configuration/configuration.ini", "user config");

        var result = fixture.Manager.Apply(fixture.Catalog, package, fixture.XPlaneRoot, ToolPackageAction.Repair);

        Assert.True(result.Succeeded);
        Assert.Equal("new plugin", fixture.ReadTarget("data/modules/main.lua"));
        Assert.Equal("user config", fixture.ReadTarget("data/modules/configuration/configuration.ini"));
    }

    [Fact]
    public void Inspect_WhenInstalledBetaIsNewerThanStable_RequiresExplicitChannelSwitch()
    {
        using var fixture = new Fixture();
        fixture.WriteInstalled("4.8b1", "newer plugin", "user config");
        var package = fixture.CreatePackage("4.7", "stable plugin", "default config");

        var inspection = fixture.Manager.Inspect(fixture.Catalog, fixture.XPlaneRoot, package.Release);
        var mislabeledUpdate = fixture.Manager.Apply(
            fixture.Catalog,
            package,
            fixture.XPlaneRoot,
            ToolPackageAction.Update);
        var channelSwitch = fixture.Manager.Apply(
            fixture.Catalog,
            package,
            fixture.XPlaneRoot,
            ToolPackageAction.SwitchChannel);

        Assert.Equal(ToolPackageInstallState.SelectedReleaseOlder, inspection.State);
        Assert.Contains("newer", inspection.Status, StringComparison.OrdinalIgnoreCase);
        Assert.False(mislabeledUpdate.Succeeded);
        Assert.True(channelSwitch.Succeeded);
        Assert.Equal("stable plugin", fixture.ReadTarget("data/modules/main.lua"));
        Assert.Equal(
            "beta",
            fixture.StateStore.TryGetToolInstallation(fixture.XPlaneRoot, fixture.Catalog.PackageId)?.Backups.Last().PreviousChannel);
    }

    [Fact]
    public void Restore_WhenOwnedFileChangedAfterUpdate_IsBlocked()
    {
        using var fixture = new Fixture();
        fixture.WriteInstalled("4.6", "old plugin", "user config");
        var package = fixture.CreatePackage("4.7", "new plugin", "default config");
        fixture.Manager.Apply(fixture.Catalog, package, fixture.XPlaneRoot, ToolPackageAction.Update);
        fixture.WriteTarget("data/modules/main.lua", "user modified plugin");

        var result = fixture.Manager.Restore(fixture.Catalog, fixture.XPlaneRoot);

        Assert.False(result.Succeeded);
        Assert.Equal("user modified plugin", fixture.ReadTarget("data/modules/main.lua"));
    }

    [Fact]
    public void Restore_AfterUpdate_RestoresExactPreviousInstallation()
    {
        using var fixture = new Fixture();
        fixture.WriteInstalled("4.6", "old plugin", "user config");
        var package = fixture.CreatePackage("4.7", "new plugin", "default config");
        fixture.Manager.Apply(fixture.Catalog, package, fixture.XPlaneRoot, ToolPackageAction.Update);

        var result = fixture.Manager.Restore(fixture.Catalog, fixture.XPlaneRoot);

        Assert.True(result.Succeeded);
        Assert.Equal("4.6", fixture.ReadTarget("data/modules/configuration/version.ini"));
        Assert.Equal("old plugin", fixture.ReadTarget("data/modules/main.lua"));
        Assert.Equal("user config", fixture.ReadTarget("data/modules/configuration/configuration.ini"));
    }

    [Fact]
    public void Apply_WhenXPlaneIsRunning_DoesNotChangeFiles()
    {
        using var fixture = new Fixture(xPlaneRunning: true);
        var package = fixture.CreatePackage("4.7", "new plugin", "default config");

        var result = fixture.Manager.Apply(fixture.Catalog, package, fixture.XPlaneRoot, ToolPackageAction.Install);

        Assert.False(result.Succeeded);
        Assert.False(Directory.Exists(fixture.TargetPath));
    }

    [Fact]
    public void Inspect_WhenTargetIsSymbolicLink_RejectsTargetWithoutFollowingIt()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new Fixture();
        var external = Path.Combine(fixture.Root, "external-yal");
        Directory.CreateDirectory(external);
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.TargetPath)!);
        Directory.CreateSymbolicLink(fixture.TargetPath, external);
        var package = fixture.CreatePackage("4.7", "new plugin", "default config");

        var inspection = fixture.Manager.Inspect(fixture.Catalog, fixture.XPlaneRoot, package.Release);

        Assert.Equal(ToolPackageInstallState.TargetUnavailable, inspection.State);
        Assert.Contains("symbolic link", inspection.Status, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture(bool xPlaneRunning = false)
        {
            Root = Path.Combine(Path.GetTempPath(), $"xplane-tool-package-tests-{Guid.NewGuid():N}");
            XPlaneRoot = Path.Combine(Root, "X-Plane 12");
            Directory.CreateDirectory(Path.Combine(XPlaneRoot, "Aircraft"));
            Directory.CreateDirectory(Path.Combine(XPlaneRoot, "Resources"));
            StateStore = new ToolStateStore(Path.Combine(Root, "state"), Path.Combine(Root, "backups"));
            Manager = new ToolPackageManager(StateStore, () => xPlaneRunning);
        }

        public string Root { get; }

        public string XPlaneRoot { get; }

        public string TargetPath => Path.Combine(XPlaneRoot, "Resources", "plugins", "YAL");

        public ToolStateStore StateStore { get; }

        public ToolPackageManager Manager { get; }

        public ContentPackageCatalogEntry Catalog { get; } = new()
        {
            PackageId = "wahltho.yal",
            DisplayName = "Yet Another Linda",
            Description = "Test tool",
            Category = ContentPackageCategory.Tool,
            Activation = ContentPatchActivation.ExplicitOptIn,
            SupportedProducts = ["zibo-737ng", "levelup-737ng"],
            RepositoryUrl = "https://github.com/example/yal",
            RestartRequired = true,
            InstallScope = "xPlaneInstallation",
            TargetPath = "Resources/plugins/YAL",
            VersionMarkerPath = "data/modules/configuration/version.ini",
            SupportedChannels = ["stable", "beta"],
            Distribution = new ContentPackageDistribution
            {
                Kind = ContentPackageDistributionKind.GitHubToolRelease,
                ManifestAssetNamePattern = "YAL-*-manifest.json",
                ManifestSchemaVersion = 1
            }
        };

        public ToolPackageProvisionResult CreatePackage(
            string version,
            string pluginContent,
            string configurationContent,
            bool includeVersionFile = true)
        {
            var packageRoot = Path.Combine(Root, "packages", version, Guid.NewGuid().ToString("N"));
            var files = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["data/modules/main.lua"] = pluginContent,
                ["data/modules/configuration/configuration.ini"] = configurationContent
            };
            if (includeVersionFile)
            {
                files["data/modules/configuration/version.ini"] = version;
            }
            foreach (var file in files)
            {
                Write(Path.Combine(packageRoot, file.Key.Replace('/', Path.DirectorySeparatorChar)), file.Value);
            }

            var manifest = new ToolPackageManifest
            {
                SchemaVersion = 1,
                PackageId = Catalog.PackageId,
                PackageVersion = version,
                ReleaseTag = "v" + version,
                Channel = "stable",
                Repository = Catalog.RepositoryUrl,
                InstallScope = Catalog.InstallScope,
                TargetPath = Catalog.TargetPath,
                SupportedProducts = [.. Catalog.SupportedProducts],
                RestartRequired = true,
                Archive = new ToolPackageArchive
                {
                    FileName = $"YAL-{version}.zip",
                    RootPath = "YAL",
                    Size = 1,
                    Sha256 = new string('a', 64)
                },
                ProtectedPaths =
                [
                    "data/modules/configuration/configuration.ini",
                    "data/modules/configuration/wprefs.ini",
                    "data/output/**"
                ],
                Files = files.Select(file =>
                {
                    var bytes = Encoding.UTF8.GetBytes(file.Value);
                    return new ToolPackageFile
                    {
                        Path = file.Key,
                        Size = bytes.LongLength,
                        Sha256 = Hash(bytes)
                    };
                }).ToList()
            };
            var release = new ToolPackageRelease(
                ToolReleaseChannel.Stable,
                manifest.ReleaseTag,
                $"https://github.com/example/yal/releases/tag/{manifest.ReleaseTag}",
                $"YAL-{version}-manifest.json",
                $"https://github.com/example/yal/releases/download/{manifest.ReleaseTag}/YAL-{version}-manifest.json",
                1,
                new string('b', 64),
                $"https://github.com/example/yal/releases/download/{manifest.ReleaseTag}/{manifest.Archive.FileName}",
                manifest);
            return new ToolPackageProvisionResult(release, packageRoot, Downloaded: false);
        }

        public void WriteInstalled(string version, string pluginContent, string configurationContent)
        {
            WriteTarget("data/modules/main.lua", pluginContent);
            WriteTarget("data/modules/configuration/configuration.ini", configurationContent);
            WriteTarget("data/modules/configuration/version.ini", version);
        }

        public void WriteTarget(string relativePath, string content) =>
            Write(Path.Combine(TargetPath, relativePath.Replace('/', Path.DirectorySeparatorChar)), content);

        public string ReadTarget(string relativePath) =>
            File.ReadAllText(Path.Combine(TargetPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static void Write(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        private static string Hash(byte[] bytes) =>
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
