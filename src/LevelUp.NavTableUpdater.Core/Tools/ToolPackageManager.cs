using System.Security.Cryptography;
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

internal enum ToolPackageTransactionPhase
{
    BackupCreated,
    StageValidated,
    TargetActivated
}

public sealed class ToolPackageManager
{
    private readonly ToolStateStore _stateStore;
    private readonly string _runtimeIdentifier;
    private readonly Func<bool> _isXPlaneRunning;
    private readonly Action<ToolPackageTransactionPhase>? _transactionObserver;

    public ToolPackageManager(
        ToolStateStore stateStore,
        Func<bool>? isXPlaneRunning = null,
        string? runtimeIdentifier = null)
        : this(stateStore, isXPlaneRunning, runtimeIdentifier, null)
    {
    }

    internal ToolPackageManager(
        ToolStateStore stateStore,
        Func<bool>? isXPlaneRunning,
        string? runtimeIdentifier,
        Action<ToolPackageTransactionPhase>? transactionObserver)
    {
        _stateStore = stateStore;
        _runtimeIdentifier = runtimeIdentifier ?? PackagePlatform.Current;
        _isXPlaneRunning = isXPlaneRunning ?? XPlaneProcessDetector.IsXPlaneRunning;
        _transactionObserver = transactionObserver;
    }

    public ToolPackageInspection Inspect(
        ContentPackageCatalogEntry catalogEntry,
        string? xPlaneRoot,
        ToolPackageRelease? release)
    {
        if (!catalogEntry.SupportsPlatform(_runtimeIdentifier))
            return new(ToolPackageInstallState.TargetUnavailable, xPlaneRoot ?? "", "", "-",
                release?.Manifest.PackageVersion ?? "Not checked",
                PackagePlatform.Unavailable(catalogEntry.DisplayName, catalogEntry.SupportedPlatforms, _runtimeIdentifier), []);
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
            IReadOnlyList<string> recordedFindings = recordedState is null
                ? []
                : ValidateRestoreGuard(targetPath, recordedState.InstalledFiles, recordedState.RetiredFiles);
            return new ToolPackageInspection(
                string.IsNullOrWhiteSpace(installedVersion)
                    ? ToolPackageInstallState.InstalledVersionUnknown
                    : recordedFindings.Count == 0
                        ? ToolPackageInstallState.Current
                        : ToolPackageInstallState.RepairRequired,
                fullRoot,
                targetPath,
                string.IsNullOrWhiteSpace(installedVersion) ? "Unknown" : installedVersion,
                "Not checked",
                string.IsNullOrWhiteSpace(installedVersion)
                    ? "Existing installation has no readable version marker"
                    : recordedFindings.Count == 0
                        ? "Installed and recorded files are verified; check the selected release channel for updates"
                        : $"Repair required: {recordedFindings.Count} recorded package file(s) are missing or changed",
                recordedFindings);
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
        ToolPackageAction action,
        IReadOnlyList<ToolResolvedDependency>? resolvedDependencies = null)
    {
        var log = new List<string>
        {
            $"[START] {action} {package.Release.Manifest.PackageId} {package.Release.Manifest.PackageVersion}",
            $"[TARGET] {xPlaneRoot}"
        };
        if (!catalogEntry.SupportsPlatform(_runtimeIdentifier))
            return MaintenanceOperationResult.Blocked(
                PackagePlatform.Unavailable(catalogEntry.DisplayName, catalogEntry.SupportedPlatforms, _runtimeIdentifier), log);

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
        var installedDependencies = ValidateResolvedDependencies(manifest, resolvedDependencies);
        GitHubToolPackageReleaseSource.ValidateExtractedPackage(package.PackageDirectory, manifest);
        var fullRoot = Path.GetFullPath(xPlaneRoot);
        var targetPath = ResolveTarget(fullRoot, manifest.TargetPath);
        var dependencyBlockers = FindRestoreDependencyBlockers(fullRoot, manifest.PackageId, manifest.PackageVersion);
        if (dependencyBlockers.Count > 0)
        {
            log.AddRange(dependencyBlockers.Select(blocker => $"[BLOCKED] {blocker}"));
            return MaintenanceOperationResult.Blocked(
                $"{action} stopped because an installed package requires a newer {toolName} version.",
                log);
        }
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
            var verifiedFiles = CaptureInstalledFiles(targetPath, manifest);
            _stateStore.UpdateToolInstallation(fullRoot, manifest.PackageId, state =>
            {
                state.TargetPath = targetPath;
                state.InstalledVersion = manifest.PackageVersion;
                state.Channel = manifest.Channel;
                state.LastOperationUtc = DateTimeOffset.UtcNow;
                state.LastOperation = "Verify";
                state.InstalledFiles = verifiedFiles;
                state.ProtectedPaths = [.. manifest.ProtectedPaths];
                state.RetiredFiles = ExpectedAbsentRetiredPaths(manifest);
                state.Dependencies = installedDependencies;
            });
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
        var retirementBlockers = ValidateRetiredFilesForApply(targetPath, manifest, previousState);
        if (retirementBlockers.Count > 0)
        {
            log.AddRange(retirementBlockers.Select(blocker => $"[BLOCKED] {blocker}"));
            return MaintenanceOperationResult.Blocked(
                $"{action} stopped because a retired {toolName} file is missing or cannot be safely removed.",
                log);
        }
        if (manifest.RetiredFiles.Count > 0)
        {
            var replacementPaths = SamePathReplacementPaths(manifest);
            var removedPaths = ExpectedAbsentRetiredPaths(manifest);
            if (replacementPaths.Count > 0)
                log.Add($"[MIGRATION] Replace legacy files at the same paths: {string.Join(", ", replacementPaths)}");
            if (removedPaths.Count > 0)
                log.Add($"[MIGRATION] Remove obsolete files: {string.Join(", ", removedPaths)}");
            log.Add($"[MIGRATION] Install replacement files: {string.Join(", ", manifest.Files.Select(file => file.Path))}");
            log.Add($"[MIGRATION] Preserve protected files: {string.Join(", ", manifest.ProtectedPaths)}");
        }
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
            _transactionObserver?.Invoke(ToolPackageTransactionPhase.BackupCreated);
            CopyDirectory(package.PackageDirectory, stagePath, overwrite: false);
            if (sourceExisted)
            {
                PreserveLocalFiles(targetPath, stagePath, manifest, log);
            }

            ValidateInstallImage(stagePath, manifest);
            _transactionObserver?.Invoke(ToolPackageTransactionPhase.StageValidated);
            if (sourceExisted)
            {
                Directory.Move(targetPath, rollbackPath);
                targetMoved = true;
            }

            Directory.Move(stagePath, targetPath);
            stageMoved = true;
            _transactionObserver?.Invoke(ToolPackageTransactionPhase.TargetActivated);
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
                InstalledFiles = installedFiles,
                PreviousDependencies = CloneDependencies(previousState?.Dependencies ?? []),
                PreviousProtectedPaths = [.. previousState?.ProtectedPaths ?? []],
                PreviousRetiredFiles = [.. previousState?.RetiredFiles ?? []]
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
                state.RetiredFiles = ExpectedAbsentRetiredPaths(manifest);
                state.Dependencies = installedDependencies;
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
        if (!catalogEntry.SupportsPlatform(_runtimeIdentifier))
            return MaintenanceOperationResult.Blocked(
                PackagePlatform.Unavailable(catalogEntry.DisplayName, catalogEntry.SupportedPlatforms, _runtimeIdentifier), log);

        if (_isXPlaneRunning())
        {
            return MaintenanceOperationResult.Blocked("X-Plane is running. Close X-Plane before restoring plugins.", [.. log, "[BLOCKED] X-Plane is running."]);
        }

        if (!LooksLikeInstallRoot(catalogEntry, xPlaneRoot))
        {
            return MaintenanceOperationResult.Blocked("The selected package installation root is invalid.", [.. log, "[BLOCKED] Invalid package installation root."]);
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

        var dependencyBlockers = FindRestoreDependencyBlockers(
            fullRoot,
            catalogEntry.PackageId,
            generation.PreviousVersion);
        if (dependencyBlockers.Count > 0)
        {
            log.AddRange(dependencyBlockers.Select(blocker => $"[BLOCKED] {blocker}"));
            return MaintenanceOperationResult.Blocked(
                $"Restore stopped because an installed package depends on {toolName}.",
                log);
        }

        var targetPath = ResolveTarget(fullRoot, catalogEntry.TargetPath);
        var guardFindings = ValidateRestoreGuard(targetPath, state.InstalledFiles, state.RetiredFiles);
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
                InstalledFiles = restoredFiles,
                PreviousDependencies = CloneDependencies(state.Dependencies),
                PreviousProtectedPaths = [.. state.ProtectedPaths],
                PreviousRetiredFiles = [.. state.RetiredFiles]
            };
            _stateStore.UpdateToolInstallation(fullRoot, catalogEntry.PackageId, updated =>
            {
                updated.TargetPath = targetPath;
                updated.InstalledVersion = restoredVersion;
                updated.Channel = generation.PreviousChannel;
                updated.LastOperationUtc = DateTimeOffset.UtcNow;
                updated.LastOperation = "Restore";
                updated.InstalledFiles = restoredFiles;
                updated.ProtectedPaths = [.. generation.PreviousProtectedPaths];
                updated.RetiredFiles = [.. generation.PreviousRetiredFiles];
                updated.Dependencies = CloneDependencies(generation.PreviousDependencies);
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

        var installedPaths = manifest.Files.Select(file => file.Path).ToHashSet(PathComparer);
        foreach (var retiredFile in manifest.RetiredFiles.Where(file => !installedPaths.Contains(file.Path)))
        {
            var path = ResolveTarget(targetPath, retiredFile.Path);
            if (File.Exists(path) || Directory.Exists(path))
            {
                findings.Add($"Retired file is present: {retiredFile.Path}");
            }
        }

        return findings;
    }

    private static IReadOnlyList<string> ValidateRestoreGuard(
        string targetPath,
        IReadOnlyList<ToolInstalledFileState> installedFiles,
        IReadOnlyList<string> retiredFiles)
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

        foreach (var retiredFile in retiredFiles)
        {
            var path = ResolveTarget(targetPath, retiredFile);
            if (File.Exists(path) || Directory.Exists(path))
            {
                findings.Add($"Retired file appeared after installation: {retiredFile}");
            }
        }

        return findings;
    }

    private static IReadOnlyList<string> ValidateRetiredFilesForApply(
        string targetPath,
        ToolPackageManifest manifest,
        ToolInstallationState? previousState)
    {
        var blockers = new List<string>();
        foreach (var retiredFile in manifest.RetiredFiles)
        {
            var path = ResolveTarget(targetPath, retiredFile.Path);
            if (!File.Exists(path))
            {
                if (Directory.Exists(path))
                {
                    blockers.Add($"Retired path is not a regular file: {retiredFile.Path}");
                }
                else if (!retiredFile.Optional
                    && (previousState is null || !VersionsEqual(previousState.InstalledVersion, manifest.PackageVersion)))
                {
                    blockers.Add($"Required retired file is missing: {retiredFile.Path}");
                }
                continue;
            }

            RejectLink(path, "Retired tool file");
            var info = new FileInfo(path);
            var hash = HashFile(path);
            var manifestAuthorizes = retiredFile.SourceSha256.Contains(hash, StringComparer.OrdinalIgnoreCase);
            var stateAuthorizes = previousState is not null
                && PathsEqual(previousState.TargetPath, targetPath)
                && previousState.InstalledFiles.Any(file =>
                    !file.Protected
                    && PathComparer.Equals(file.RelativePath, retiredFile.Path)
                    && file.Size == info.Length
                    && file.Sha256.Equals(hash, StringComparison.OrdinalIgnoreCase));
            if (!manifestAuthorizes && !stateAuthorizes)
            {
                blockers.Add($"Retired file has an unknown or locally modified SHA-256: {retiredFile.Path} ({hash})");
            }
        }

        return blockers;
    }

    private static void PreserveLocalFiles(
        string sourceRoot,
        string stageRoot,
        ToolPackageManifest manifest,
        ICollection<string> log)
    {
        var packagePaths = manifest.Files.Select(file => file.Path).ToHashSet(PathComparer);
        var retiredPaths = manifest.RetiredFiles.Select(file => file.Path).ToHashSet(PathComparer);
        foreach (var sourcePath in EnumerateFilesWithoutLinks(sourceRoot))
        {
            RejectLink(sourcePath, "Existing tool file");
            var relativePath = NormalizeRelativePath(Path.GetRelativePath(sourceRoot, sourcePath));
            var protectedPath = ToolPackageManifestParser.IsProtectedPath(manifest, relativePath);
            if (retiredPaths.Contains(relativePath))
            {
                log.Add(packagePaths.Contains(relativePath)
                    ? $"[REPLACE] Authorized legacy file replaced at the same path: {relativePath}"
                    : $"[RETIRE] Managed legacy file removed from the new installation: {relativePath}");
                continue;
            }
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

        var installedPaths = manifest.Files.Select(file => file.Path).ToHashSet(PathComparer);
        foreach (var retiredFile in manifest.RetiredFiles.Where(file => !installedPaths.Contains(file.Path)))
        {
            var path = ResolveTarget(root, retiredFile.Path);
            if (File.Exists(path) || Directory.Exists(path))
            {
                throw new InvalidDataException($"Staged tool image still contains retired file: {retiredFile.Path}.");
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

    private static List<string> SamePathReplacementPaths(ToolPackageManifest manifest)
    {
        var installedPaths = manifest.Files.Select(file => file.Path).ToHashSet(PathComparer);
        return manifest.RetiredFiles
            .Where(file => installedPaths.Contains(file.Path))
            .Select(file => file.Path)
            .ToList();
    }

    private static List<string> ExpectedAbsentRetiredPaths(ToolPackageManifest manifest)
    {
        var installedPaths = manifest.Files.Select(file => file.Path).ToHashSet(PathComparer);
        return manifest.RetiredFiles
            .Where(file => !installedPaths.Contains(file.Path))
            .Select(file => file.Path)
            .ToList();
    }

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
            || catalog.Distribution.ManifestSchemaVersionFor(manifest.Channel) != manifest.SchemaVersion
            || !manifest.SupportedProducts.ToHashSet(StringComparer.Ordinal).SetEquals(catalog.SupportedProducts)
            || !manifest.SupportedPlatforms.ToHashSet(StringComparer.Ordinal).SetEquals(catalog.SupportedPlatforms))
        {
            throw new InvalidDataException($"Tool package does not match trusted catalog entry {catalog.PackageId}.");
        }
    }

    private List<ToolInstalledDependencyState> ValidateResolvedDependencies(
        ToolPackageManifest manifest,
        IReadOnlyList<ToolResolvedDependency>? resolvedDependencies)
    {
        resolvedDependencies ??= [];
        if (manifest.Dependencies.Count != resolvedDependencies.Count)
        {
            throw new InvalidDataException("The resolved tool dependency set does not match the release manifest.");
        }

        if (resolvedDependencies.Select(dependency => dependency.PackageId).Distinct(StringComparer.Ordinal).Count()
            != resolvedDependencies.Count)
        {
            throw new InvalidDataException("The resolved tool dependency set contains duplicate package IDs.");
        }

        var resolvedById = resolvedDependencies.ToDictionary(dependency => dependency.PackageId, StringComparer.Ordinal);
        var installed = new List<ToolInstalledDependencyState>();
        foreach (var requirement in manifest.Dependencies)
        {
            var dependency = resolvedById.GetValueOrDefault(requirement.PackageId);
            var recorded = dependency is null || string.IsNullOrWhiteSpace(dependency.InstallationRoot)
                ? null
                : _stateStore.TryGetToolInstallation(dependency.InstallationRoot, dependency.PackageId);
            if (dependency is null
                || !dependency.MinimumVersion.Equals(requirement.MinimumVersion, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(dependency.InstallationRoot)
                || !ToolPackageVersion.IsAtLeast(dependency.ResolvedVersion, requirement.MinimumVersion)
                || recorded is null
                || !VersionsEqual(recorded.InstalledVersion, dependency.ResolvedVersion)
                || !ToolPackageVersion.IsAtLeast(recorded.InstalledVersion, requirement.MinimumVersion))
            {
                throw new InvalidDataException($"Required package {requirement.PackageId} is missing or below its minimum version.");
            }

            installed.Add(new ToolInstalledDependencyState
            {
                PackageId = dependency.PackageId,
                MinimumVersion = dependency.MinimumVersion,
                InstallationRoot = Path.GetFullPath(dependency.InstallationRoot),
                ResolvedVersion = dependency.ResolvedVersion
            });
        }

        return installed;
    }

    private IReadOnlyList<string> FindRestoreDependencyBlockers(string root, string packageId, string restoredVersion) =>
        _stateStore.Load().ToolInstallations.Values
            .Where(installation => !string.IsNullOrWhiteSpace(installation.InstalledVersion))
            .SelectMany(installation => installation.Dependencies.Select(dependency => (installation, dependency)))
            .Where(item => item.dependency.PackageId.Equals(packageId, StringComparison.Ordinal)
                && PathsEqual(item.dependency.InstallationRoot, root)
                && !ToolPackageVersion.IsAtLeast(restoredVersion, item.dependency.MinimumVersion))
            .Select(item => $"{item.installation.PackageId} requires {packageId}"
                + (string.IsNullOrWhiteSpace(item.dependency.MinimumVersion) ? "." : $" {item.dependency.MinimumVersion} or newer."))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static List<ToolInstalledDependencyState> CloneDependencies(IEnumerable<ToolInstalledDependencyState> dependencies) =>
        dependencies.Select(dependency => new ToolInstalledDependencyState
        {
            PackageId = dependency.PackageId,
            MinimumVersion = dependency.MinimumVersion,
            InstallationRoot = dependency.InstallationRoot,
            ResolvedVersion = dependency.ResolvedVersion
        }).ToList();

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
        ToolPackageVersion.TryParse(version, out var parsed) && parsed.Suffix.Length > 0 ? "beta" : "stable";

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
        try
        {
            return ToolPackageVersion.Compare(left, right);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
}
