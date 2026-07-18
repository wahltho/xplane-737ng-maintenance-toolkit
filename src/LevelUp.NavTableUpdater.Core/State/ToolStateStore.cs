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
        var stamp = createdUtc.UtcDateTime.ToString("yyyyMMddTHHmmssfffZ");
        var directory = Path.Combine(BackupRootPath, SanitizePathPart(variant.AircraftId), stamp);
        return Path.Combine(directory, Path.GetFileName(sourcePath));
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

    private static string TargetKey(string acfPath)
    {
        var normalized = Path.GetFullPath(acfPath).ToUpperInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
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
