using LevelUp.NavTableUpdater.Core.Upstream;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class AircraftUpdatePackageCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"xplane-737ng-cache-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Clear_WhenRootIsMarked_RemovesCachedContentOnly()
    {
        var cache = new AircraftUpdatePackageCache(_root);
        cache.EnsureRoot();
        Directory.CreateDirectory(Path.Combine(_root, "zibo-737ng"));
        File.WriteAllText(Path.Combine(_root, "zibo-737ng", "package.zip"), "cached");

        var removed = cache.Clear();

        Assert.Equal(1, removed);
        Assert.True(File.Exists(Path.Combine(_root, ".xplane-737ng-aircraft-update-cache")));
        Assert.False(Directory.Exists(Path.Combine(_root, "zibo-737ng")));
    }

    [Fact]
    public void Clear_WhenRootIsUnmarkedAndNotDefault_Throws()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "unrelated.txt"), "keep");
        var cache = new AircraftUpdatePackageCache(_root);

        Assert.Throws<InvalidOperationException>(() => cache.Clear());
        Assert.True(File.Exists(Path.Combine(_root, "unrelated.txt")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
