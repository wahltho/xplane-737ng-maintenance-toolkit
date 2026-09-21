using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.State;
using LevelUp.NavTableUpdater.Core.Tools;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class ToolPackageDependencyPlannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mtk-tool-dependencies-{Guid.NewGuid():N}");

    [Fact]
    public async Task MissingTransitiveDependencies_ArePlannedBeforeDependentPackage()
    {
        var catalog = Catalog("a", "b", "c");
        var releases = new Dictionary<string, ToolPackageRelease>(StringComparer.Ordinal)
        {
            ["a"] = Release("a", "1.0", Dependency("b", "2.0")),
            ["b"] = Release("b", "2.1", Dependency("c")),
            ["c"] = Release("c", "3.0")
        };
        var planner = new ToolPackageDependencyPlanner(catalog, Store(), "win-x64");

        var plan = await planner.CreateAsync(
            Entry(catalog, "a"), releases["a"], Root("a"), ToolPackageAction.Install, "zibo-737ng",
            entry => Root(entry.PackageId),
            (entry, root, release) => Inspection(ToolPackageInstallState.NotInstalled, root, release),
            (entry, _, _) => Task.FromResult<ToolPackageRelease?>(releases[entry.PackageId]));

        Assert.Equal(new[] { "c", "b", "a" }, plan.Actions.Select(item => item.CatalogEntry.PackageId));
        Assert.Equal("2.1", Assert.Single(plan.Actions[^1].ResolvedDependencies).ResolvedVersion);
        Assert.Equal("3.0", Assert.Single(plan.Actions[1].ResolvedDependencies).ResolvedVersion);
    }

    [Fact]
    public async Task InstalledDependencyAtMinimum_DoesNotCreateAnExtraAction()
    {
        var catalog = Catalog("a", "b");
        var target = Release("a", "1.0", Dependency("b", "2.0"));
        var store = Store();
        store.UpdateToolInstallation(Root("b"), "b", state => state.InstalledVersion = "2.4");
        var planner = new ToolPackageDependencyPlanner(catalog, store, "win-x64");

        var plan = await planner.CreateAsync(
            Entry(catalog, "a"), target, Root("a"), ToolPackageAction.Install, "zibo-737ng",
            entry => Root(entry.PackageId),
            (entry, root, release) => entry.PackageId == "b"
                ? new ToolPackageInspection(ToolPackageInstallState.Current, root, root, "2.4", "Not checked", "Installed", [])
                : Inspection(ToolPackageInstallState.NotInstalled, root, release),
            (_, _, _) => throw new Exception("A satisfying installed dependency must not require release discovery."));

        var item = Assert.Single(plan.Actions);
        Assert.Equal("a", item.CatalogEntry.PackageId);
        Assert.Equal("2.4", Assert.Single(item.ResolvedDependencies).ResolvedVersion);
    }

    [Fact]
    public async Task DependencyCycle_IsRejectedBeforeAnyPackageAction()
    {
        var catalog = Catalog("a", "b");
        var releases = new Dictionary<string, ToolPackageRelease>(StringComparer.Ordinal)
        {
            ["a"] = Release("a", "1.0", Dependency("b")),
            ["b"] = Release("b", "1.0", Dependency("a"))
        };
        var planner = new ToolPackageDependencyPlanner(catalog, Store(), "win-x64");

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => planner.CreateAsync(
            Entry(catalog, "a"), releases["a"], Root("a"), ToolPackageAction.Install, "zibo-737ng",
            entry => Root(entry.PackageId),
            (entry, root, release) => Inspection(ToolPackageInstallState.NotInstalled, root, release),
            (entry, _, _) => Task.FromResult<ToolPackageRelease?>(releases[entry.PackageId])));

        Assert.Contains("cycle", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DependencyBelowRequiredAvailableVersion_IsRejected()
    {
        var catalog = Catalog("a", "b");
        var target = Release("a", "1.0", Dependency("b", "5.0"));
        var planner = new ToolPackageDependencyPlanner(catalog, Store(), "win-x64");

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => planner.CreateAsync(
            Entry(catalog, "a"), target, Root("a"), ToolPackageAction.Install, "zibo-737ng",
            entry => Root(entry.PackageId),
            (entry, root, release) => Inspection(ToolPackageInstallState.NotInstalled, root, release),
            (_, _, _) => Task.FromResult<ToolPackageRelease?>(Release("b", "4.9"))));

        Assert.Contains("5.0", error.Message, StringComparison.Ordinal);
        Assert.Contains("4.9", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreThatWouldBreakInstalledDependent_IsReported()
    {
        var store = Store();
        store.UpdateToolInstallation(Root("b"), "b", state => state.InstalledVersion = "2.1");
        store.UpdateToolInstallation(Root("a"), "a", state =>
        {
            state.InstalledVersion = "1.0";
            state.Dependencies = [new ToolInstalledDependencyState
            {
                PackageId = "b", MinimumVersion = "2.0", InstallationRoot = Root("b"), ResolvedVersion = "2.1"
            }];
        });
        var planner = new ToolPackageDependencyPlanner(Catalog("a", "b"), store, "win-x64");

        Assert.Single(planner.FindRestoreBlockers("b", Root("b"), "1.9"));
        Assert.Empty(planner.FindRestoreBlockers("b", Root("b"), "2.0"));
    }

    [Theory]
    [InlineData("1.0-beta.1", "1.0", -1)]
    [InlineData("1.0", "1.0-beta.1", 1)]
    [InlineData("4.7", "4.7.0", 0)]
    [InlineData("4.8b1", "4.8b1", 0)]
    [InlineData("4.8b2", "4.8b1", 1)]
    [InlineData("4.8b10", "4.8b2", 1)]
    [InlineData("4.8a9", "4.8b1", -1)]
    public void SharedVersionComparison_HandlesReleaseAndPrereleaseVersions(string left, string right, int expectedSign) =>
        Assert.Equal(expectedSign, Math.Sign(ToolPackageVersion.Compare(left, right)));

    [Fact]
    public void YalStandaloneProviderMinimum_RejectsOlderYalAndAcceptsRequiredBeta()
    {
        Assert.False(ToolPackageVersion.IsAtLeast("4.7.9", "4.8b1"));
        Assert.True(ToolPackageVersion.IsAtLeast("4.8b1", "4.8b1"));
        Assert.True(ToolPackageVersion.IsAtLeast("4.8b2", "4.8b1"));
    }

    private ToolStateStore Store() => new(Path.Combine(_root, "state"), Path.Combine(_root, "backups"));
    private string Root(string id) => Path.Combine(_root, id);

    private static ContentPackageCatalog Catalog(params string[] ids) => ContentPackageCatalog.Parse(JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        catalogVersion = "1.0.0",
        minimumToolkitVersion = "0.1.0",
        packages = ids.Select(id => new
        {
            packageId = id,
            displayName = id.ToUpperInvariant(),
            description = "Test package " + id,
            category = "tool",
            activation = "explicitOptIn",
            supportedProducts = new[] { "zibo-737ng" },
            supportedPlatforms = new[] { "win-x64" },
            repositoryUrl = $"https://github.com/example/{id}",
            restartRequired = true,
            installScope = "xPlaneInstallation",
            targetPath = $"Resources/plugins/{id}",
            versionMarkerPath = "version.txt",
            supportedChannels = new[] { "stable" },
            distribution = new { kind = "gitHubToolRelease", manifestAssetNamePattern = $"{id}-*-manifest.json", manifestSchemaVersion = 1 }
        })
    }));

    private static ContentPackageCatalogEntry Entry(ContentPackageCatalog catalog, string id) =>
        catalog.Packages.Single(entry => entry.PackageId == id);

    private static ToolPackageRelease Release(string id, string version, params ToolPackageDependency[] dependencies)
    {
        var manifest = new ToolPackageManifest
        {
            SchemaVersion = 1,
            PackageId = id,
            PackageVersion = version,
            ReleaseTag = "v" + version,
            Channel = "stable",
            Repository = $"https://github.com/example/{id}",
            InstallScope = "xPlaneInstallation",
            Layout = "directory",
            TargetPath = $"Resources/plugins/{id}",
            SupportedProducts = ["zibo-737ng"],
            SupportedPlatforms = ["win-x64"],
            Dependencies = [.. dependencies],
            RestartRequired = true
        };
        return new ToolPackageRelease(ToolReleaseChannel.Stable, manifest.ReleaseTag, "", "", "", 1, new string('a', 64), "", manifest);
    }

    private static ToolPackageDependency Dependency(string packageId, string minimumVersion = "") => new()
    {
        PackageId = packageId,
        MinimumVersion = minimumVersion
    };

    private static ToolPackageInspection Inspection(ToolPackageInstallState state, string root, ToolPackageRelease? release) =>
        new(state, root, root, "-", release?.Manifest.PackageVersion ?? "Not checked", state.ToString(), []);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
