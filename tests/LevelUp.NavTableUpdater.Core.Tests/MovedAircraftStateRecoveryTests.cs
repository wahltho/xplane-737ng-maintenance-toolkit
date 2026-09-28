using System.Security.Cryptography;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class MovedAircraftStateRecoveryTests
{
    [Fact]
    public void MovedAircraft_ReconnectsContentAndProductHistory_WithoutChangingFilesOrNewToolState()
    {
        using var fixture = new Fixture();
        fixture.MoveAircraft();
        fixture.Store.UpdateToolInstallation(fixture.Current, "wahltho.optimized-xlua", state =>
            state.InstalledVersion = "2.0.0b1-opt4");
        var before = File.ReadAllBytes(fixture.CurrentTarget);
        var backupBefore = File.ReadAllBytes(fixture.Backup);

        var candidate = Assert.IsType<MovedAircraftStateCandidate>(fixture.Recovery.FindCandidate(
            fixture.Current, AircraftProductIds.LevelUp737Ng));
        Assert.Equal(1, candidate.VerifiedFiles);
        Assert.Equal(1, candidate.VerifiedBackups);
        var stateCopy = fixture.Recovery.Reconnect(candidate);

        Assert.True(File.Exists(stateCopy));
        Assert.Null(fixture.Store.TryGetContentInstallation(fixture.Previous));
        var installation = Assert.IsType<ContentInstallationToolState>(fixture.Store.TryGetContentInstallation(fixture.Current));
        var file = Assert.Single(Assert.Single(installation.ContentComponents.Values).Files);
        Assert.Equal(fixture.CurrentTarget, file.TargetPath);
        Assert.Equal(fixture.Backup, file.BackupPath);
        Assert.Equal(fixture.CurrentTarget, Assert.Single(installation.Backups).SourcePath);
        var product = Assert.IsType<AircraftToolState>(fixture.Store.TryGetProductTarget(fixture.CurrentVariant));
        Assert.Equal(fixture.Current, product.AircraftFolder);
        Assert.Equal(fixture.CurrentTarget, Assert.Single(product.Backups).SourcePath);
        Assert.Equal(fixture.CurrentTarget, Assert.Single(Assert.Single(product.ContentComponents.Values).Files).TargetPath);
        Assert.Equal("2.0.0b1-opt4", fixture.Store.TryGetToolInstallation(
            fixture.Current, "wahltho.optimized-xlua")?.InstalledVersion);
        Assert.Equal(before, File.ReadAllBytes(fixture.CurrentTarget));
        Assert.Equal(backupBefore, File.ReadAllBytes(fixture.Backup));
        Assert.Null(fixture.Recovery.FindCandidate(fixture.Current, AircraftProductIds.LevelUp737Ng));
    }

    [Fact]
    public void CopyWithFormerFolderStillPresent_IsNotTreatedAsMove()
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.CurrentTarget)!);
        File.Copy(fixture.PreviousTarget, fixture.CurrentTarget);

        Assert.Null(fixture.Recovery.FindCandidate(fixture.Current, AircraftProductIds.LevelUp737Ng));
    }

    [Fact]
    public void ChangedManagedFile_DoesNotQualifyAndCannotReconnectAfterConfirmation()
    {
        using var fixture = new Fixture();
        fixture.MoveAircraft();
        var candidate = Assert.IsType<MovedAircraftStateCandidate>(fixture.Recovery.FindCandidate(
            fixture.Current, AircraftProductIds.LevelUp737Ng));
        File.WriteAllText(fixture.CurrentTarget, "locally changed");

        Assert.Null(fixture.Recovery.FindCandidate(fixture.Current, AircraftProductIds.LevelUp737Ng));
        Assert.Throws<InvalidOperationException>(() => fixture.Recovery.Reconnect(candidate));
        Assert.NotNull(fixture.Store.TryGetContentInstallation(fixture.Previous));
    }

    [Fact]
    public void MissingOrChangedOriginalBackup_BlocksReconnect()
    {
        using var fixture = new Fixture();
        fixture.MoveAircraft();
        File.WriteAllText(fixture.Backup, "modified original");

        Assert.Null(fixture.Recovery.FindCandidate(fixture.Current, AircraftProductIds.LevelUp737Ng));
        Assert.NotNull(fixture.Store.TryGetContentInstallation(fixture.Previous));
    }

    [Fact]
    public void PriorToolOwnershipAtFormerPath_BlocksPartialRelink()
    {
        using var fixture = new Fixture();
        fixture.Store.UpdateToolInstallation(fixture.Previous, "wahltho.optimized-xlua", state =>
            state.InstalledVersion = "1.3.7r5");
        fixture.MoveAircraft();

        Assert.Null(fixture.Recovery.FindCandidate(fixture.Current, AircraftProductIds.LevelUp737Ng));
    }

    [Fact]
    public void FullAircraftBackupGeneration_IsRebasedForLaterRestore()
    {
        using var fixture = new Fixture();
        var directoryBackup = Path.Combine(Path.GetDirectoryName(fixture.Backup)!, "full-aircraft");
        Directory.CreateDirectory(directoryBackup);
        fixture.Store.UpdateContentAndProduct(fixture.PreviousVariant, (installation, product) =>
        {
            var historical = new ContentComponentState
            {
                ComponentId = "wahltho.levelup-737ng.maintenance",
                Files = [new ContentComponentFileState
                {
                    RelativePath = "plugins/xlua/scripts/B738.a_fms/B738.a_fms.lua",
                    TargetPath = fixture.PreviousTarget,
                    BackupPath = fixture.Backup,
                    OriginalExisted = true,
                    OriginalSizeBytes = new FileInfo(fixture.Backup).Length,
                    OriginalSha256 = Fixture.Hash(fixture.Backup)
                }]
            };
            product.Backups.Add(new BackupRecord
            {
                Operation = "AircraftUpdateFullDirectory",
                SourcePath = fixture.Previous,
                BackupPath = directoryBackup,
                SourceExisted = true,
                AircraftContentGeneration = new AircraftContentGenerationState
                {
                    InstallationComponents = { [historical.ComponentId] = historical }
                }
            });
        });
        fixture.MoveAircraft();

        var candidate = Assert.IsType<MovedAircraftStateCandidate>(fixture.Recovery.FindCandidate(
            fixture.Current, AircraftProductIds.LevelUp737Ng));
        fixture.Recovery.Reconnect(candidate);

        var product = Assert.IsType<AircraftToolState>(fixture.Store.TryGetProductTarget(fixture.CurrentVariant));
        var generation = product.Backups.Single(record => record.Operation == "AircraftUpdateFullDirectory");
        Assert.Equal(fixture.Current, generation.SourcePath);
        Assert.Equal(directoryBackup, generation.BackupPath);
        Assert.Equal(fixture.CurrentTarget,
            Assert.Single(Assert.Single(generation.AircraftContentGeneration!.InstallationComponents.Values).Files).TargetPath);
    }

    [Fact]
    public void ExistingDestinationContentState_BlocksReconnect()
    {
        using var fixture = new Fixture();
        fixture.MoveAircraft();
        fixture.Store.UpdateContentInstallation(fixture.Current, state =>
            state.HasAuthoritativeContentState = true);

        Assert.Null(fixture.Recovery.FindCandidate(fixture.Current, AircraftProductIds.LevelUp737Ng));
    }

    [Fact]
    public void TwoMatchingOrphanedRecords_AreAmbiguousAndNotOffered()
    {
        using var fixture = new Fixture();
        fixture.MoveAircraft();
        fixture.AddSecondMatchingOrphan();

        Assert.Null(fixture.Recovery.FindCandidate(fixture.Current, AircraftProductIds.LevelUp737Ng));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"mtk-aircraft-move-{Guid.NewGuid():N}");
        private const string RelativeTarget = "plugins/xlua/scripts/B738.a_fms/B738.a_fms.lua";

        public Fixture()
        {
            Previous = Path.Combine(_root, "Aircraft", "737NG Series");
            Current = Path.Combine(_root, "Aircraft", "Planes", "737NG Series");
            PreviousTarget = Path.Combine(Previous, RelativeTarget.Replace('/', Path.DirectorySeparatorChar));
            CurrentTarget = Path.Combine(Current, RelativeTarget.Replace('/', Path.DirectorySeparatorChar));
            Backup = Path.Combine(_root, "backups", "B738.a_fms.lua");
            Store = new ToolStateStore(Path.Combine(_root, "state"), Path.Combine(_root, "backups"));
            Recovery = new MovedAircraftStateRecovery(Store);

            Directory.CreateDirectory(Path.GetDirectoryName(PreviousTarget)!);
            Directory.CreateDirectory(Path.GetDirectoryName(Backup)!);
            File.WriteAllText(PreviousTarget, "installed patch");
            File.WriteAllText(Backup, "original aircraft file");
            Store.UpdateContentAndProduct(PreviousVariant, (installation, product) =>
            {
                installation.HasAuthoritativeContentState = true;
                var component = new ContentComponentState
                {
                    ComponentId = "wahltho.levelup-737ng.maintenance",
                    PackageVersion = "1.0",
                    Files = [new ContentComponentFileState
                    {
                        RelativePath = RelativeTarget,
                        TargetPath = PreviousTarget,
                        BackupPath = Backup,
                        OriginalExisted = true,
                        OriginalSizeBytes = new FileInfo(Backup).Length,
                        OriginalSha256 = Hash(Backup),
                        InstalledSizeBytes = new FileInfo(PreviousTarget).Length,
                        InstalledSha256 = Hash(PreviousTarget)
                    }]
                };
                installation.ContentComponents[component.ComponentId] = component;
                product.ContentComponents[component.ComponentId] = component;
                var record = new BackupRecord
                {
                    Operation = "ContentPatchInstall",
                    SourcePath = PreviousTarget,
                    BackupPath = Backup,
                    SourceExisted = true,
                    SourceSizeBytes = new FileInfo(Backup).Length,
                    SourceSha256 = Hash(Backup)
                };
                installation.Backups.Add(record);
                product.Backups.Add(record);
            });
        }

        public string Previous { get; }
        public string Current { get; }
        public string PreviousTarget { get; }
        public string CurrentTarget { get; }
        public string Backup { get; }
        public ToolStateStore Store { get; }
        public MovedAircraftStateRecovery Recovery { get; }
        public AircraftVariantViewAnalysis CurrentVariant => Variant(Current);
        public AircraftVariantViewAnalysis PreviousVariant => Variant(Previous);

        public void MoveAircraft()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Current)!);
            Directory.Move(Previous, Current);
        }

        public void AddSecondMatchingOrphan()
        {
            var other = Previous + "-other";
            var otherTarget = Path.Combine(other, RelativeTarget.Replace('/', Path.DirectorySeparatorChar));
            Store.UpdateContentAndProduct(Variant(other), (installation, product) =>
            {
                installation.HasAuthoritativeContentState = true;
                var component = new ContentComponentState
                {
                    ComponentId = "wahltho.levelup-737ng.maintenance",
                    Files = [new ContentComponentFileState
                    {
                        RelativePath = RelativeTarget,
                        TargetPath = otherTarget,
                        BackupPath = Backup,
                        OriginalExisted = true,
                        OriginalSizeBytes = new FileInfo(Backup).Length,
                        OriginalSha256 = Hash(Backup),
                        InstalledSizeBytes = new FileInfo(CurrentTarget).Length,
                        InstalledSha256 = Hash(CurrentTarget)
                    }]
                };
                installation.ContentComponents[component.ComponentId] = component;
                product.ContentComponents[component.ComponentId] = component;
            });
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);

        public static string Hash(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        private static AircraftVariantViewAnalysis Variant(string root) => new(
            AircraftId: "levelup-737-700", DisplayName: "LevelUp 737-700", Family: "levelup-737ng",
            AcfPath: Path.Combine(root, "737_70NG.acf"), PrefsPath: Path.Combine(root, "737_70NG_prefs.txt"),
            Source: "test", SourceRef: "test", SourceVersion: "1", LocalVersion: null,
            AcfVersion: null, FileWriterVersion: null, CurrentCgYFeet: null, CurrentCgZFeet: null,
            ReferenceCgYFeet: 0, ReferenceCgZFeet: 0, DeltaYFeet: null, DeltaZFeet: null,
            DeltaYMeters: null, DeltaZMeters: null, Status: "test", IdentityStatus: "test",
            QuickViewStatus: "test", DefaultViewStatus: "test");
    }
}
