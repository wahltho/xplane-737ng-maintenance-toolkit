using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Platform;

namespace LevelUp.NavTableUpdater.Core.State;

public sealed class ToolStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public ToolStateStore(string rootPath, string? backupRootPath = null)
    {
        RootPath = Path.GetFullPath(rootPath);
        StatePath = Path.Combine(RootPath, "state.json");
        BackupRootPath = NormalizeBackupRootPath(backupRootPath);
    }

    public string RootPath { get; }

    public string StatePath { get; }

    public string BackupRootPath { get; private set; }

    public static string DefaultRootPath => ToolkitPaths.RoamingAppDataRoot;

    public static string DefaultBackupRootPath => ToolkitPaths.DefaultBackupRootPath;

    public static ToolStateStore CreateDefault(string? backupRootPath = null)
    {
        return new ToolStateStore(DefaultRootPath, backupRootPath);
    }

    public void SetBackupRootPath(string backupRootPath)
    {
        BackupRootPath = NormalizeBackupRootPath(backupRootPath);
    }

    public ToolStateDocument Load()
    {
        if (!File.Exists(StatePath))
        {
            return new ToolStateDocument();
        }

        var json = File.ReadAllText(StatePath, Encoding.UTF8);
        return JsonSerializer.Deserialize<ToolStateDocument>(json, JsonOptions) ?? new ToolStateDocument();
    }

    public void Save(ToolStateDocument document)
    {
        Directory.CreateDirectory(RootPath);
        var tempPath = StatePath + $".tmp-{Guid.NewGuid():N}";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(document, JsonOptions), new UTF8Encoding(false));
        File.Move(tempPath, StatePath, overwrite: true);
    }

    public string CreateBackupPath(AircraftVariantViewAnalysis variant, string sourcePath, DateTimeOffset createdUtc)
    {
        return CreateBackupPath(variant.AircraftId, sourcePath, createdUtc, relativePath: null);
    }

    public string CreateBackupPath(
        AircraftVariantViewAnalysis variant,
        string sourcePath,
        DateTimeOffset createdUtc,
        string relativePath)
    {
        return CreateBackupPath(variant.AircraftId, sourcePath, createdUtc, relativePath);
    }

    public string CreateProductBackupPath(
        AircraftVariantViewAnalysis variant,
        string sourcePath,
        DateTimeOffset createdUtc,
        string? relativePath = null)
    {
        var product = AircraftProductIdentity.FromVariant(variant);
        return CreateBackupPath(product.BackupScopeId, sourcePath, createdUtc, relativePath);
    }

    public AircraftToolState? TryGetTarget(AircraftVariantViewAnalysis variant)
    {
        var document = Load();
        return document.Aircraft.GetValueOrDefault(TargetKey(variant.AcfPath));
    }

    public void UpdateTarget(AircraftVariantViewAnalysis variant, Action<AircraftToolState> update)
    {
        var document = Load();
        var key = TargetKey(variant.AcfPath);
        if (!document.Aircraft.TryGetValue(key, out var target))
        {
            target = new AircraftToolState();
            document.Aircraft[key] = target;
        }

        target.AircraftId = variant.AircraftId;
        target.AircraftFolder = Path.GetDirectoryName(variant.AcfPath) ?? "";
        target.AcfPath = variant.AcfPath;
        target.PrefsPath = variant.PrefsPath;
        target.LastObservedCgYFeet = variant.CurrentCgYFeet;
        target.LastObservedCgZFeet = variant.CurrentCgZFeet;
        update(target);
        Save(document);
    }

    public AircraftToolState? TryGetProductTarget(AircraftVariantViewAnalysis variant)
    {
        var document = Load();
        var key = ProductTargetKey(variant);
        if (document.Aircraft.TryGetValue(key, out var target))
        {
            return target;
        }

        return BuildMigratedProductTarget(document, variant);
    }

    public void UpdateProductTarget(AircraftVariantViewAnalysis variant, Action<AircraftToolState> update)
    {
        var document = Load();
        var key = ProductTargetKey(variant);
        if (!document.Aircraft.TryGetValue(key, out var target))
        {
            target = BuildMigratedProductTarget(document, variant) ?? new AircraftToolState();
            document.Aircraft[key] = target;
        }

        var product = AircraftProductIdentity.FromVariant(variant);
        target.AircraftId = product.BackupScopeId;
        target.AircraftFolder = ProductFolder(variant);
        target.AcfPath = "";
        target.PrefsPath = "";
        update(target);
        Save(document);
    }

    private static string TargetKey(string acfPath)
    {
        var normalized = Path.GetFullPath(acfPath).ToUpperInvariant();
        return HashKey(normalized);
    }

    private static string ProductTargetKey(AircraftVariantViewAnalysis variant)
    {
        var product = AircraftProductIdentity.FromVariant(variant);
        var normalized = $"PRODUCT|{product.Family}|{ProductFolder(variant)}".ToUpperInvariant();
        return HashKey(normalized);
    }

    private static string HashKey(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private string CreateBackupPath(
        string scopeId,
        string sourcePath,
        DateTimeOffset createdUtc,
        string? relativePath)
    {
        var stamp = createdUtc.UtcDateTime.ToString("yyyyMMddTHHmmssfffZ");
        var directory = Path.GetFullPath(Path.Combine(BackupRootPath, SanitizePathPart(scopeId), stamp));
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return Path.Combine(directory, Path.GetFileName(sourcePath));
        }

        var normalizedRelativePath = relativePath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalizedRelativePath))
        {
            throw new InvalidDataException($"Backup relative path must not be rooted: {relativePath}");
        }

        var parts = normalizedRelativePath.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(part => part is "." or ".."))
        {
            throw new InvalidDataException($"Backup relative path is invalid: {relativePath}");
        }

        var candidate = Path.GetFullPath(Path.Combine(directory, normalizedRelativePath));
        var relativeToDirectory = Path.GetRelativePath(directory, candidate);
        if (Path.IsPathRooted(relativeToDirectory)
            || relativeToDirectory.Equals("..", StringComparison.Ordinal)
            || relativeToDirectory.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Backup relative path escapes its generation folder: {relativePath}");
        }

        return candidate;
    }

    private static AircraftToolState? BuildMigratedProductTarget(
        ToolStateDocument document,
        AircraftVariantViewAnalysis variant)
    {
        var product = AircraftProductIdentity.FromVariant(variant);
        var productFolder = ProductFolder(variant);
        var legacyTargets = document.Aircraft.Values
            .Where(target => product.MatchesLegacyAircraftId(target.AircraftId)
                && PathsEqual(TargetAircraftFolder(target), productFolder))
            .ToArray();
        if (legacyTargets.Length == 0)
        {
            return null;
        }

        var migrated = new AircraftToolState
        {
            AircraftId = product.BackupScopeId,
            AircraftFolder = productFolder
        };

        var latestContent = legacyTargets
            .Where(target => target.LastContentOperationUtc.HasValue)
            .OrderByDescending(target => target.LastContentOperationUtc)
            .FirstOrDefault();
        if (latestContent is not null)
        {
            migrated.InstalledContentPackageId = latestContent.InstalledContentPackageId;
            migrated.InstalledContentPackageVersion = latestContent.InstalledContentPackageVersion;
            migrated.LastContentOperationUtc = latestContent.LastContentOperationUtc;
        }

        var latestAircraftUpdate = legacyTargets
            .Where(target => target.LastAircraftUpdateUtc.HasValue)
            .OrderByDescending(target => target.LastAircraftUpdateUtc)
            .FirstOrDefault();
        if (latestAircraftUpdate is not null)
        {
            migrated.InstalledAircraftUpdateFamily = latestAircraftUpdate.InstalledAircraftUpdateFamily;
            migrated.InstalledAircraftUpdateVersion = latestAircraftUpdate.InstalledAircraftUpdateVersion;
            migrated.LastAircraftUpdateMode = latestAircraftUpdate.LastAircraftUpdateMode;
            migrated.LastAircraftUpdateUtc = latestAircraftUpdate.LastAircraftUpdateUtc;
            migrated.LastAircraftUpdatePackages = [.. latestAircraftUpdate.LastAircraftUpdatePackages];
        }

        var productBackups = legacyTargets
            .SelectMany(target => target.Backups)
            .Where(record => IsProductBackupOperation(record.Operation))
            .GroupBy(BackupIdentity, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(record => record.CreatedUtc).First())
            .OrderBy(record => record.CreatedUtc)
            .ToList();
        migrated.Backups = productBackups;

        var latestOperation = legacyTargets
            .Where(target => IsProductBackupOperation(target.LastOperation ?? ""))
            .OrderByDescending(target => target.LastAircraftUpdateUtc ?? target.LastContentOperationUtc)
            .FirstOrDefault();
        migrated.LastOperation = latestOperation?.LastOperation;
        return migrated;
    }

    private static bool IsProductBackupOperation(string operation) =>
        operation.StartsWith("AircraftUpdate", StringComparison.Ordinal)
            || operation.StartsWith("VnavContent", StringComparison.Ordinal);

    private static string BackupIdentity(BackupRecord record) =>
        $"{record.Operation}\n{record.SourcePath}\n{record.BackupPath}\n{record.CreatedUtc:O}";

    private static string TargetAircraftFolder(AircraftToolState target)
    {
        if (!string.IsNullOrWhiteSpace(target.AircraftFolder))
        {
            return Path.GetFullPath(target.AircraftFolder);
        }

        return string.IsNullOrWhiteSpace(target.AcfPath)
            ? ""
            : Path.GetFullPath(Path.GetDirectoryName(target.AcfPath) ?? "");
    }

    private static string ProductFolder(AircraftVariantViewAnalysis variant) =>
        Path.GetFullPath(Path.GetDirectoryName(variant.AcfPath) ?? "");

    private static bool PathsEqual(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static string SanitizePathPart(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-');
        }

        return builder.ToString();
    }

    private static string NormalizeBackupRootPath(string? backupRootPath) =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(backupRootPath)
            ? DefaultBackupRootPath
            : backupRootPath);
}
