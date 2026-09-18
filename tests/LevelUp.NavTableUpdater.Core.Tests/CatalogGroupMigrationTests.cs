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

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NoChangeRecords_AtCurrentAndIntermediateHashesPreserveOriginal(bool reverseOrder, bool uppercaseHash)
    {
        File.WriteAllText(Path.Combine(_root, "shared.lua"), "both patches");
        var records = new[]
        {
            State("first", "stock", "first patch"),
            State("intermediate", "first patch", "first patch"),
            State("second", "first patch", "both patches"),
            State("current", "both patches", "both patches"),
            State("duplicate", "both patches", "both patches")
        };
        if (uppercaseHash)
            foreach (var record in records)
                record.Files[0].OriginalSha256 = record.Files[0].OriginalSha256!.ToUpperInvariant();
        var installed = (reverseOrder ? records.Reverse() : records)
            .ToDictionary(record => record.ComponentId);
        var manifest = Manifest();
        manifest.Sources.Add(new() { PackageId = "intermediate" });
        manifest.Sources.Add(new() { PackageId = "current" });
        manifest.Sources.Add(new() { PackageId = "duplicate" });

        var result = CatalogGroupMigration.Prepare(_root, manifest, installed)!;

        var file = Assert.Single(result.Files);
        Assert.Equal(Hash("stock"), file.OriginalSha256, ignoreCase: true);
        Assert.Equal("stock", File.ReadAllText(file.BackupPath));
        Assert.Equal(Hash("both patches"), file.InstalledSha256);
        Assert.Equal("both patches", File.ReadAllText(Path.Combine(_root, "shared.lua")));
        Assert.Equal(5, installed.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidNoChangeBackup_StillBlocks(bool missing)
    {
        File.WriteAllText(Path.Combine(_root, "shared.lua"), "both patches");
        var installed = new Dictionary<string, ContentComponentState>
        {
            ["first"] = State("first", "stock", "both patches"),
            ["second"] = State("second", "both patches", "both patches")
        };
        var backup = installed["second"].Files[0].BackupPath;
        if (missing) File.Delete(backup);
        else File.WriteAllText(backup, "corrupt");

        Assert.Throws<InvalidOperationException>(() => CatalogGroupMigration.Prepare(_root, Manifest(), installed));
        Assert.Equal("both patches", File.ReadAllText(Path.Combine(_root, "shared.lua")));
        Assert.Equal(2, installed.Count);
    }

    [Fact]
    public void NoChangeRecord_DoesNotHideDisconnectedHistory()
    {
        File.WriteAllText(Path.Combine(_root, "shared.lua"), "both patches");
        var installed = new Dictionary<string, ContentComponentState>
        {
            ["first"] = State("first", "stock", "different patch"),
            ["second"] = State("second", "both patches", "both patches")
        };
        var error = Assert.Throws<InvalidOperationException>(() => CatalogGroupMigration.Prepare(_root, Manifest(), installed));
        Assert.Contains("complete backup chain", error.Message);
        Assert.Equal("both patches", File.ReadAllText(Path.Combine(_root, "shared.lua")));
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
