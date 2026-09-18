using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.Platform;
using LevelUp.NavTableUpdater.Core.Tools;
using LevelUp.NavTableUpdater.Core.Transactions;

namespace LevelUp.NavTableUpdater.Core.Aircraft;

public sealed record HardwareConfigTarget(string DisplayName, string FileName, string Path, bool Exists);

/// <summary>Copies the shared Zibo/LevelUp hardware configuration, never unrelated preferences.</summary>
public sealed class HardwareConfigTransferOperation(string backupRoot, Func<bool>? isXPlaneRunning = null)
{
    private readonly Func<bool> _isRunning = isXPlaneRunning ?? XPlaneProcessDetector.IsXPlaneRunning;
    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal)
    {
        ["zibo-737-800-2k"] = "b738x_hw.cfg", ["zibo-737-800-4k"] = "b738x_hw.cfg",
        ["levelup-737-600"] = "737_60NG_hw.cfg", ["levelup-737-700"] = "737_70NG_hw.cfg",
        ["levelup-737-800"] = "737_80NG_hw.cfg", ["levelup-737-900"] = "737_90NG_hw.cfg",
        ["levelup-737-900er"] = "737_9ENG_hw.cfg"
    };

    public static IReadOnlyList<HardwareConfigTarget> Discover(string root)
    {
        RequireRoot(root);
        return new AircraftViewAnalyzer().Analyze(Path.Combine(root, "Aircraft")).Variants
            .Where(v => Names.ContainsKey(v.AircraftId) && v.IdentityStatus != "Unreadable")
            .Select(v => Names[v.AircraftId]).Distinct(StringComparer.Ordinal)
            .Select(name => new HardwareConfigTarget(name == "b738x_hw.cfg" ? "Zibo 737-800 (2K / 4K)" : "LevelUp " + name.Replace("_hw.cfg", ""),
                name, TargetPath(root, name), File.Exists(TargetPath(root, name))))
            .OrderBy(v => v.DisplayName).ToArray();
    }

    public MaintenanceOperationResult Copy(string root, string sourceName, IReadOnlyList<string> targetNames)
    {
        try
        {
            RequireRoot(root);
            RequireStopped();
            var available = Discover(root).Select(v => v.FileName).ToHashSet(StringComparer.Ordinal);
            if (!available.Contains(sourceName) || targetNames.Count == 0 || targetNames.Any(n => !available.Contains(n) || n == sourceName))
                throw new InvalidOperationException("Select an existing source and other detected Zibo/LevelUp variants in this X-Plane installation.");
            var source = Read(TargetPath(root, sourceName)) ?? throw new InvalidOperationException("Source hardware configuration is missing. Save it in the aircraft first.");
            Validate(source);
            var changes = targetNames.Distinct(StringComparer.Ordinal)
                .Select(n => new Snapshot(n, Read(TargetPath(root, n)), source)).Where(s => !Same(s.Before, s.After)).ToArray();
            if (changes.Length == 0) return MaintenanceOperationResult.NoChange("Selected hardware configurations already match the source.", []);
            return Execute(root, changes, "Hardware configurations copied");
        }
        catch (Exception ex) when (Expected(ex)) { return MaintenanceOperationResult.Blocked(ex.Message, [ex.Message]); }
    }

    public MaintenanceOperationResult Restore(string root)
    {
        try
        {
            RequireRoot(root);
            RequireStopped();
            var journalPath = JournalPath(root);
            if (!File.Exists(journalPath)) return MaintenanceOperationResult.NoChange("No hardware copy is available to restore for this installation.", []);
            var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllBytes(journalPath))
                ?? throw new InvalidOperationException("Hardware backup is unreadable.");
            if (journal.Root != Path.GetFullPath(root) || journal.Files is null || journal.Files.Length == 0 || journal.Files.Any(s => s is null) || journal.Files.Select(s => s.Name).Distinct().Count() != journal.Files.Length)
                throw new InvalidOperationException("Hardware backup does not match this installation.");
            var changes = new List<Snapshot>();
            foreach (var snapshot in journal.Files)
            {
                var current = Read(TargetPath(root, snapshot.Name));
                // A partially completed operation can contain either the original or copied bytes.
                if (Same(current, snapshot.Before)) continue;
                if (!Same(current, snapshot.After)) throw new InvalidOperationException($"Hardware configuration changed since the copy: {snapshot.Name}. Restore blocked to preserve your changes.");
                changes.Add(new Snapshot(snapshot.Name, current, snapshot.Before));
            }
            if (changes.Count == 0) return MaintenanceOperationResult.NoChange("Original hardware configurations are already restored.", []);
            return Execute(root, changes.ToArray(), "Original hardware configurations restored", restoring: true);
        }
        catch (Exception ex) when (Expected(ex)) { return MaintenanceOperationResult.Blocked(ex.Message, [ex.Message]); }
    }

    // The complete preimage is persisted before any mutation, including absence of new files.
    private MaintenanceOperationResult Execute(string root, Snapshot[] changes, string message, bool restoring = false)
    {
        var journal = new Journal(Path.GetFullPath(root), changes);
        var json = JsonSerializer.SerializeToUtf8Bytes(journal);
        var latest = JournalPath(root);
        var archive = Path.Combine(Path.GetDirectoryName(latest)!, $"{DateTime.UtcNow:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}.json");
        var previousJournal = File.Exists(latest) ? File.ReadAllBytes(latest) : null;
        AtomicFileMutation.Write(archive, json);
        if (!restoring) AtomicFileMutation.Write(latest, json);
        var attempted = new List<Snapshot>();
        try
        {
            RequireStopped();
            foreach (var change in changes)
            {
                var path = TargetPath(root, change.Name);
                if (!Same(Read(path), change.Before)) throw new InvalidOperationException($"Hardware configuration changed during preparation: {change.Name}.");
                attempted.Add(change);
                Write(path, change.After);
            }
        }
        catch (Exception failure)
        {
            var rollbackErrors = new List<string>();
            foreach (var change in attempted.AsEnumerable().Reverse())
                try
                {
                    var path = TargetPath(root, change.Name);
                    if (!Same(Read(path), change.Before)) Write(path, change.Before);
                }
                catch (Exception ex) { rollbackErrors.Add(ex.Message); }
            if (!restoring && rollbackErrors.Count == 0)
                try { Write(latest, previousJournal); }
                catch (Exception ex) { rollbackErrors.Add(ex.Message); }
            throw new IOException($"{failure.Message} " + (rollbackErrors.Count == 0 ? "Changes rolled back." : $"Rollback incomplete: {string.Join("; ", rollbackErrors)}. Recovery backup: {archive}"), failure);
        }
        return MaintenanceOperationResult.Applied($"{message}: {changes.Length}. Backup: {archive}", [archive],
            changes.Select(s => $"{message}: {s.Name}").Append($"Backup: {archive}").ToArray());
    }

    private string JournalPath(string root)
    {
        var normalized = Path.GetFullPath(root);
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()) normalized = normalized.ToUpperInvariant();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return ContentPatchPathSafety.ResolveTarget(backupRoot, $"hardware-config/{key}/latest.json", "Hardware backup");
    }

    private static string TargetPath(string root, string name)
    {
        if (!Names.ContainsValue(name)) throw new InvalidOperationException("Unsupported hardware configuration name.");
        var path = ContentPatchPathSafety.ResolveTarget(root, $"Output/preferences/{name}", "Hardware configuration");
        // File.Exists/Directory.Exists are false for dangling links.
        foreach (var entry in new[] { Path.Combine(root, "Output"), Path.Combine(root, "Output", "preferences"), path })
            if (new FileInfo(entry).LinkTarget is not null || new DirectoryInfo(entry).LinkTarget is not null)
                throw new InvalidOperationException("Hardware configuration traverses a symbolic link.");
        return path;
    }
    private static byte[]? Read(string path)
    {
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 128 * 1024) throw new InvalidOperationException("Hardware configuration exceeds the supported size.");
        return File.ReadAllBytes(path);
    }
    private static void Validate(byte[] bytes)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF').Split('\n'))
        {
            var text = line.Trim();
            if (text.Length == 0 || text.StartsWith("***", StringComparison.Ordinal)) continue;
            var pair = text.Split('=', 2);
            if (pair.Length != 2 || !keys.Add(pair[0].Trim()) || !double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
                throw new InvalidOperationException("Source is not a valid Zibo/LevelUp hardware configuration.");
        }
        if (!keys.Contains("TOE BRAKE AXIS") || !keys.Contains("THROTTLE NOISE") || !keys.Contains("PITCH 0 ZONE"))
            throw new InvalidOperationException("Source lacks the expected hardware configuration fields.");
    }
    private void RequireStopped() { if (_isRunning()) throw new InvalidOperationException("Close X-Plane before copying or restoring hardware configurations."); }
    private static void RequireRoot(string root) { if (!XPlaneInstallationLocator.LooksLikeXPlaneRoot(root)) throw new InvalidOperationException("Select an aircraft inside a valid X-Plane installation."); }
    private static bool Same(byte[]? a, byte[]? b) => a is null ? b is null : b is not null && a.AsSpan().SequenceEqual(b);
    private static void Write(string path, byte[]? bytes) { if (bytes is null) File.Delete(path); else AtomicFileMutation.Write(path, bytes); }
    private static bool Expected(Exception ex) => ex is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or DecoderFallbackException;
    public sealed record Snapshot(string Name, byte[]? Before, byte[]? After);
    public sealed record Journal(string Root, Snapshot[] Files);
}
