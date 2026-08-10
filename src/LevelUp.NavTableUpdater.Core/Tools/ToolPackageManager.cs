using System.Security.Cryptography;
using System.Text.RegularExpressions;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.Platform;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Tools;

public enum ToolPackageAction
{
    Install,
    Update,
    SwitchChannel,
    Repair
}

public sealed class ToolPackageManager
{
    private readonly ToolStateStore _stateStore;
    private readonly Func<bool> _isXPlaneRunning;

    public ToolPackageManager(ToolStateStore stateStore, Func<bool>? isXPlaneRunning = null)
    {
        _stateStore = stateStore;
        _isXPlaneRunning = isXPlaneRunning ?? XPlaneProcessDetector.IsXPlaneRunning;
    }

    public ToolPackageInspection Inspect(
        ContentPackageCatalogEntry catalogEntry,
        string? xPlaneRoot,
        ToolPackageRelease? release)
    {
        var toolName = catalogEntry.DisplayName;
        if (string.IsNullOrWhiteSpace(xPlaneRoot) || !LooksLikeInstallRoot(catalogEntry, xPlaneRoot))
        {
            return new ToolPackageInspection(
                ToolPackageInstallState.TargetUnavailable,
                xPlaneRoot ?? "",
                "",
                "-",
                release?.Manifest.PackageVersion ?? "Not checked",
                catalogEntry.InstallScope == "aircraftInstallation"
                    ? "Select a supported Zibo or LevelUp aircraft installation."
                    : "Select an X-Plane installation containing a supported Zibo or LevelUp aircraft.",
                []);
        }

        var fullRoot = Path.GetFullPath(xPlaneRoot);
        var targetPath = ResolveTarget(fullRoot, catalogEntry.TargetPath);
        try
        {
            RejectTargetPathLinks(fullRoot, targetPath);
            RejectNestedLinks(targetPath);
        }
        catch (InvalidDataException ex)
        {
            return new ToolPackageInspection(
                ToolPackageInstallState.TargetUnavailable,
                fullRoot,
                targetPath,
                "-",
                release?.Manifest.PackageVersion ?? "Not checked",
                $"The {toolName} target contains an unsupported symbolic link",
                [ex.Message]);
        }

        if (!Directory.Exists(targetPath))
        {
            return new ToolPackageInspection(
                ToolPackageInstallState.NotInstalled,
                fullRoot,
                targetPath,
                "-",
                release?.Manifest.PackageVersion ?? "Not checked",
                release is null ? "Not installed; release not checked" : "Not installed",
                []);
        }

        var recordedState = _stateStore.TryGetToolInstallation(fullRoot, catalogEntry.PackageId);
        var installedVersion = ResolveInstalledVersion(
            catalogEntry,
            targetPath,
            recordedState,
            release);
        if (release is null)
        {
            return new ToolPackageInspection(
                string.IsNullOrWhiteSpace(installedVersion)
                    ? ToolPackageInstallState.InstalledVersionUnknown
                    : ToolPackageInstallState.Current,
                fullRoot,
                targetPath,
                string.IsNullOrWhiteSpace(installedVersion) ? "Unknown" : installedVersion,
                "Not checked",
                string.IsNullOrWhiteSpace(installedVersion)
                    ? "Existing installation has no readable version marker"
                    : "Installed; check the selected release channel for updates",
                []);
        }

        if (string.IsNullOrWhiteSpace(installedVersion))
        {
            return new ToolPackageInspection(
                ToolPackageInstallState.InstalledVersionUnknown,
                fullRoot,
                targetPath,
                "Unknown",
                release.Manifest.PackageVersion,
                "Existing installation can be adopted through a verified update",
                [$"The existing {toolName} directory has no trusted version marker or toolkit state and does not match the selected release hashes."]);
        }

        if (!VersionsEqual(installedVersion, release.Manifest.PackageVersion))
        {
            var versionOrder = CompareVersions(installedVersion, release.Manifest.PackageVersion);
            if (versionOrder > 0)
            {
                return new ToolPackageInspection(
                    ToolPackageInstallState.SelectedReleaseOlder,
                    fullRoot,
                    targetPath,
                    installedVersion,
                    release.Manifest.PackageVersion,
                    $"Installed version is newer than the selected {release.Manifest.Channel} release",
                    [$"Replacing {installedVersion} with {release.Manifest.PackageVersion} requires an explicit channel switch."]);
            }

            return new ToolPackageInspection(
                ToolPackageInstallState.UpdateAvailable,
                fullRoot,
                targetPath,
                installedVersion,
                release.Manifest.PackageVersion,
                $"{release.Manifest.Channel} update available",
                []);
        }

        var findings = ValidateInstalledFiles(targetPath, release.Manifest);
        return findings.Count == 0
            ? new ToolPackageInspection(
                ToolPackageInstallState.Current,
                fullRoot,
                targetPath,
                installedVersion,
                release.Manifest.PackageVersion,
                "Installed release is current and verified",
                [])
            : new ToolPackageInspection(
                ToolPackageInstallState.RepairRequired,
                fullRoot,
                targetPath,
                installedVersion,
                release.Manifest.PackageVersion,
                $"Repair required: {findings.Count} package file(s) are missing or changed",
                findings);
    }

    public MaintenanceOperationResult Apply(
        ContentPackageCatalogEntry catalogEntry,
        ToolPackageProvisionResult package,
        string xPlaneRoot,
        ToolPackageAction action)
    {
        var log = new List<string>
        {
            $"[START] {action} {package.Release.Manifest.PackageId} {package.Release.Manifest.PackageVersion}",
            $"[TARGET] {xPlaneRoot}"
        };
        if (_isXPlaneRunning())
        {
            log.Add("[BLOCKED] X-Plane is running.");
            return MaintenanceOperationResult.Blocked("X-Plane is running. Close X-Plane before changing plugins.", log);
        }

        if (!LooksLikeInstallRoot(catalogEntry, xPlaneRoot))
        {
            log.Add("[BLOCKED] Package installation scope is invalid.");
            return MaintenanceOperationResult.Blocked("The selected package installation scope is invalid.", log);
        }

        var manifest = package.Release.Manifest;
        var toolName = catalogEntry.DisplayName;
        ValidatePackageForCatalog(catalogEntry, manifest);
        GitHubToolPackageReleaseSource.ValidateExtractedPackage(package.PackageDirectory, manifest);
        var fullRoot = Path.GetFullPath(xPlaneRoot);
        var targetPath = ResolveTarget(fullRoot, manifest.TargetPath);
        var inspection = Inspect(catalogEntry, fullRoot, package.Release);
        if (!ActionMatchesState(action, inspection.State))
        {
            log.Add($"[BLOCKED] {action} is not valid for {inspection.State}.");
            return MaintenanceOperationResult.Blocked(
                $"{action} is not valid for the current {toolName} state ({inspection.Status}).",
                log);
        }

        if (action is ToolPackageAction.Repair && inspection.State is ToolPackageInstallState.Current)
        {
            log.Add("[NO-CHANGE] Installed package files already match the release manifest.");
            return MaintenanceOperationResult.NoChange($"{toolName} is current and all package files are verified.", log);
        }

        RejectTargetPathLinks(fullRoot, targetPath);
        RejectNestedLinks(targetPath);
        var createdUtc = DateTimeOffset.UtcNow;
        var previousState = _stateStore.TryGetToolInstallation(fullRoot, manifest.PackageId);
        var sourceExisted = Directory.Exists(targetPath);
        var previousVersion = ResolveInstalledVersion(
            catalogEntry,
            targetPath,
            previousState,
            package.Release);
        var previousChannel = previousState?.Channel ?? InferChannel(previousVersion);
        var backupRoot = _stateStore.CreateToolBackupDirectory(fullRoot, manifest.PackageId, createdUtc);
        var backupPath = Path.Combine(backupRoot, Path.GetFileName(targetPath));
        if (sourceExisted)
        {
            CopyDirectory(targetPath, backupPath, overwrite: false);
            log.Add($"[BACKUP] Existing {toolName} installation copied to {backupPath}");
        }

        var targetParent = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException("Tool target has no parent directory.");
        Directory.CreateDirectory(targetParent);
        var stagePath = Path.Combine(targetParent, $".{Path.GetFileName(targetPath)}.stage-{Guid.NewGuid():N}");
        var rollbackPath = Path.Combine(targetParent, $".{Path.GetFileName(targetPath)}.rollback-{Guid.NewGuid():N}");
        var targetMoved = false;
        var stageMoved = false;
        try
        {
            CopyDirectory(package.PackageDirectory, stagePath, overwrite: false);
            if (sourceExisted)
            {
                PreserveLocalFiles(targetPath, stagePath, manifest, log);
            }

            ValidateInstallImage(stagePath, manifest);
            if (sourceExisted)
            {
                Directory.Move(targetPath, rollbackPath);
                targetMoved = true;
            }

            Directory.Move(stagePath, targetPath);
            stageMoved = true;
            ValidateInstallImage(targetPath, manifest);
            var installedFiles = CaptureInstalledFiles(targetPath, manifest);
            var backup = new ToolBackupGenerationState
            {
                BackupId = createdUtc.UtcDateTime.ToString("yyyyMMddTHHmmssfffZ"),
                BackupPath = sourceExisted ? backupPath : "",
                CreatedUtc = createdUtc,
                SourceExisted = sourceExisted,
                PreviousVersion = previousVersion,
                PreviousChannel = previousChannel,
                InstalledVersion = manifest.PackageVersion,
                InstalledFiles = installedFiles
            };
            _stateStore.UpdateToolInstallation(fullRoot, manifest.PackageId, state =>
            {
                state.TargetPath = targetPath;
                state.InstalledVersion = manifest.PackageVersion;
                state.Channel = manifest.Channel;
                state.LastOperationUtc = DateTimeOffset.UtcNow;
                state.LastOperation = action.ToString();
                state.InstalledFiles = installedFiles;
                state.ProtectedPaths = [.. manifest.ProtectedPaths];
                state.Backups.Add(backup);
            });

            targetMoved = false;
            TryDeleteDirectory(rollbackPath, log);

            log.Add($"[OK] {toolName} {manifest.PackageVersion} ({manifest.Channel}) installed and verified.");
            return MaintenanceOperationResult.Applied(
                $"{toolName} {manifest.PackageVersion} was {ActionPastTense(action)}. Restart X-Plane before using it.",
                sourceExisted ? [backupPath] : [],
                log);
        }
        catch
        {
            if (stageMoved && Directory.Exists(targetPath))
            {
                Directory.Delete(targetPath, recursive: true);
            }

            if (targetMoved && Directory.Exists(rollbackPath))
            {
                Directory.Move(rollbackPath, targetPath);
                log.Add($"[ROLLBACK] Previous {toolName} installation restored.");
            }

            throw;
        }
        finally
        {
            if (Directory.Exists(stagePath))
            {
                Directory.Delete(stagePath, recursive: true);
            }

            if (!targetMoved && Directory.Exists(rollbackPath))
            {
                Directory.Delete(rollbackPath, recursive: true);
            }
        }
    }

    public MaintenanceOperationResult Restore(ContentPackageCatalogEntry catalogEntry, string xPlaneRoot)
    {
        var toolName = catalogEntry.DisplayName;
        var log = new List<string> { $"[START] Restore {catalogEntry.PackageId}" };
        if (_isXPlaneRunning())
        {
            return MaintenanceOperationResult.Blocked("X-Plane is running. Close X-Plane before restoring plugins.", [.. log, "[BLOCKED] X-Plane is running."]);
        }

        if (!XPlaneInstallationLocator.LooksLikeXPlaneRoot(xPlaneRoot))
        {
            return MaintenanceOperationResult.Blocked("The selected X-Plane installation root is invalid.", [.. log, "[BLOCKED] Invalid X-Plane root."]);
        }

        var fullRoot = Path.GetFullPath(xPlaneRoot);
        var state = _stateStore.TryGetToolInstallation(fullRoot, catalogEntry.PackageId);
        var generation = state?.Backups
            .Where(backup => !backup.SourceExisted || Directory.Exists(backup.BackupPath))
            .OrderByDescending(backup => backup.CreatedUtc)
            .FirstOrDefault();
        if (state is null || generation is null)
        {
            return MaintenanceOperationResult.Blocked($"No valid {toolName} backup generation is available for this X-Plane installation.", [.. log, "[BLOCKED] No backup generation."]);
        }

        var targetPath = ResolveTarget(fullRoot, catalogEntry.TargetPath);
        var guardFindings = ValidateRestoreGuard(targetPath, state.InstalledFiles);
        if (guardFindings.Count > 0)
        {
            log.AddRange(guardFindings.Select(finding => $"[BLOCKED] {finding}"));
            return MaintenanceOperationResult.Blocked(
                $"Restore stopped because package-owned {toolName} files changed after the recorded installation.",
                log);
        }

        RejectTargetPathLinks(fullRoot, targetPath);
        RejectNestedLinks(targetPath);
        var createdUtc = DateTimeOffset.UtcNow;
        var preRestoreRoot = _stateStore.CreateToolBackupDirectory(fullRoot, catalogEntry.PackageId, createdUtc);
        var preRestorePath = Path.Combine(preRestoreRoot, Path.GetFileName(targetPath));
        var currentExisted = Directory.Exists(targetPath);
        if (currentExisted)
        {
            CopyDirectory(targetPath, preRestorePath, overwrite: false);
        }

        var targetParent = Path.GetDirectoryName(targetPath)!;
        var rollbackPath = Path.Combine(targetParent, $".{Path.GetFileName(targetPath)}.restore-{Guid.NewGuid():N}");
        var stagedPath = Path.Combine(targetParent, $".{Path.GetFileName(targetPath)}.stage-{Guid.NewGuid():N}");
        string restoredVersion;
        List<ToolInstalledFileState> restoredFiles;
        try
        {
            if (generation.SourceExisted)
            {
                CopyDirectory(generation.BackupPath, stagedPath, overwrite: false);
            }

            if (currentExisted)
            {
                Directory.Move(targetPath, rollbackPath);
            }

            if (generation.SourceExisted)
            {
                Directory.Move(stagedPath, targetPath);
            }

            restoredVersion = generation.SourceExisted
                ? ResolveRestoredVersion(catalogEntry, targetPath, generation.PreviousVersion)
                : "";
            restoredFiles = generation.SourceExisted ? CaptureAllFiles(targetPath, state.ProtectedPaths) : [];
            var preRestoreGeneration = new ToolBackupGenerationState
            {
                BackupId = createdUtc.UtcDateTime.ToString("yyyyMMddTHHmmssfffZ"),
                BackupPath = currentExisted ? preRestorePath : "",
                CreatedUtc = createdUtc,
                SourceExisted = currentExisted,
                PreviousVersion = state.InstalledVersion,
                PreviousChannel = state.Channel,
                InstalledVersion = restoredVersion,
                InstalledFiles = restoredFiles
            };
            _stateStore.UpdateToolInstallation(fullRoot, catalogEntry.PackageId, updated =>
            {
                updated.TargetPath = targetPath;
                updated.InstalledVersion = restoredVersion;
                updated.Channel = generation.PreviousChannel;
                updated.LastOperationUtc = DateTimeOffset.UtcNow;
                updated.LastOperation = "Restore";
                updated.InstalledFiles = restoredFiles;
                updated.Backups.Add(preRestoreGeneration);
            });

            TryDeleteDirectory(rollbackPath, log);
        }
        catch
        {
            if (Directory.Exists(targetPath))
            {
                Directory.Delete(targetPath, recursive: true);
            }

            if (Directory.Exists(rollbackPath))
            {
                Directory.Move(rollbackPath, targetPath);
            }

            throw;
        }
        finally
        {
            if (Directory.Exists(stagedPath))
            {
                Directory.Delete(stagedPath, recursive: true);
            }
        }

        log.Add(generation.SourceExisted
            ? $"[OK] Restored {toolName} {DisplayVersion(restoredVersion)}."
            : $"[OK] Removed the newly installed {toolName} directory and restored the previous absent state.");
        return MaintenanceOperationResult.Restored(
            generation.SourceExisted
                ? $"Restored {toolName} {DisplayVersion(restoredVersion)}. Restart X-Plane."
                : $"Restored the state from before {toolName} was installed.",
            currentExisted ? [preRestorePath] : [],
            log);
    }

    private static IReadOnlyList<string> ValidateInstalledFiles(string targetPath, ToolPackageManifest manifest)
    {
        var findings = new List<string>();
        foreach (var file in manifest.Files)
        {
            var path = ResolveTarget(targetPath, file.Path);
            if (!File.Exists(path))
            {
                findings.Add($"Missing: {file.Path}");
                continue;
            }

            if (ToolPackageManifestParser.IsProtectedPath(manifest, file.Path))
            {
                continue;
            }

            if (!FileMatches(path, file.Size, file.Sha256))
            {
                findings.Add($"Changed: {file.Path}");
            }
        }

        return findings;
    }

    private static IReadOnlyList<string> ValidateRestoreGuard(
        string targetPath,
        IReadOnlyList<ToolInstalledFileState> installedFiles)
    {
        if (installedFiles.Count == 0)
        {
            return ["No recorded post-install file hashes are available for a safe restore."];
        }

        var findings = new List<string>();
        foreach (var file in installedFiles.Where(file => !file.Protected))
        {
            var path = ResolveTarget(targetPath, file.RelativePath);
            if (!FileMatches(path, file.Size, file.Sha256))
            {
                findings.Add($"Changed after installation: {file.RelativePath}");
            }
        }

        return findings;
    }

    private static void PreserveLocalFiles(
        string sourceRoot,
        string stageRoot,
        ToolPackageManifest manifest,
        ICollection<string> log)
    {
        var packagePaths = manifest.Files.Select(file => file.Path).ToHashSet(PathComparer);
        foreach (var sourcePath in EnumerateFilesWithoutLinks(sourceRoot))
        {
            RejectLink(sourcePath, "Existing tool file");
            var relativePath = NormalizeRelativePath(Path.GetRelativePath(sourceRoot, sourcePath));
            var protectedPath = ToolPackageManifestParser.IsProtectedPath(manifest, relativePath);
            if (!protectedPath && packagePaths.Contains(relativePath))
            {
                continue;
            }

            var destination = ResolveTarget(stageRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(sourcePath, destination, overwrite: true);
            CopyUnixMode(sourcePath, destination);
            log.Add(protectedPath
                ? $"[PRESERVE] Protected user data: {relativePath}"
                : $"[PRESERVE] Local unowned file: {relativePath}");
        }
    }

    private static void ValidateInstallImage(string root, ToolPackageManifest manifest)
    {
        foreach (var file in manifest.Files)
        {
            var path = ResolveTarget(root, file.Path);
            if (!File.Exists(path))
            {
                throw new InvalidDataException($"Staged tool image is missing {file.Path}.");
            }

            if (!ToolPackageManifestParser.IsProtectedPath(manifest, file.Path)
                && !FileMatches(path, file.Size, file.Sha256))
            {
                throw new InvalidDataException($"Staged tool image failed verification: {file.Path}.");
            }
        }
    }

    private static List<ToolInstalledFileState> CaptureInstalledFiles(string root, ToolPackageManifest manifest) =>
        manifest.Files.Select(file =>
        {
            var path = ResolveTarget(root, file.Path);
            var info = new FileInfo(path);
            return new ToolInstalledFileState
            {
                RelativePath = file.Path,
                Size = info.Length,
                Sha256 = HashFile(path),
                Protected = ToolPackageManifestParser.IsProtectedPath(manifest, file.Path)
            };
        }).ToList();

    private static List<ToolInstalledFileState> CaptureAllFiles(string root, IReadOnlyList<string> protectedPaths) =>
        Directory.Exists(root)
            ? EnumerateFilesWithoutLinks(root)
                .Select(path => new ToolInstalledFileState
                {
                    RelativePath = NormalizeRelativePath(Path.GetRelativePath(root, path)),
                    Size = new FileInfo(path).Length,
                    Sha256 = HashFile(path),
                    Protected = ToolPackageManifestParser.IsProtectedPath(
                        protectedPaths,
                        NormalizeRelativePath(Path.GetRelativePath(root, path)))
                })
                .ToList()
            : [];

    private static void CopyDirectory(string sourceRoot, string destinationRoot, bool overwrite)
    {
        RejectLink(sourceRoot, "Tool directory");
        Directory.CreateDirectory(destinationRoot);
        foreach (var source in Directory.EnumerateFileSystemEntries(sourceRoot))
        {
            RejectLink(source, "Tool path");
            var destination = Path.Combine(destinationRoot, Path.GetFileName(source));
            if (Directory.Exists(source))
            {
                CopyDirectory(source, destination, overwrite);
            }
            else
            {
                File.Copy(source, destination, overwrite);
                CopyUnixMode(source, destination);
            }
        }
    }

    private static void CopyUnixMode(string source, string destination)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(destination, File.GetUnixFileMode(source));
        }
    }

    private static void RejectNestedLinks(string path)
    {
        if (!Directory.Exists(path))
        {
            RejectLink(path, "Tool path");
            return;
        }

        foreach (var _ in EnumerateFilesWithoutLinks(path))
        {
        }
    }

    private static IEnumerable<string> EnumerateFilesWithoutLinks(string root)
    {
        RejectLink(root, "Tool directory");
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var item in Directory.EnumerateFileSystemEntries(directory))
            {
                RejectLink(item, "Tool path");
                if (Directory.Exists(item))
                {
                    pending.Push(item);
                }
                else
                {
                    yield return item;
                }
            }
        }
    }

    private static void RejectTargetPathLinks(string root, string targetPath)
    {
        var relative = Path.GetRelativePath(root, targetPath);
        var current = Path.GetFullPath(root);
        foreach (var part in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            RejectLink(current, "Tool target path");
        }
    }

    private static void TryDeleteDirectory(string path, ICollection<string> log)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Add($"[WARN] Temporary rollback directory could not be removed: {path} ({ex.Message})");
        }
    }

    private static void RejectLink(string path, string label)
    {
        if (new FileInfo(path).LinkTarget is not null
            || new DirectoryInfo(path).LinkTarget is not null)
        {
            throw new InvalidDataException($"{label} is a symbolic link: {path}.");
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
            throw new InvalidDataException($"Tool target escapes its root: {relativePath}.");
        }

        return target;
    }

    private static string NormalizeRelativePath(string path) =>
        string.Join('/', path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries));

    private static bool FileMatches(string path, long size, string sha256) =>
        File.Exists(path)
        && new FileInfo(path).Length == size
        && HashFile(path).Equals(sha256, StringComparison.OrdinalIgnoreCase);

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string ResolveInstalledVersion(
        ContentPackageCatalogEntry catalogEntry,
        string targetPath,
        ToolInstallationState? recordedState,
        ToolPackageRelease? release)
    {
        if (!Directory.Exists(targetPath))
        {
            return "";
        }

        var markerVersion = ReadVersionMarker(targetPath, catalogEntry.VersionMarkerPath);
        if (!string.IsNullOrWhiteSpace(markerVersion))
        {
            return markerVersion;
        }

        if (recordedState is not null
            && PathsEqual(recordedState.TargetPath, targetPath)
            && !string.IsNullOrWhiteSpace(recordedState.InstalledVersion))
        {
            return recordedState.InstalledVersion.Trim();
        }

        return release is not null && ValidateInstalledFiles(targetPath, release.Manifest).Count == 0
            ? release.Manifest.PackageVersion
            : "";
    }

    private static string ResolveRestoredVersion(
        ContentPackageCatalogEntry catalogEntry,
        string targetPath,
        string recordedVersion)
    {
        if (!string.IsNullOrWhiteSpace(recordedVersion))
        {
            return recordedVersion.Trim();
        }

        return ReadVersionMarker(targetPath, catalogEntry.VersionMarkerPath);
    }

    private static string ReadVersionMarker(string targetPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return "";
        }

        var normalized = ToolPackageManifestParser.NormalizeRelativePath(relativePath);
        var path = Path.Combine(targetPath, Path.Combine(normalized.Split('/')));
        if (!File.Exists(path))
        {
            return "";
        }

        try
        {
            return File.ReadAllText(path).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)).Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            comparison);
    }

    private static string DisplayVersion(string version) =>
        string.IsNullOrWhiteSpace(version) ? "(version unknown)" : version;

    private static void ValidatePackageForCatalog(ContentPackageCatalogEntry catalog, ToolPackageManifest manifest)
    {
        if (!catalog.PackageId.Equals(manifest.PackageId, StringComparison.Ordinal)
            || !catalog.InstallScope.Equals(manifest.InstallScope, StringComparison.Ordinal)
            || !catalog.TargetPath.Equals(manifest.TargetPath, StringComparison.Ordinal)
            || !catalog.RepositoryUrl.TrimEnd('/').Equals(manifest.Repository.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
            || !manifest.SupportedProducts.ToHashSet(StringComparer.Ordinal).SetEquals(catalog.SupportedProducts))
        {
            throw new InvalidDataException($"Tool package does not match trusted catalog entry {catalog.PackageId}.");
        }
    }

    private static bool LooksLikeInstallRoot(ContentPackageCatalogEntry catalogEntry, string root)
    {
        if (catalogEntry.InstallScope == "xPlaneInstallation")
        {
            return XPlaneInstallationLocator.LooksLikeXPlaneRoot(root);
        }

        if (catalogEntry.InstallScope != "aircraftInstallation" || !Directory.Exists(root))
        {
            return false;
        }

        try
        {
            var fullRoot = Path.GetFullPath(root);
            return Directory.EnumerateFiles(fullRoot, "*.acf", SearchOption.TopDirectoryOnly).Any()
                && Directory.Exists(Path.Combine(fullRoot, "plugins", "xlua", "scripts"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool ActionMatchesState(ToolPackageAction action, ToolPackageInstallState state) =>
        action switch
        {
            ToolPackageAction.Install => state is ToolPackageInstallState.NotInstalled,
            ToolPackageAction.Update => state is ToolPackageInstallState.UpdateAvailable
                or ToolPackageInstallState.InstalledVersionUnknown,
            ToolPackageAction.SwitchChannel => state is ToolPackageInstallState.SelectedReleaseOlder,
            ToolPackageAction.Repair => state is ToolPackageInstallState.RepairRequired
                or ToolPackageInstallState.Current,
            _ => false
        };

    private static bool VersionsEqual(string left, string right) =>
        left.Trim().TrimStart('v', 'V').Equals(right.Trim().TrimStart('v', 'V'), StringComparison.OrdinalIgnoreCase);

    private static string InferChannel(string version) =>
        ParseComparableVersion(version)?.Suffix.Length > 0 ? "beta" : "stable";

    private static string ActionPastTense(ToolPackageAction action) =>
        action switch
        {
            ToolPackageAction.Install => "installed",
            ToolPackageAction.Update => "updated",
            ToolPackageAction.SwitchChannel => "switched to the selected release channel",
            ToolPackageAction.Repair => "repaired",
            _ => "applied"
        };

    private static int? CompareVersions(string left, string right)
    {
        var leftVersion = ParseComparableVersion(left);
        var rightVersion = ParseComparableVersion(right);
        if (leftVersion is null || rightVersion is null)
        {
            return null;
        }

        var numeric = leftVersion.Value.Major.CompareTo(rightVersion.Value.Major);
        if (numeric == 0)
        {
            numeric = leftVersion.Value.Minor.CompareTo(rightVersion.Value.Minor);
        }

        if (numeric == 0)
        {
            numeric = leftVersion.Value.Patch.CompareTo(rightVersion.Value.Patch);
        }

        if (numeric != 0)
        {
            return numeric;
        }

        if (leftVersion.Value.Suffix.Length == 0 || rightVersion.Value.Suffix.Length == 0)
        {
            return leftVersion.Value.Suffix.Length == rightVersion.Value.Suffix.Length
                ? 0
                : leftVersion.Value.Suffix.Length == 0 ? 1 : -1;
        }

        return StringComparer.OrdinalIgnoreCase.Compare(leftVersion.Value.Suffix, rightVersion.Value.Suffix);
    }

    private static ComparableVersion? ParseComparableVersion(string value)
    {
        var match = Regex.Match(
            value.Trim().TrimStart('v', 'V'),
            @"^(?<major>\d+)(?:\.(?<minor>\d+))?(?:\.(?<patch>\d+))?(?<suffix>.*)$",
            RegexOptions.CultureInvariant);
        if (!match.Success
            || !int.TryParse(match.Groups["major"].Value, out var major)
            || !TryParsePart(match.Groups["minor"].Value, out var minor)
            || !TryParsePart(match.Groups["patch"].Value, out var patch))
        {
            return null;
        }

        return new ComparableVersion(
            major,
            minor,
            patch,
            match.Groups["suffix"].Value.TrimStart('-', '.', '_'));
    }

    private static bool TryParsePart(string value, out int result)
    {
        if (value.Length == 0)
        {
            result = 0;
            return true;
        }

        return int.TryParse(value, out result);
    }

    private readonly record struct ComparableVersion(int Major, int Minor, int Patch, string Suffix);

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
}
