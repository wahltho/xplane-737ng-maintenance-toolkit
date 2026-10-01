using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Aircraft;

namespace LevelUp.NavTableUpdater.Core.State;

internal sealed class AircraftMoveJournal
{
    internal const long MaximumBytes = 64L * 1024 * 1024;
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Source { get; set; } = "";
    public string Destination { get; set; } = "";
    public string Stage { get; set; } = "";
    public string Hold { get; set; } = "";
    public string Phase { get; set; } = "Prepared";
    public List<AircraftMoveEntry> Entries { get; set; } = [];
    public byte[]? OriginalState { get; set; }
    public byte[]? OriginalSettings { get; set; }
    public byte[] NewState { get; set; } = [];
    public byte[] NewSettings { get; set; } = [];

    internal static void Write(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(flushToDisk: true); }
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    internal void Save(string path)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(this);
        if (bytes.LongLength > MaximumBytes)
            throw new InvalidDataException("The installation history is too large for a recoverable aircraft move.");
        Write(path, bytes);
    }
}
