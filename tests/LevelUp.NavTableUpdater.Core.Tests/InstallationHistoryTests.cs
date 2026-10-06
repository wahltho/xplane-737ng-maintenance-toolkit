using System.Security.Cryptography;
using LevelUp.NavTableUpdater.App.Services;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class InstallationHistoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mtk-history-{Guid.NewGuid():N}");
    private string Aircraft => Path.Combine(_root, "aircraft");
    private ToolStateStore Store => new(Path.Combine(_root, "state"), Path.Combine(_root, "backups"));
    private static readonly DateTimeOffset Earlier = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Earlier.AddDays(1);

    [Fact]
    public void Read_DeduplicatesMirroredBackups_SortsRecords_AndNeverChangesFiles()
    {
        var store = Store;
        var backup = Path.Combine(store.BackupRootPath, "original.lua");
        Directory.CreateDirectory(store.BackupRootPath);
        File.WriteAllText(backup, "original bytes");
        var record = new BackupRecord { Operation = "ContentPatchInstall", CreatedUtc = Earlier,
            PackageId = "patch", PackageVersion = "1.0", SourcePath = Path.Combine(Aircraft, "fms.lua"), BackupPath = backup };
        var component = new ContentComponentState { PackageVersion = "2.0", LastOperation = "ContentPatchUpdate", LastOperationUtc = Later,
            Files = [new() { OriginalExisted = true, BackupPath = backup }, new() { OriginalExisted = false }] };
        store.Save(new() {
            ContentInstallations = new() { ["installation"] = new() { AircraftFolder = Aircraft,
                ContentComponents = new() { ["patch"] = component }, Backups = [record] } },
            Aircraft = new() { ["variant"] = new() { AircraftFolder = Aircraft, ContentComponents = new() { ["patch"] = component }, Backups = [record] } }
        });
        var before = Snapshot();
        var history = InstallationHistorySummary.Read(store, Aircraft, null, new Dictionary<string, string> { ["patch"] = "Test patch" });
        Assert.Equal("History loaded", history.Status);
        Assert.Equal(2, history.Entries.Count);
        Assert.Equal(Later, history.Entries[0].DateUtc);
        Assert.Equal("Test patch", history.Entries[0].Name);
        Assert.Equal("Update patches", history.Entries[0].Action);
        Assert.Equal("2.0", history.Entries[0].Version);
        Assert.Contains(history.Entries[0].Backups, b => b.Path == backup && b.Status == "File found; contents not checked");
        Assert.Contains(history.Entries[0].Backups, b => b.Status.StartsWith("Did not exist before the change"));
        Assert.Single(history.Entries, e => e.Scope == "Backup");
        Assert.Contains(history.Notes, n => n.Contains("may not include every change"));
        Assert.Contains(history.Notes, n => n.Contains("confirm that they can be restored"));
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void MissingBackups_Adoption_UndatedRecords_AndOldBackupPaths_AreExplicit()
    {
        var store = Store;
        var oldPath = Path.Combine(_root, "old-backups", "original.lua");
        // A previously configured root may still contain a valid recorded backup.
        Directory.CreateDirectory(Path.GetDirectoryName(oldPath)!);
        File.WriteAllText(oldPath, "original");
        store.Save(new() { ContentInstallations = new() { ["a"] = new() { AircraftFolder = Aircraft, ContentComponents = new() {
            ["adopted"] = new() { RestoreAvailable = false },
            ["dated"] = new() { LastOperationUtc = Later, Files = [
                new() { OriginalExisted = true, BackupPath = oldPath },
                new() { OriginalExisted = true, BackupPath = Path.Combine(store.BackupRootPath, "missing") },
                new() { OriginalExisted = true }] }
        } } } });
        var history = InstallationHistorySummary.Read(store, Aircraft, null);
        var adopted = history.Entries.Last();
        Assert.Equal("No date saved", adopted.When);
        Assert.Null(adopted.DateUtc);
        Assert.Contains("Restore is unavailable", adopted.BackupStatus);
        Assert.Contains(history.Entries[0].Backups, b => b.Path == oldPath && b.Status != "Backup not found");
        Assert.Contains(history.Entries[0].Backups, b => b.Status == "Backup not found");
        Assert.Contains(history.Entries[0].Backups, b => b.Status == "Backup location unknown");
        Assert.Equal(oldPath, history.Entries[0].Backups[0].Path);
    }

    [Fact]
    public void ToolsAndLiveries_AreFilteredByAircraftAndXPlaneRoot_WithExpectedAbsence()
    {
        var store = Store;
        var xp = Path.Combine(_root, "XPlane");
        var directory = Path.Combine(store.BackupRootPath, "xlua1");
        Directory.CreateDirectory(directory);
        store.Save(new() {
            ToolInstallations = new() {
                ["component"] = new() { PackageId = "xlua", XPlaneRoot = Aircraft, TargetPath = "plugins/xlua", InstalledVersion = "2.0", LastOperationUtc = Later,
                    Backups = [new() { SourceExisted = true, BackupPath = directory, CreatedUtc = Earlier, PreviousVersion = "1.0", InstalledVersion = "2.0" }] },
                ["shared"] = new() { PackageId = "shared", XPlaneRoot = xp, TargetPath = "Resources/plugins/tool", Backups = [new() { SourceExisted = false, CreatedUtc = Later }] },
                ["other"] = new() { PackageId = "other", XPlaneRoot = Path.Combine(_root, "other"), TargetPath = "plugins/other" },
                ["overlay"] = new() { PackageId = "overlay", XPlaneRoot = Aircraft, TargetPath = "plugins", Backups = [new() { BackupPath = directory, CreatedUtc = Later,
                    OverlayFiles = [new() { RelativePath = "created.lua", OriginalExisted = false }] }] }
            },
            LiveryInstallations = new() {
                ["livery"] = new() { PackageId = "livery", DestinationDirectory = Path.Combine(Aircraft, "liveries"), LastOperationUtc = Later },
                ["other-livery"] = new() { PackageId = "other-livery", DestinationDirectory = Path.Combine(_root, "other", "liveries") }
            }
        });
        var history = InstallationHistorySummary.Read(store, Aircraft, xp);
        Assert.DoesNotContain(history.Entries, e => e.Name.StartsWith("other"));
        Assert.Contains(history.Entries, e => e.Name == "shared" && e.Scope.StartsWith("Shared X-Plane tool"));
        Assert.Contains(history.Entries, e => e.Name == "xlua" && e.Scope.StartsWith("Aircraft component"));
        Assert.Contains(history.Entries, e => e.Name == "livery");
        var generation = history.Entries.Single(e => e.Name == "xlua" && e.Action == "Earlier backup");
        Assert.Equal("Folder found; contents not checked", Assert.Single(generation.Backups).Status);
        Assert.Equal("Previous: 1.0; installed: 2.0", generation.Version);
        Assert.StartsWith("Did not exist before the change", Assert.Single(history.Entries.Single(e => e.Name == "overlay" && e.Action == "Earlier backup").Backups).Status);
    }

    [Fact]
    public void AuthoritativeEmptyState_DoesNotResurrectLegacyComponents()
    {
        Store.Save(new() { ContentInstallations = new() { ["i"] = new() { AircraftFolder = Aircraft, HasAuthoritativeContentState = true } },
            Aircraft = new() { ["a"] = new() { AircraftFolder = Aircraft, ContentComponents = new() { ["old"] = new() } } } });
        Assert.Empty(InstallationHistorySummary.Read(Store, Aircraft, null).Entries);
    }

    [Fact]
    public void AircraftDirectoryBackup_IsNotMisreportedAsAMissingFile()
    {
        var backup = Path.Combine(Store.BackupRootPath, "previous-aircraft");
        Directory.CreateDirectory(backup);
        Store.Save(new() { Aircraft = new() { ["a"] = new() { AircraftFolder = Aircraft, Backups = [new() {
            Operation = "AircraftUpdateFullDirectory", BackupPath = backup, SourcePath = Aircraft, SourceExisted = true, CreatedUtc = Earlier
        }] } } });
        var entry = Assert.Single(InstallationHistorySummary.Read(Store, Aircraft, null).Entries);
        Assert.Equal("Complete aircraft folder backup", entry.Action);
        var location = Assert.Single(entry.Backups);
        Assert.Equal(Aircraft, location.OriginalPath);
        Assert.Equal("Folder found; contents not checked", location.Status);
        Directory.Delete(backup);
        File.WriteAllText(backup, "unexpected file");
        Assert.Equal("Expected a folder but found a file",
            Assert.Single(InstallationHistorySummary.Read(Store, Aircraft, null).Entries[0].Backups).Status);
    }

    [Fact]
    public void LargeHistory_IsLimitedToTheNewestRecords_WithAnExplicitNote()
    {
        Store.Save(new() { Aircraft = new() { ["a"] = new() { AircraftFolder = Aircraft,
            Backups = Enumerable.Range(0, 1002).Select(i => new BackupRecord {
                CreatedUtc = Earlier.AddMinutes(i), SourceExisted = false, SourcePath = $"file-{i}"
            }).ToList()
        } } });
        var history = InstallationHistorySummary.Read(Store, Aircraft, null);
        Assert.Equal(1000, history.Entries.Count);
        Assert.Equal(Earlier.AddMinutes(1001), history.Entries[0].DateUtc);
        Assert.Contains(history.Notes, n => n.Contains("newest 1000"));
    }

    [Fact]
    public void MissingOrMalformedState_DoesNotClaimCompleteHistory_AndCancellationIsHonored()
    {
        var store = Store;
        Assert.Equal("No saved MTK history", InstallationHistorySummary.Read(store, Aircraft, null).Status);
        Assert.False(Directory.Exists(store.RootPath));
        Directory.CreateDirectory(store.RootPath);
        File.WriteAllText(store.StatePath, "{invalid");
        var before = Snapshot();
        Assert.Equal("History incomplete", InstallationHistorySummary.Read(store, Aircraft, null).Status);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => InstallationHistorySummary.Read(store, Aircraft, null, cancellationToken: canceled.Token));
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void LinkedBackups_AreNotFollowed_AndLinkedStateIsRejected()
    {
        if (OperatingSystem.IsWindows()) return; // Creating symlinks needs privileges on Windows.
        var store = Store;
        Directory.CreateDirectory(store.BackupRootPath);
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "original"), "original");
        var link = Path.Combine(store.BackupRootPath, "linked");
        Directory.CreateSymbolicLink(link, outside);
        store.Save(new() { ContentInstallations = new() { ["i"] = new() { AircraftFolder = Aircraft, ContentComponents = new() {
            ["patch"] = new() { Files = [new() { OriginalExisted = true, BackupPath = Path.Combine(link, "original") }] }
        } } } });
        Assert.Equal("Not checked (linked file or folder)", Assert.Single(InstallationHistorySummary.Read(store, Aircraft, null).Entries[0].Backups).Status);
        File.Move(store.StatePath, Path.Combine(outside, "state.json"));
        File.CreateSymbolicLink(store.StatePath, Path.Combine(outside, "state.json"));
        Assert.Equal("History incomplete", InstallationHistorySummary.Read(store, Aircraft, null).Status);
    }

    private string[] Snapshot() => Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
        .Select(p => p + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))).ToArray();
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
