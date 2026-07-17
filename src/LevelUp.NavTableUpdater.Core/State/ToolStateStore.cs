using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Aircraft;

namespace LevelUp.NavTableUpdater.Core.State;

public sealed class ToolStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public ToolStateStore(string rootPath)
    {
        RootPath = rootPath;
        StatePath = Path.Combine(rootPath, "state.json");
    }

    public string RootPath { get; }

    public string StatePath { get; }

    public static ToolStateStore CreateDefault()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".xplane-737ng-maintenance-toolkit");
        }

        return new ToolStateStore(Path.Combine(appData, "XPlane737NGMaintenanceToolkit"));
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
        var directory = Path.Combine(RootPath, "backups", SanitizePathPart(variant.AircraftId), stamp);
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
}
