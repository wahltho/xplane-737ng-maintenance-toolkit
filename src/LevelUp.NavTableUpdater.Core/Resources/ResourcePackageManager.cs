using System.Security.Cryptography;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.State;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace LevelUp.NavTableUpdater.Core.Resources;

public sealed class ResourcePackageManager
{
    private const long FreeSpaceMarginBytes = 64L * 1024 * 1024;
    private readonly ToolStateStore _stateStore;

    public ResourcePackageManager(ToolStateStore stateStore)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    }

    public ResourcePackageInspection Inspect(
        ContentPackageCatalogEntry catalogEntry,
        ResourcePackageRelease? release,
        bool verifyHash = false)
    {
        ValidateCatalogEntry(catalogEntry);
        var state = _stateStore.TryGetResourceInstallation(catalogEntry.PackageId);
        if (state is null)
        {
            return new ResourcePackageInspection(
                ResourcePackageState.NotInstalled,
                "-",
                release?.Manifest.PackageVersion ?? "Not checked",
                "",
                "",
                "This resource has not been installed through the Toolkit.");
        }

        var availableVersion = release?.Manifest.PackageVersion ?? "Not checked";
        if (!Directory.Exists(state.TargetPath))
        {
            return new ResourcePackageInspection(
                ResourcePackageState.Missing,
                state.PackageVersion,
                availableVersion,
                state.DestinationDirectory,
                state.TargetPath,
                "The recorded resource installation is missing and can be installed again.");
        }

        var integrity = InspectInstallation(state, verifyHash);
        if (!integrity.Valid)
        {
            return new ResourcePackageInspection(
                ResourcePackageState.VerificationFailed,
                state.PackageVersion,
                availableVersion,
                state.DestinationDirectory,
                state.TargetPath,
                integrity.Status);
        }

        if (release is not null
            && !VersionsEqual(state.PackageVersion, release.Manifest.PackageVersion))
        {
            return new ResourcePackageInspection(
                ResourcePackageState.UpdateAvailable,
                state.PackageVersion,
                availableVersion,
                state.DestinationDirectory,
                state.TargetPath,
                $"Resource {release.Manifest.PackageVersion} is available.");
        }

        return new ResourcePackageInspection(
            ResourcePackageState.Current,
            state.PackageVersion,
            availableVersion,
            state.DestinationDirectory,
            state.TargetPath,
            release is null
                ? verifyHash
                    ? "The installed resource is present and verified. Check releases to compare versions."
                    : "The installed resource is present. Use Verify for a full SHA-256 check."
                : verifyHash
                    ? "The installed resource is current and verified."
                    : "The installed resource is current. Use Verify for a full SHA-256 check.");
    }

    public void ValidateDestination(
        ContentPackageCatalogEntry catalogEntry,
        ResourcePackageRelease release,
        string destinationDirectory)
    {
        ValidateCatalogEntry(catalogEntry);
        ValidateRelease(catalogEntry, release);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        var directory = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(directory);
        RejectLink(directory, "Resource destination");
        var targetPath = TargetPath(directory, release.Manifest.TargetDirectory);
        var existingState = _stateStore.TryGetResourceInstallation(catalogEntry.PackageId);

        if (File.Exists(targetPath))
        {
            throw new InvalidOperationException(
                $"A file already exists where the resource directory would be installed: {targetPath}.");
        }

        if (Directory.Exists(targetPath))
        {
            if (existingState is null || !PathsEqual(existingState.TargetPath, targetPath))
            {
                throw new InvalidOperationException(
                    $"An unowned directory already exists at the resource destination: {targetPath}.");
            }

            var integrity = InspectInstallation(existingState, verifyHash: true);
            if (!integrity.Valid)
            {
                throw new InvalidOperationException(
                    $"The existing resource installation cannot be replaced safely. {integrity.Status}");
            }
        }
        else if (existingState is not null
            && Directory.Exists(existingState.TargetPath)
            && !PathsEqual(existingState.TargetPath, targetPath))
        {
            throw new InvalidOperationException(
                "Remove the recorded resource installation before installing it in a different location.");
        }

        EnsureFreeSpace(directory, release.Manifest.Archive.Size + release.Manifest.ExtractedSize + FreeSpaceMarginBytes);
    }

    public ResourcePackageOperationResult InstallToDirectory(
        ContentPackageCatalogEntry catalogEntry,
        ResourcePackageProvisionResult provisioned,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        ValidateCatalogEntry(catalogEntry);
        ValidateRelease(catalogEntry, provisioned.Release);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        var directory = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(directory);
        var manifest = provisioned.Release.Manifest;
        var targetPath = TargetPath(directory, manifest.TargetDirectory);
        var sourcePath = Path.GetFullPath(provisioned.ArchivePath);
        if (provisioned.Temporary)
        {
            EnsureDirectChild(directory, sourcePath);
        }

        var stagingPath = TargetPath(directory, $".{manifest.TargetDirectory}.{Guid.NewGuid():N}.staging");
        var rollbackPath = TargetPath(directory, $".{manifest.TargetDirectory}.{Guid.NewGuid():N}.rollback");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!FileMatches(sourcePath, manifest.Archive.Size, manifest.Archive.Sha256))
            {
                throw new InvalidDataException("Downloaded resource archive failed size/SHA-256 verification.");
            }

            ValidateDestination(catalogEntry, provisioned.Release, directory);
            var existingState = _stateStore.TryGetResourceInstallation(catalogEntry.PackageId);
            if (existingState is not null
                && VersionsEqual(existingState.PackageVersion, manifest.PackageVersion)
                && PathsEqual(existingState.TargetPath, targetPath))
            {
                return new ResourcePackageOperationResult(
                    true,
                    false,
                    $"{catalogEntry.DisplayName} {manifest.PackageVersion} is already installed and verified.",
                    targetPath);
            }

            ExtractVerifiedArchive(sourcePath, manifest, stagingPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var movedExisting = false;
            var installedNew = false;
            try
            {
                if (Directory.Exists(targetPath))
                {
                    Directory.Move(targetPath, rollbackPath);
                    movedExisting = true;
                }

                Directory.Move(stagingPath, targetPath);
                installedNew = true;
                _stateStore.UpdateResourceInstallation(catalogEntry.PackageId, state =>
                {
                    state.PackageId = manifest.PackageId;
                    state.PackageVersion = manifest.PackageVersion;
                    state.ReleaseTag = manifest.ReleaseTag;
                    state.Channel = manifest.Channel;
                    state.DestinationDirectory = directory;
                    state.TargetPath = targetPath;
                    state.InstalledFiles = manifest.Files.Select(file => new ResourceInstalledFileState
                    {
                        RelativePath = file.Path,
                        Size = file.Size,
                        Sha256 = file.Sha256
                    }).ToList();
                    state.LastOperationUtc = DateTimeOffset.UtcNow;
                });

                if (movedExisting)
                {
                    try
                    {
                        Directory.Delete(rollbackPath, recursive: true);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // Keep the hidden previous generation rather than risking the new verified install.
                    }

                    movedExisting = false;
                }
            }
            catch
            {
                if (installedNew && Directory.Exists(targetPath))
                {
                    Directory.Delete(targetPath, recursive: true);
                }

                if (movedExisting && Directory.Exists(rollbackPath))
                {
                    Directory.Move(rollbackPath, targetPath);
                }

                throw;
            }

            return new ResourcePackageOperationResult(
                true,
                true,
                $"{catalogEntry.DisplayName} {manifest.PackageVersion} was downloaded, verified and installed.",
                targetPath);
        }
        finally
        {
            DeleteDirectoryIfPresent(stagingPath);
            if (provisioned.Temporary && File.Exists(sourcePath))
            {
                File.Delete(sourcePath);
            }
        }
    }

    public ResourcePackageOperationResult Remove(ContentPackageCatalogEntry catalogEntry)
    {
        ValidateCatalogEntry(catalogEntry);
        var state = _stateStore.TryGetResourceInstallation(catalogEntry.PackageId);
        if (state is null)
        {
            return new ResourcePackageOperationResult(true, false, "No recorded resource installation exists.", "");
        }

        if (Directory.Exists(state.TargetPath))
        {
            var integrity = InspectInstallation(state, verifyHash: true);
            if (!integrity.Valid)
            {
                throw new InvalidOperationException(
                    $"The recorded resource installation has changed and will not be removed automatically. {integrity.Status}");
            }

            Directory.Delete(state.TargetPath, recursive: true);
        }

        _stateStore.RemoveResourceInstallation(catalogEntry.PackageId);
        return new ResourcePackageOperationResult(
            true,
            true,
            $"The recorded {catalogEntry.DisplayName} installation was removed.",
            state.TargetPath);
    }

    private static void ExtractVerifiedArchive(
        string archivePath,
        ResourcePackageManifest manifest,
        string stagingPath,
        CancellationToken cancellationToken)
    {
        var expected = manifest.Files.ToDictionary(file => file.Path, StringComparer.Ordinal);
        using var archive = OpenArchive(archivePath);
        if (archive.Type is not ArchiveType.SevenZip || archive.IsEncrypted)
        {
            throw new InvalidDataException("Resource archive must be an unencrypted 7z archive.");
        }

        ValidateArchiveEntries(archive, manifest, expected);
        Directory.CreateDirectory(stagingPath);

        using var reader = archive.ExtractAllEntries();
        var extractedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rootPrefix = manifest.ArchiveRoot + "/";
        while (reader.MoveToNextEntry())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = reader.Entry;
            var memberPath = NormalizeArchivePath(entry.Key ?? "");
            if (memberPath.Equals(manifest.ArchiveRoot, StringComparison.Ordinal))
            {
                continue;
            }

            if (!memberPath.StartsWith(rootPrefix, StringComparison.Ordinal)
                || !string.IsNullOrWhiteSpace(entry.LinkTarget))
            {
                throw new InvalidDataException(
                    $"Resource archive changed during extraction or contains an unsafe entry: {memberPath}.");
            }

            var relativePath = memberPath[rootPrefix.Length..];
            if (entry.IsDirectory)
            {
                continue;
            }

            if (!extractedFiles.Add(relativePath)
                || !expected.TryGetValue(relativePath, out var expectedFile)
                || entry.Size != expectedFile.Size)
            {
                throw new InvalidDataException(
                    $"Resource archive entry is missing from or differs from the manifest: {relativePath}.");
            }

            var outputPath = ResolveChildPath(stagingPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            using var input = reader.OpenEntryStream();
            using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[131072];
            long total = 0;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                total += read;
                if (total > expectedFile.Size)
                {
                    throw new InvalidDataException($"Resource archive entry exceeds its declared size: {relativePath}.");
                }

                hash.AppendData(buffer, 0, read);
                output.Write(buffer, 0, read);
            }

            output.Flush(flushToDisk: true);
            var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (total != expectedFile.Size
                || !actualHash.Equals(expectedFile.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Extracted resource file failed size/SHA-256 verification: {relativePath}.");
            }
        }

        if (extractedFiles.Count != expected.Count
            || expected.Keys.Any(path => !extractedFiles.Contains(path)))
        {
            throw new InvalidDataException("Extracted resource file inventory does not match the manifest.");
        }

        if (!FileMatches(archivePath, manifest.Archive.Size, manifest.Archive.Sha256))
        {
            throw new InvalidDataException("Resource archive changed during extraction.");
        }

        var extractedState = new ResourceInstallationState
        {
            TargetPath = stagingPath,
            InstalledFiles = manifest.Files.Select(file => new ResourceInstalledFileState
            {
                RelativePath = file.Path,
                Size = file.Size,
                Sha256 = file.Sha256
            }).ToList()
        };
        var integrity = InspectInstallation(extractedState, verifyHash: false);
        if (!integrity.Valid)
        {
            throw new InvalidDataException($"Staged resource verification failed. {integrity.Status}");
        }
    }

    private static void ValidateArchiveEntries(
            IArchive archive,
            ResourcePackageManifest manifest,
            IReadOnlyDictionary<string, ResourcePackageFile> expected)
    {
        var seenEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileCount = 0;
        var rootPrefix = manifest.ArchiveRoot + "/";
        foreach (var entry in archive.Entries)
        {
            var memberPath = NormalizeArchivePath(entry.Key ?? "");
            if (!seenEntries.Add(memberPath))
            {
                throw new InvalidDataException($"Resource archive contains a duplicate path: {memberPath}.");
            }

            if (!string.IsNullOrWhiteSpace(entry.LinkTarget))
            {
                throw new InvalidDataException($"Resource archive symbolic links are not permitted: {memberPath}.");
            }

            if (memberPath.Equals(manifest.ArchiveRoot, StringComparison.Ordinal))
            {
                if (!entry.IsDirectory)
                {
                    throw new InvalidDataException("Resource archive root must be a directory.");
                }

                continue;
            }

            if (!memberPath.StartsWith(rootPrefix, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Resource archive entry is outside the declared archive root: {memberPath}.");
            }

            var relativePath = memberPath[rootPrefix.Length..];
            if (!ResourcePackageManifestParser.IsSafeRelativePath(relativePath))
            {
                throw new InvalidDataException($"Resource archive contains an unsafe path: {memberPath}.");
            }

            if (entry.IsDirectory)
            {
                continue;
            }

            if (!seenFiles.Add(relativePath)
                || !expected.TryGetValue(relativePath, out var expectedFile)
                || entry.Size != expectedFile.Size)
            {
                throw new InvalidDataException(
                    $"Resource archive entry is missing from or differs from the manifest: {relativePath}.");
            }

            fileCount++;
        }

        if (fileCount != expected.Count
            || expected.Keys.Any(path => !seenFiles.Contains(path)))
        {
            throw new InvalidDataException("Resource archive file inventory does not match the manifest.");
        }
    }

    private static InstallationIntegrity InspectInstallation(ResourceInstallationState state, bool verifyHash)
    {
        if (string.IsNullOrWhiteSpace(state.TargetPath) || !Directory.Exists(state.TargetPath))
        {
            return new InstallationIntegrity(false, "The recorded resource directory is missing.");
        }

        try
        {
            RejectLink(state.TargetPath, "Resource installation");
            var expected = state.InstalledFiles.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
            if (expected.Count != state.InstalledFiles.Count)
            {
                return new InstallationIntegrity(false, "The recorded resource file inventory is invalid.");
            }

            var tree = ScanInstallationTree(state.TargetPath);
            if (tree.Files.Count != expected.Count)
            {
                return new InstallationIntegrity(false, "The resource directory contains missing or additional files.");
            }

            var expectedDirectories = ExpectedDirectories(state.InstalledFiles);
            if (!tree.Directories.SetEquals(expectedDirectories))
            {
                return new InstallationIntegrity(false, "The resource directory contains missing or additional directories.");
            }

            foreach (var filePath in tree.Files)
            {
                var relativePath = Path.GetRelativePath(state.TargetPath, filePath).Replace('\\', '/');
                if (!expected.TryGetValue(relativePath, out var expectedFile)
                    || new FileInfo(filePath).Length != expectedFile.Size
                    || (verifyHash && !FileMatches(filePath, expectedFile.Size, expectedFile.Sha256)))
                {
                    return new InstallationIntegrity(false, $"Resource file verification failed: {relativePath}.");
                }
            }

            return new InstallationIntegrity(true, "The installed resource matches its recorded file inventory.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new InstallationIntegrity(false, ex.Message);
        }
    }

    private static InstallationTree ScanInstallationTree(string root)
    {
        var files = new List<string>();
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                RejectLink(entry.FullName, "Resource content");
                if (entry is DirectoryInfo childDirectory)
                {
                    directories.Add(Path.GetRelativePath(root, childDirectory.FullName).Replace('\\', '/'));
                    pending.Push(childDirectory);
                }
                else if (entry is FileInfo file)
                {
                    files.Add(file.FullName);
                }
            }
        }

        return new InstallationTree(files, directories);
    }

    private static HashSet<string> ExpectedDirectories(IEnumerable<ResourceInstalledFileState> files)
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var parts = file.RelativePath.Split('/');
            for (var length = 1; length < parts.Length; length++)
            {
                directories.Add(string.Join('/', parts.Take(length)));
            }
        }

        return directories;
    }

    private static IArchive OpenArchive(string path)
    {
        try
        {
            return ArchiveFactory.OpenArchive(path);
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or NotSupportedException)
        {
            throw new InvalidDataException($"Unsupported or unreadable resource archive: {Path.GetFileName(path)}", ex);
        }
    }

    private static void ValidateCatalogEntry(ContentPackageCatalogEntry entry)
    {
        if (entry.Category is not ContentPackageCategory.Resource
            || entry.Distribution.Kind is not ContentPackageDistributionKind.GitHubResourceRelease)
        {
            throw new InvalidOperationException($"Catalog entry {entry.PackageId} is not an installable resource.");
        }
    }

    private static void ValidateRelease(ContentPackageCatalogEntry entry, ResourcePackageRelease release)
    {
        if (!release.Manifest.PackageId.Equals(entry.PackageId, StringComparison.Ordinal)
            || !release.Manifest.Repository.TrimEnd('/').Equals(entry.RepositoryUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
            || !release.Manifest.SupportedProducts.ToHashSet(StringComparer.Ordinal)
                .SetEquals(entry.SupportedProducts))
        {
            throw new InvalidDataException("Resource release does not match the trusted catalog entry.");
        }
    }

    private static string NormalizeArchivePath(string value)
    {
        var normalized = value.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.StartsWith("/", StringComparison.Ordinal)
            || normalized.Contains('\\')
            || normalized.Contains(':')
            || normalized.Contains('\0')
            || normalized.Split('/').Any(part => part is "" or "." or ".."))
        {
            throw new InvalidDataException($"Resource archive contains an unsafe member path: {value}.");
        }

        return normalized;
    }

    private static string TargetPath(string directory, string childName)
    {
        var path = Path.GetFullPath(Path.Combine(directory, childName));
        EnsureDirectChild(directory, path);
        return path;
    }

    private static string ResolveChildPath(string root, string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relative)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Resource path escapes the staging directory: {relativePath}.");
        }

        return path;
    }

    private static bool VersionsEqual(string left, string right) =>
        left.Trim().TrimStart('v', 'V').Equals(
            right.Trim().TrimStart('v', 'V'),
            StringComparison.OrdinalIgnoreCase);

    private static bool FileMatches(string path, long size, string sha256)
    {
        if (string.IsNullOrWhiteSpace(path)
            || !File.Exists(path)
            || new FileInfo(path).Length != size
            || !ResourcePackageManifestParser.IsSha256(sha256))
        {
            return false;
        }

        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return actual.Equals(sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureDirectChild(string directory, string childPath)
    {
        var parent = Path.GetDirectoryName(childPath) ?? "";
        if (!PathsEqual(directory, parent))
        {
            throw new InvalidDataException("Resource destination escapes the selected directory.");
        }
    }

    private static void EnsureFreeSpace(string directory, long requiredBytes)
    {
        var root = Path.GetPathRoot(directory);
        if (!string.IsNullOrWhiteSpace(root))
        {
            var drive = new DriveInfo(root);
            if (drive.IsReady && drive.AvailableFreeSpace < requiredBytes)
            {
                throw new IOException(
                    $"The selected destination needs at least {requiredBytes:N0} bytes of free space for download and extraction.");
            }
        }
    }

    private static void RejectLink(string path, string label)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return;
        }

        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidDataException($"{label} must not be a symbolic link or reparse point: {path}.");
        }
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static bool PathsEqual(string left, string right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Equals(
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    private sealed record InstallationIntegrity(bool Valid, string Status);

    private sealed record InstallationTree(
        IReadOnlyList<string> Files,
        HashSet<string> Directories);
}
