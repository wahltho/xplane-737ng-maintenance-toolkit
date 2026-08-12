using System.Security.Cryptography;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.Platform;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Tools;

public sealed class XPlaneOverlayPackageManager
{
    private readonly ToolStateStore _stateStore;
    private readonly Func<bool> _isXPlaneRunning;

    public XPlaneOverlayPackageManager(ToolStateStore stateStore, Func<bool>? isXPlaneRunning = null)
    {
        _stateStore = stateStore;
        _isXPlaneRunning = isXPlaneRunning ?? XPlaneProcessDetector.IsXPlaneRunning;
    }

    public ToolPackageInspection Inspect(
        ContentPackageCatalogEntry catalogEntry,
        string? xPlaneRoot,
        ToolPackageRelease? release)
    {
        if (string.IsNullOrWhiteSpace(xPlaneRoot) || !XPlaneInstallationLocator.LooksLikeXPlaneRoot(xPlaneRoot))
        {
            return new ToolPackageInspection(
                ToolPackageInstallState.TargetUnavailable,
                xPlaneRoot ?? "",
                "",
                "-",
                release?.Manifest.PackageVersion ?? "Not checked",
                "Select an X-Plane installation containing a supported Zibo or LevelUp aircraft.",
                []);
        }

        var root = Path.GetFullPath(xPlaneRoot);
        try
        {
            RejectRootOrTargetLinks(root, release?.Manifest.Files.Select(file => file.Path) ?? []);
        }
        catch (InvalidDataException ex)
        {
            return new ToolPackageInspection(
                ToolPackageInstallState.TargetUnavailable,
                root,
                root,
                "-",
                release?.Manifest.PackageVersion ?? "Not checked",
                "The X-Plane overlay target contains an unsupported symbolic link.",
                [ex.Message]);
        }

        var state = _stateStore.TryGetToolInstallation(root, catalogEntry.PackageId);
        if (release is null)
        {
            return new ToolPackageInspection(
                state is null ? ToolPackageInstallState.NotInstalled : ToolPackageInstallState.Current,
                root,
                root,
                state?.InstalledVersion ?? "-",
                "Not checked",
                state is null ? "Not installed; release not checked" : "Installed; check the selected release channel for updates",
                []);
        }

        ValidatePackageForCatalog(catalogEntry, release.Manifest);
        var findings = ValidateInstalledFiles(root, release.Manifest);
        var installedVersion = state?.InstalledVersion ?? "";
        var present = release.Manifest.Files.Count(file => File.Exists(ResolveTarget(root, file.Path)));
        if (state is null)
        {
            if (findings.Count == 0)
            {
                installedVersion = release.Manifest.PackageVersion;
                return Inspection(ToolPackageInstallState.Current, root, installedVersion, release, "Existing files match the selected release", []);
            }

            return present == 0
                ? Inspection(ToolPackageInstallState.NotInstalled, root, "-", release, "Not installed", [])
                : Inspection(
                    ToolPackageInstallState.InstalledVersionUnknown,
                    root,
                    "Unknown",
                    release,
                    "Existing overlay files can be adopted through a verified update",
                    findings);
        }

        if (string.IsNullOrWhiteSpace(installedVersion) && present == 0)
        {
            return Inspection(ToolPackageInstallState.NotInstalled, root, "-", release, "Not installed", []);
        }

        if (!VersionsEqual(installedVersion, release.Manifest.PackageVersion))
        {
            var order = CompareVersions(installedVersion, release.Manifest.PackageVersion);
            return order > 0
                ? Inspection(
                    ToolPackageInstallState.SelectedReleaseOlder,
                    root,
                    installedVersion,
                    release,
                    "Installed version is newer than the selected release",
                    [])
                : Inspection(
                    ToolPackageInstallState.UpdateAvailable,
                    root,
                    installedVersion,
                    release,
                    $"{release.Manifest.Channel} update available",
                    []);
        }

        return findings.Count == 0
            ? Inspection(ToolPackageInstallState.Current, root, installedVersion, release, "Installed release is current and verified", [])
            : Inspection(
                ToolPackageInstallState.RepairRequired,
                root,
                installedVersion,
                release,
                $"Repair required: {findings.Count} package file(s) are missing or changed",
                findings);
    }

    public MaintenanceOperationResult Apply(
        ContentPackageCatalogEntry catalogEntry,
        ToolPackageProvisionResult package,
        string xPlaneRoot,
        ToolPackageAction action)
    {
        var manifest = package.Release.Manifest;
        var log = new List<string>
        {
            $"[START] {action} {manifest.PackageId} {manifest.PackageVersion}",
            $"[TARGET] {xPlaneRoot}"
        };
        if (_isXPlaneRunning())
        {
            return MaintenanceOperationResult.Blocked("X-Plane is running. Close X-Plane before changing plugins.", [.. log, "[BLOCKED] X-Plane is running."]);
        }

        if (!XPlaneInstallationLocator.LooksLikeXPlaneRoot(xPlaneRoot))
        {
            return MaintenanceOperationResult.Blocked("The selected X-Plane installation root is invalid.", [.. log, "[BLOCKED] Invalid X-Plane root."]);
        }

        ValidatePackageForCatalog(catalogEntry, manifest);
        GitHubToolPackageReleaseSource.ValidateExtractedPackage(package.PackageDirectory, manifest);
        var root = Path.GetFullPath(xPlaneRoot);
        var inspection = Inspect(catalogEntry, root, package.Release);
        if (!ActionMatchesState(action, inspection.State))
        {
            return MaintenanceOperationResult.Blocked(
                $"{action} is not valid for the current {catalogEntry.DisplayName} state ({inspection.Status}).",
                [.. log, $"[BLOCKED] {action} is not valid for {inspection.State}."]);
        }

        if (action is ToolPackageAction.Repair && inspection.State is ToolPackageInstallState.Current)
        {
            return MaintenanceOperationResult.NoChange($"{catalogEntry.DisplayName} is current and all package files are verified.", [.. log, "[NO-CHANGE] All files match."]);
        }

        RejectRootOrTargetLinks(root, manifest.Files.Select(file => file.Path));
        var createdUtc = DateTimeOffset.UtcNow;
        var previousState = _stateStore.TryGetToolInstallation(root, manifest.PackageId);
        var backupRoot = _stateStore.CreateToolBackupDirectory(root, manifest.PackageId, createdUtc);
        Directory.CreateDirectory(backupRoot);
        var backupFiles = CaptureAndBackupOriginals(root, manifest, backupRoot, log);
        var stagedFiles = StageFiles(package.PackageDirectory, manifest, root);
        var applied = new Stack<AppliedFile>();
        try
        {
            foreach (var file in manifest.Files)
            {
                var target = ResolveTarget(root, file.Path);
                var staged = stagedFiles[file.Path];
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                RejectLink(target, "Overlay target file");
                var rollback = target + $".rollback-{Guid.NewGuid():N}";
                var existed = File.Exists(target);
                if (existed)
                {
                    File.Move(target, rollback);
                }

                try
                {
                    File.Move(staged, target);
                    applied.Push(new AppliedFile(target, rollback, existed));
                }
                catch
                {
                    if (existed && File.Exists(rollback))
                    {
                        File.Move(rollback, target);
                    }

                    throw;
                }
            }

            var findings = ValidateInstalledFiles(root, manifest);
            if (findings.Count > 0)
            {
                throw new InvalidDataException($"Installed overlay failed verification: {findings[0]}.");
            }

            var installedFiles = CaptureInstalledFiles(root, manifest);
            var generation = new ToolBackupGenerationState
            {
                BackupId = createdUtc.UtcDateTime.ToString("yyyyMMddTHHmmssfffZ"),
                BackupPath = backupRoot,
                CreatedUtc = createdUtc,
                SourceExisted = backupFiles.Any(file => file.OriginalExisted),
                PreviousVersion = previousState?.InstalledVersion ?? "",
                PreviousChannel = previousState?.Channel ?? "stable",
                InstalledVersion = manifest.PackageVersion,
                InstalledFiles = installedFiles,
                OverlayFiles = backupFiles
            };
            _stateStore.UpdateToolInstallation(root, manifest.PackageId, state =>
            {
                state.TargetPath = root;
                state.InstalledVersion = manifest.PackageVersion;
                state.Channel = manifest.Channel;
                state.LastOperationUtc = DateTimeOffset.UtcNow;
                state.LastOperation = action.ToString();
                state.InstalledFiles = installedFiles;
                state.ProtectedPaths = [.. manifest.ProtectedPaths];
                state.Backups.Add(generation);
            });

            foreach (var file in applied)
            {
                TryDeleteFile(file.RollbackPath);
            }
            applied.Clear();

            log.Add($"[OK] {catalogEntry.DisplayName} {manifest.PackageVersion} installed and verified.");
            return MaintenanceOperationResult.Applied(
                $"{catalogEntry.DisplayName} {manifest.PackageVersion} was installed. Restart X-Plane before using it.",
                backupFiles.Any(file => file.OriginalExisted) ? [backupRoot] : [],
                log);
        }
        catch
        {
            while (applied.Count > 0)
            {
                var file = applied.Pop();
                DeleteFileIfPresent(file.TargetPath);
                if (file.OriginalExisted && File.Exists(file.RollbackPath))
                {
                    File.Move(file.RollbackPath, file.TargetPath);
                }
            }

            log.Add("[ROLLBACK] Previous overlay files restored.");
            throw;
        }
        finally
        {
            foreach (var staged in stagedFiles.Values)
            {
                DeleteFileIfPresent(staged);
            }
        }
    }

    public MaintenanceOperationResult Restore(ContentPackageCatalogEntry catalogEntry, string xPlaneRoot)
    {
        var log = new List<string> { $"[START] Restore {catalogEntry.PackageId}" };
        if (_isXPlaneRunning())
        {
            return MaintenanceOperationResult.Blocked("X-Plane is running. Close X-Plane before restoring plugins.", [.. log, "[BLOCKED] X-Plane is running."]);
        }

        if (!XPlaneInstallationLocator.LooksLikeXPlaneRoot(xPlaneRoot))
        {
            return MaintenanceOperationResult.Blocked("The selected X-Plane installation root is invalid.", [.. log, "[BLOCKED] Invalid X-Plane root."]);
        }

        var root = Path.GetFullPath(xPlaneRoot);
        var state = _stateStore.TryGetToolInstallation(root, catalogEntry.PackageId);
        var generation = state?.Backups
            .Where(backup => backup.OverlayFiles.Count > 0 && Directory.Exists(backup.BackupPath))
            .OrderByDescending(backup => backup.CreatedUtc)
            .FirstOrDefault();
        if (state is null || generation is null)
        {
            return MaintenanceOperationResult.Blocked(
                $"No valid {catalogEntry.DisplayName} backup generation is available for this X-Plane installation.",
                [.. log, "[BLOCKED] No overlay backup generation."]);
        }

        var guardFindings = ValidateRestoreGuard(root, state.InstalledFiles);
        if (guardFindings.Count > 0)
        {
            return MaintenanceOperationResult.Blocked(
                $"Restore stopped because package-owned {catalogEntry.DisplayName} files changed after installation.",
                [.. log, .. guardFindings.Select(finding => $"[BLOCKED] {finding}")]);
        }

        RejectRootOrTargetLinks(root, generation.OverlayFiles.Select(file => file.RelativePath));
        var preRestoreRoot = _stateStore.CreateToolBackupDirectory(root, catalogEntry.PackageId, DateTimeOffset.UtcNow);
        var currentBackup = CaptureCurrentFiles(root, state.InstalledFiles, preRestoreRoot);
        var completed = new Stack<AppliedFile>();
        try
        {
            foreach (var file in generation.OverlayFiles)
            {
                var target = ResolveTarget(root, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var rollback = target + $".rollback-{Guid.NewGuid():N}";
                var existed = File.Exists(target);
                if (existed)
                {
                    File.Move(target, rollback);
                }

                try
                {
                    if (file.OriginalExisted)
                    {
                        var backup = ResolveTarget(generation.BackupPath, file.RelativePath);
                        File.Copy(backup, target, overwrite: false);
                        CopyUnixMode(backup, target);
                        if (!MatchesOptionalOriginal(target, file))
                        {
                            throw new InvalidDataException($"Restored file failed verification: {file.RelativePath}.");
                        }
                    }

                    completed.Push(new AppliedFile(target, rollback, existed));
                }
                catch
                {
                    DeleteFileIfPresent(target);
                    if (existed && File.Exists(rollback))
                    {
                        File.Move(rollback, target);
                    }

                    throw;
                }
            }

            _stateStore.UpdateToolInstallation(root, catalogEntry.PackageId, updated =>
            {
                updated.InstalledVersion = generation.PreviousVersion;
                updated.Channel = generation.PreviousChannel;
                updated.LastOperationUtc = DateTimeOffset.UtcNow;
                updated.LastOperation = "Restore";
                updated.InstalledFiles = generation.OverlayFiles
                    .Where(file => file.OriginalExisted)
                    .Select(file => new ToolInstalledFileState
                    {
                        RelativePath = file.RelativePath,
                        Size = file.OriginalSize ?? 0,
                        Sha256 = file.OriginalSha256 ?? "",
                        Protected = false
                    })
                    .ToList();
                updated.Backups.Add(new ToolBackupGenerationState
                {
                    BackupId = DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyyMMddTHHmmssfffZ"),
                    BackupPath = preRestoreRoot,
                    CreatedUtc = DateTimeOffset.UtcNow,
                    SourceExisted = currentBackup.Count > 0,
                    PreviousVersion = state.InstalledVersion,
                    PreviousChannel = state.Channel,
                    InstalledVersion = generation.PreviousVersion,
                    InstalledFiles = [.. updated.InstalledFiles],
                    OverlayFiles = currentBackup
                });
            });

            foreach (var file in completed)
            {
                TryDeleteFile(file.RollbackPath);
            }
            completed.Clear();

            log.Add("[OK] Previous overlay file state restored. Generated logs were not changed.");
            return MaintenanceOperationResult.Restored(
                $"Restored the previous {catalogEntry.DisplayName} file state. Generated logs were preserved. Restart X-Plane.",
                [preRestoreRoot],
                log);
        }
        catch
        {
            while (completed.Count > 0)
            {
                var file = completed.Pop();
                DeleteFileIfPresent(file.TargetPath);
                if (file.OriginalExisted && File.Exists(file.RollbackPath))
                {
                    File.Move(file.RollbackPath, file.TargetPath);
                }
            }

            throw;
        }
    }

    private static ToolPackageInspection Inspection(
        ToolPackageInstallState state,
        string root,
        string installedVersion,
        ToolPackageRelease release,
        string status,
        IReadOnlyList<string> findings) =>
        new(state, root, root, installedVersion, release.Manifest.PackageVersion, status, findings);

    private static List<ToolOverlayBackupFileState> CaptureAndBackupOriginals(
        string root,
        ToolPackageManifest manifest,
        string backupRoot,
        ICollection<string> log)
    {
        var result = new List<ToolOverlayBackupFileState>();
        foreach (var file in manifest.Files)
        {
            var target = ResolveTarget(root, file.Path);
            var existed = File.Exists(target);
            var state = new ToolOverlayBackupFileState
            {
                RelativePath = file.Path,
                OriginalExisted = existed
            };
            if (existed)
            {
                var backup = ResolveTarget(backupRoot, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(target, backup, overwrite: false);
                CopyUnixMode(target, backup);
                state.OriginalSize = new FileInfo(target).Length;
                state.OriginalSha256 = HashFile(target);
                log.Add($"[BACKUP] {file.Path}");
            }

            result.Add(state);
        }

        return result;
    }

    private static Dictionary<string, string> StageFiles(
        string packageDirectory,
        ToolPackageManifest manifest,
        string root)
    {
        var staged = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var file in manifest.Files)
            {
                var source = ResolveTarget(packageDirectory, file.Path);
                var target = ResolveTarget(root, file.Path);
                var stage = target + $".stage-{Guid.NewGuid():N}";
                Directory.CreateDirectory(Path.GetDirectoryName(stage)!);
                File.Copy(source, stage, overwrite: false);
                CopyUnixMode(source, stage);
                if (!FileMatches(stage, file.Size, file.Sha256))
                {
                    throw new InvalidDataException($"Staged overlay file failed verification: {file.Path}.");
                }

                staged[file.Path] = stage;
            }

            return staged;
        }
        catch
        {
            foreach (var stagedPath in staged.Values)
            {
                DeleteFileIfPresent(stagedPath);
            }

            throw;
        }
    }

    private static List<ToolOverlayBackupFileState> CaptureCurrentFiles(
        string root,
        IReadOnlyList<ToolInstalledFileState> files,
        string backupRoot)
    {
        var result = new List<ToolOverlayBackupFileState>();
        foreach (var file in files)
        {
            var source = ResolveTarget(root, file.RelativePath);
            if (!File.Exists(source))
            {
                continue;
            }

            var backup = ResolveTarget(backupRoot, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            File.Copy(source, backup, overwrite: false);
            CopyUnixMode(source, backup);
            result.Add(new ToolOverlayBackupFileState
            {
                RelativePath = file.RelativePath,
                OriginalExisted = true,
                OriginalSize = new FileInfo(source).Length,
                OriginalSha256 = HashFile(source)
            });
        }

        return result;
    }

    private static List<ToolInstalledFileState> CaptureInstalledFiles(string root, ToolPackageManifest manifest) =>
        manifest.Files.Select(file => new ToolInstalledFileState
        {
            RelativePath = file.Path,
            Size = file.Size,
            Sha256 = file.Sha256,
            Protected = false
        }).ToList();

    private static IReadOnlyList<string> ValidateInstalledFiles(string root, ToolPackageManifest manifest)
    {
        var findings = new List<string>();
        foreach (var file in manifest.Files)
        {
            var target = ResolveTarget(root, file.Path);
            if (!FileMatches(target, file.Size, file.Sha256))
            {
                findings.Add(File.Exists(target) ? $"Changed: {file.Path}" : $"Missing: {file.Path}");
            }
        }

        return findings;
    }

    private static IReadOnlyList<string> ValidateRestoreGuard(string root, IReadOnlyList<ToolInstalledFileState> files) =>
        files.Where(file => !FileMatches(ResolveTarget(root, file.RelativePath), file.Size, file.Sha256))
            .Select(file => $"Changed after installation: {file.RelativePath}")
            .ToArray();

    private static void ValidatePackageForCatalog(ContentPackageCatalogEntry catalog, ToolPackageManifest manifest)
    {
        if (catalog.Distribution.Kind is not ContentPackageDistributionKind.GitHubXPlaneOverlayRelease
            || manifest.SchemaVersion != 2
            || manifest.Layout != "xPlaneOverlay"
            || !catalog.PackageId.Equals(manifest.PackageId, StringComparison.Ordinal)
            || !catalog.InstallScope.Equals(manifest.InstallScope, StringComparison.Ordinal)
            || !string.IsNullOrWhiteSpace(catalog.TargetPath)
            || !string.IsNullOrWhiteSpace(manifest.TargetPath)
            || !catalog.RepositoryUrl.TrimEnd('/').Equals(manifest.Repository.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
            || !manifest.SupportedProducts.ToHashSet(StringComparer.Ordinal).SetEquals(catalog.SupportedProducts))
        {
            throw new InvalidDataException($"Overlay package does not match trusted catalog entry {catalog.PackageId}.");
        }
    }

    private static void RejectRootOrTargetLinks(string root, IEnumerable<string> paths)
    {
        RejectLink(root, "X-Plane root");
        foreach (var relativePath in paths)
        {
            var current = root;
            var parts = ToolPackageManifestParser.NormalizeRelativePath(relativePath).Split('/');
            for (var index = 0; index < parts.Length; index++)
            {
                current = Path.Combine(current, parts[index]);
                RejectLink(current, index == parts.Length - 1 ? "Overlay target file" : "Overlay target directory");
            }
        }
    }

    private static string ResolveTarget(string root, string relativePath)
    {
        var normalized = ToolPackageManifestParser.NormalizeRelativePath(relativePath);
        var fullRoot = Path.GetFullPath(root);
        var target = Path.GetFullPath(Path.Combine(fullRoot, Path.Combine(normalized.Split('/'))));
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!target.StartsWith(Path.TrimEndingDirectorySeparator(fullRoot) + Path.DirectorySeparatorChar, comparison))
        {
            throw new InvalidDataException($"Overlay target escapes its root: {relativePath}.");
        }

        return target;
    }

    private static void RejectLink(string path, string label)
    {
        if (new FileInfo(path).LinkTarget is not null || new DirectoryInfo(path).LinkTarget is not null)
        {
            throw new InvalidDataException($"{label} is a symbolic link: {path}.");
        }
    }

    private static bool FileMatches(string path, long size, string sha256) =>
        File.Exists(path)
        && new FileInfo(path).Length == size
        && HashFile(path).Equals(sha256, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesOptionalOriginal(string path, ToolOverlayBackupFileState file) =>
        file.OriginalSize.HasValue
        && !string.IsNullOrWhiteSpace(file.OriginalSha256)
        && FileMatches(path, file.OriginalSize.Value, file.OriginalSha256);

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void CopyUnixMode(string source, string destination)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(destination, File.GetUnixFileMode(source));
        }
    }

    private static void DeleteFileIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            DeleteFileIfPresent(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool ActionMatchesState(ToolPackageAction action, ToolPackageInstallState state) =>
        action switch
        {
            ToolPackageAction.Install => state is ToolPackageInstallState.NotInstalled,
            ToolPackageAction.Update => state is ToolPackageInstallState.UpdateAvailable or ToolPackageInstallState.InstalledVersionUnknown,
            ToolPackageAction.SwitchChannel => state is ToolPackageInstallState.SelectedReleaseOlder,
            ToolPackageAction.Repair => state is ToolPackageInstallState.RepairRequired or ToolPackageInstallState.Current,
            _ => false
        };

    private static bool VersionsEqual(string left, string right) =>
        left.Trim().TrimStart('v', 'V').Equals(right.Trim().TrimStart('v', 'V'), StringComparison.OrdinalIgnoreCase);

    private static int CompareVersions(string left, string right)
    {
        var leftVersion = ParseVersion(left);
        var rightVersion = ParseVersion(right);
        return leftVersion is not null && rightVersion is not null
            ? leftVersion.CompareTo(rightVersion)
            : string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static Version? ParseVersion(string value) =>
        Version.TryParse(value.Trim().TrimStart('v', 'V').Split('-', 2)[0], out var version) ? version : null;

    private sealed record AppliedFile(string TargetPath, string RollbackPath, bool OriginalExisted);
}
