using System.Security.Cryptography;
using System.Text;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.Manifest;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class CatalogGroupMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "catalog-migration-" + Guid.NewGuid().ToString("N"));
    public CatalogGroupMigrationTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void SharedTarget_FollowsHashChainToOriginalInsteadOfLatestBackup()
    {
        File.WriteAllText(Path.Combine(_root, "shared.lua"), "both patches");
        var installed = States();
        var result = CatalogGroupMigration.Prepare(_root, Manifest(), installed)!;
        Assert.Equal(Hash("stock"), Assert.Single(result.Files).OriginalSha256);
        Assert.Equal("stock", File.ReadAllText(result.Files[0].BackupPath));
        Assert.Equal("both patches", File.ReadAllText(Path.Combine(_root, "shared.lua")));
        Assert.Equal(2, installed.Count); // Planning must not transfer ownership yet.
    }

    [Fact]
    public void ForeignEdit_BlocksWithoutChangingFilesOrState()
    {
        File.WriteAllText(Path.Combine(_root, "shared.lua"), "foreign change");
        var installed = States();
        Assert.Throws<InvalidOperationException>(() => CatalogGroupMigration.Prepare(_root, Manifest(), installed));
        Assert.Equal("foreign change", File.ReadAllText(Path.Combine(_root, "shared.lua")));
        Assert.Equal(2, installed.Count);
    }

    [Fact]
    public void CorruptOriginalBackup_BlocksMigration()
    {
        File.WriteAllText(Path.Combine(_root, "shared.lua"), "both patches");
        var installed = States();
        File.WriteAllText(installed["first"].Files[0].BackupPath, "corrupt");
        Assert.Throws<InvalidOperationException>(() => CatalogGroupMigration.Prepare(_root, Manifest(), installed));
    }

    private Dictionary<string, ContentComponentState> States() => new()
    {
        ["first"] = State("first", "stock", "first patch"),
        ["second"] = State("second", "first patch", "both patches")
    };
    private ContentComponentState State(string id, string original, string installed)
    {
        var backup = Path.Combine(_root, id + ".bak");
        File.WriteAllText(backup, original);
        return new() { ComponentId = id, Files = [new() { RelativePath = "shared.lua", BackupPath = backup,
            OriginalExisted = true, OriginalSha256 = Hash(original), OriginalSizeBytes = Encoding.UTF8.GetByteCount(original),
            InstalledSha256 = Hash(installed), InstalledSizeBytes = Encoding.UTF8.GetByteCount(installed) }] };
    }
    private static CompatibilityPackageManifest Manifest() => new()
    {
        PackageId = "group", Sources = [new() { PackageId = "first" }, new() { PackageId = "second" }],
        Modules = [new() { Targets = [new() { RelativePath = "shared.lua" }] }]
    };
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
