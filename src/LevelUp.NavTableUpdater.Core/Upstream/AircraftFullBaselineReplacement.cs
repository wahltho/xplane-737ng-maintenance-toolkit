using System.Security.Cryptography;
using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Upstream;

internal sealed class AircraftFullBaselineReplacement
{
    private static readonly JsonSerializerOptions MetadataJsonOptions = new() { WriteIndented = true };

    private readonly ToolStateStore _stateStore;
    private readonly Action? _afterTargetMoved;

    public AircraftFullBaselineReplacement(ToolStateStore stateStore, Action? afterTargetMoved = null)
    {
        _stateStore = stateStore;
        _afterTargetMoved = afterTargetMoved;
    }

    public MaintenanceOperationResult Apply(
        AircraftVariantViewAnalysis variant,
        AircraftUpstreamUpdateCheckResult updateCheck,
        IReadOnlyList<AircraftUpdatePackageCacheEntry> cachedPackages,
        CancellationToken cancellationToken,
        Action? writePhaseStarting,
        IReadOnlyList<AircraftUpdatePreservationPlan> preservationPlans,
        ICollection<string> log)
    {
        if (cachedPackages.Count == 0
            || cachedPackages[0].Package.Kind != AircraftUpdatePackageKind.FullBaseline
            || cachedPackages.Count(package => package.Package.Kind == AircraftUpdatePackageKind.FullBaseline) != 1
            || cachedPackages.Skip(1).Any(package => package.Package.Kind != AircraftUpdatePackageKind.CumulativePatch))
        {
            throw new InvalidDataException("A clean baseline replacement requires exactly one full baseline first, followed only by cumulative patches.");
        }

        var selectedFolder = Path.GetFullPath(Path.GetDirectoryName(variant.AcfPath) ?? "");
        var targetFolder = AircraftUpdatePath.ResolvePhysicalPath(selectedFolder);
        var targetParent = Path.GetDirectoryName(targetFolder)
            ?? throw new InvalidOperationException("Aircraft folder has no parent directory.");
        var targetName = Path.GetFileName(targetFolder);
        var stamp = DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyyMMddTHHmmssfffZ");
        var stagePath = Path.Combine(targetParent, $".{targetName}.toolkit-stage-{Guid.NewGuid():N}");
        var backupPath = Path.Combine(targetParent, $".{targetName}.toolkit-backup-{stamp}-{Guid.NewGuid():N}");
        var targetMoved = false;
        var stageMoved = false;

        try
        {
            Directory.CreateDirectory(stagePath);
            foreach (var cachedPackage in cachedPackages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ExtractPackage(stagePath, cachedPackage, cancellationToken, log);
            }

            MigrateProtectedLocalFiles(targetFolder, stagePath, cancellationToken, log);
            MigrateLocalLiveries(targetFolder, stagePath, cancellationToken, log);
            foreach (var plan in preservationPlans)
            {
                cancellationToken.ThrowIfCancellationRequested();
                plan.ApplyTo(stagePath, log);
            }
            WriteToolkitMetadata(stagePath, variant, updateCheck);
            ValidateStagedAircraft(stagePath, variant);
            cancellationToken.ThrowIfCancellationRequested();

            writePhaseStarting?.Invoke();
            Directory.Move(targetFolder, backupPath);
            targetMoved = true;
            _afterTargetMoved?.Invoke();
            Directory.Move(stagePath, targetFolder);
            stageMoved = true;
            ValidateStagedAircraft(targetFolder, variant);

            var backupRecord = new BackupRecord
            {
                Operation = "AircraftUpdateFullDirectory",
                SourcePath = targetFolder,
                BackupPath = backupPath,
                CreatedUtc = DateTimeOffset.UtcNow,
                CgYFeet = variant.CurrentCgYFeet,
                CgZFeet = variant.CurrentCgZFeet,
                PackageId = updateCheck.Family,
                PackageVersion = updateCheck.AvailableVersionDisplay,
                PackageFileName = string.Join(";", updateCheck.RequiredPackages.Select(package => package.FileName)),
                SourceExisted = true
            };
            RecordState(variant, updateCheck, backupRecord);
            targetMoved = false;

            log.Add($"[BACKUP] Exact previous aircraft directory retained at {backupPath}");
            log.Add("[OK] Clean baseline replacement completed and validated.");
            return MaintenanceOperationResult.Applied(
                $"Clean baseline replacement completed to {updateCheck.AvailableVersionDisplay}.",
                [backupPath],
                log.ToArray());
        }
        catch
        {
            if (stageMoved && Directory.Exists(targetFolder))
            {
                Directory.Delete(targetFolder, recursive: true);
            }

            if (targetMoved && Directory.Exists(backupPath))
            {
                Directory.Move(backupPath, targetFolder);
                log.Add("[ROLLBACK] Previous aircraft directory restored.");
            }

            throw;
        }
        finally
        {
            if (Directory.Exists(stagePath))
            {
                Directory.Delete(stagePath, recursive: true);
            }
        }
    }

    public MaintenanceOperationResult Restore(
        AircraftVariantViewAnalysis variant,
        BackupRecord generation,
        ICollection<string> log)
    {
        var targetFolder = AircraftUpdatePath.ResolvePhysicalPath(generation.SourcePath);
        var backupFolder = Path.GetFullPath(generation.BackupPath);
        if (!Directory.Exists(backupFolder))
        {
            throw new DirectoryNotFoundException($"Aircraft baseline backup directory is missing: {backupFolder}");
        }

        var parent = Path.GetDirectoryName(targetFolder)
            ?? throw new InvalidOperationException("Aircraft folder has no parent directory.");
        var preRestorePath = Path.Combine(
            parent,
            $".{Path.GetFileName(targetFolder)}.toolkit-backup-{DateTimeOffset.UtcNow.UtcDateTime:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        var currentMoved = false;
        var backupMoved = false;
        try
        {
            Directory.Move(targetFolder, preRestorePath);
            currentMoved = true;
            Directory.Move(backupFolder, targetFolder);
            backupMoved = true;

            var selectedAcf = Path.Combine(targetFolder, Path.GetFileName(variant.AcfPath));
            if (!File.Exists(selectedAcf))
            {
                throw new InvalidDataException($"Restored aircraft directory does not contain {Path.GetFileName(variant.AcfPath)}.");
            }

            var preRestoreRecord = new BackupRecord
            {
                Operation = "AircraftUpdateFullDirectory",
                SourcePath = targetFolder,
                BackupPath = preRestorePath,
                CreatedUtc = DateTimeOffset.UtcNow,
                CgYFeet = variant.CurrentCgYFeet,
                CgZFeet = variant.CurrentCgZFeet,
                PackageId = generation.PackageId,
                PackageVersion = generation.PackageVersion,
                PackageFileName = generation.PackageFileName,
                SourceExisted = true
            };
            _stateStore.UpdateProductTarget(variant, state =>
            {
                state.InstalledAircraftUpdateFamily = null;
                state.InstalledAircraftUpdateVersion = null;
                state.LastAircraftUpdateMode = null;
                state.LastAircraftUpdateUtc = DateTimeOffset.UtcNow;
                state.LastAircraftUpdatePackages.Clear();
                state.LastOperation = "AircraftUpdateFullRestore";
                state.Backups.Add(preRestoreRecord);
            });
            currentMoved = false;

            log.Add($"[BACKUP] Pre-restore aircraft directory retained at {preRestorePath}");
            log.Add("[OK] Previous aircraft baseline directory restored.");
            return MaintenanceOperationResult.Restored(
                "Restored the complete aircraft directory from the latest clean-baseline backup.",
                [preRestorePath],
                log.ToArray());
        }
        catch
        {
            if (backupMoved && Directory.Exists(targetFolder))
            {
                Directory.Move(targetFolder, backupFolder);
            }

            if (currentMoved && Directory.Exists(preRestorePath))
            {
                Directory.Move(preRestorePath, targetFolder);
                log.Add("[ROLLBACK] Current aircraft directory restored after restore failure.");
            }

            throw;
        }
    }

    internal static void ExtractPackage(
        string stagePath,
        AircraftUpdatePackageCacheEntry cachedPackage,
        CancellationToken cancellationToken,
        ICollection<string> log)
    {
        using var archive = AircraftPackageArchive.Open(cachedPackage.CachePath);
        var fileEntries = archive.Entries.Where(entry => !entry.IsDirectory).ToArray();
        var contentRoot = AircraftUpdatePath.DetectContentRoot(cachedPackage.Package, fileEntries);
        var mappedPaths = new HashSet<string>(AircraftUpdateLocalContentPolicy.PathComparer);
        log.Add($"[STAGE] Extracting {cachedPackage.Package.FileName} ({fileEntries.Length} files)."
            + (contentRoot is null ? "" : $" Content root: {contentRoot}."));

        foreach (var archiveEntry in fileEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = AircraftUpdatePath.MapArchivePath(archiveEntry.Path, contentRoot)
                ?? throw new InvalidDataException($"Unsafe or inconsistent archive path in {cachedPackage.Package.FileName}: {archiveEntry.Path}");
            if (!mappedPaths.Add(relativePath))
            {
                throw new InvalidDataException($"Archive contains a duplicate target path: {relativePath}");
            }

            if (string.Equals(Path.GetFileName(relativePath), AircraftMaintenanceMetadata.FileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var targetPath = AircraftUpdatePath.ResolveTargetPath(stagePath, relativePath);
            var manifestFile = cachedPackage.Package.Manifest?.Files
                .FirstOrDefault(file => string.Equals(file.Path, relativePath, StringComparison.OrdinalIgnoreCase));
            ExtractEntry(archiveEntry, targetPath, manifestFile, cancellationToken);
        }

        foreach (var deletedPath in cachedPackage.Package.Manifest?.DeletedPaths ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (AircraftUpdateLocalContentPolicy.IsProtectedLocalFile(deletedPath)
                || string.Equals(Path.GetFileName(deletedPath), AircraftMaintenanceMetadata.FileName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Package attempts to delete a protected local path: {deletedPath}");
            }

            var targetPath = AircraftUpdatePath.ResolveTargetPath(stagePath, deletedPath);
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
            else if (Directory.Exists(targetPath))
            {
                Directory.Delete(targetPath, recursive: true);
            }
        }
    }

    private static void ExtractEntry(
        AircraftPackageArchiveEntry archiveEntry,
        string targetPath,
        AircraftUpdateManifestFile? manifestFile,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        using (var input = archiveEntry.Open())
        using (var output = File.Create(targetPath))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                output.Write(buffer, 0, read);
            }
        }

        if (manifestFile is null)
        {
            return;
        }

        var info = new FileInfo(targetPath);
        if (info.Length != manifestFile.Size
            || !string.Equals(ComputeSha256(targetPath), manifestFile.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Staged payload failed manifest verification: {manifestFile.Path}");
        }
    }

    private static void MigrateProtectedLocalFiles(
        string sourceRoot,
        string stageRoot,
        CancellationToken cancellationToken,
        ICollection<string> log)
    {
        foreach (var sourcePath in EnumerateFilesWithoutFollowingLinks(sourceRoot, cancellationToken))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, sourcePath);
            if (!AircraftUpdateLocalContentPolicy.IsProtectedLocalFile(relativePath))
            {
                continue;
            }

            var destination = AircraftUpdatePath.ResolveTargetPath(stageRoot, relativePath);
            CopyFile(sourcePath, destination);
            log.Add($"[MIGRATE] Preserved local configuration {relativePath}.");
        }
    }

    private static void MigrateLocalLiveries(
        string sourceRoot,
        string stageRoot,
        CancellationToken cancellationToken,
        ICollection<string> log)
    {
        var sourceLiveries = Path.Combine(sourceRoot, "liveries");
        if (!Directory.Exists(sourceLiveries))
        {
            return;
        }

        var stageLiveries = Path.Combine(stageRoot, "liveries");
        foreach (var sourceEntry in Directory.EnumerateFileSystemEntries(sourceLiveries))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectLink(sourceEntry);
            var destination = Path.Combine(stageLiveries, Path.GetFileName(sourceEntry));
            if (File.Exists(destination) || Directory.Exists(destination))
            {
                continue;
            }

            if (Directory.Exists(sourceEntry))
            {
                CopyDirectory(sourceEntry, destination, cancellationToken);
            }
            else
            {
                CopyFile(sourceEntry, destination);
            }

            log.Add($"[MIGRATE] Preserved local livery {Path.GetFileName(sourceEntry)}.");
        }
    }

    private static IEnumerable<string> EnumerateFilesWithoutFollowingLinks(
        string root,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var entry in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileSystemInfo info = Directory.Exists(entry) ? new DirectoryInfo(entry) : new FileInfo(entry);
                if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    var relativePath = Path.GetRelativePath(root, entry);
                    if (AircraftUpdateLocalContentPolicy.CouldContainProtectedLocalContent(relativePath))
                    {
                        throw new InvalidDataException($"Protected local content contains a symbolic link that cannot be migrated safely: {entry}");
                    }

                    continue;
                }

                if (Directory.Exists(entry))
                {
                    pending.Push(entry);
                }
                else
                {
                    yield return entry;
                }
            }
        }
    }

    private static void CopyDirectory(string source, string destination, CancellationToken cancellationToken)
    {
        RejectLink(source);
        Directory.CreateDirectory(destination);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectLink(entry);
            var target = Path.Combine(destination, Path.GetFileName(entry));
            if (Directory.Exists(entry))
            {
                CopyDirectory(entry, target, cancellationToken);
            }
            else
            {
                CopyFile(entry, target);
            }
        }
    }

    private static void CopyFile(string source, string destination)
    {
        RejectLink(source);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
        File.SetAttributes(destination, File.GetAttributes(source));
        if (!OperatingSystem.IsWindows())
        {
#pragma warning disable CA1416
            File.SetUnixFileMode(destination, File.GetUnixFileMode(source));
#pragma warning restore CA1416
        }
    }

    private static void RejectLink(string path)
    {
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidDataException($"Protected local content contains a symbolic link that cannot be migrated safely: {path}");
        }
    }

    private static void ValidateStagedAircraft(string stagePath, AircraftVariantViewAnalysis variant)
    {
        var selectedAcf = Path.Combine(stagePath, Path.GetFileName(variant.AcfPath));
        if (!File.Exists(selectedAcf))
        {
            throw new InvalidDataException($"Staged aircraft image does not contain {Path.GetFileName(variant.AcfPath)}.");
        }

        if (!Directory.Exists(Path.Combine(stagePath, "plugins")))
        {
            throw new InvalidDataException("Staged aircraft image does not contain the expected plugins directory.");
        }
    }

    private static void WriteToolkitMetadata(
        string stagePath,
        AircraftVariantViewAnalysis variant,
        AircraftUpstreamUpdateCheckResult updateCheck)
    {
        var metadata = new AircraftMaintenanceMetadata(
            SchemaVersion: 1,
            AircraftFamily: variant.Family,
            Variant: null,
            Distribution: null,
            DistributionVersion: updateCheck.AvailableVersionDisplay,
            UpstreamFamily: updateCheck.Family,
            UpstreamBaseVersion: updateCheck.AvailableVersionDisplay,
            UpstreamSourceRef: null,
            UpstreamReleaseTag: null,
            Runtime: null);
        File.WriteAllText(
            Path.Combine(stagePath, AircraftMaintenanceMetadata.FileName),
            JsonSerializer.Serialize(metadata, MetadataJsonOptions));
    }

    private void RecordState(
        AircraftVariantViewAnalysis variant,
        AircraftUpstreamUpdateCheckResult updateCheck,
        BackupRecord backupRecord)
    {
        _stateStore.UpdateProductTarget(variant, target =>
        {
            target.InstalledAircraftUpdateFamily = updateCheck.Family;
            target.InstalledAircraftUpdateVersion = updateCheck.AvailableVersionDisplay;
            target.LastAircraftUpdateMode = AircraftUpdateMode.Full.ToString();
            target.LastAircraftUpdateUtc = DateTimeOffset.UtcNow;
            target.LastAircraftUpdatePackages = updateCheck.RequiredPackages.Select(package => package.FileName).ToList();
            target.LastOperation = "AircraftUpdateFullApply";
            target.Backups.Add(backupRecord);
        });
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
