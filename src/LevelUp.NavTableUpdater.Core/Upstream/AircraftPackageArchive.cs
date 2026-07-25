using SharpCompress.Archives;

namespace LevelUp.NavTableUpdater.Core.Upstream;

internal sealed class AircraftPackageArchive : IDisposable
{
    private readonly IArchive _archive;

    private AircraftPackageArchive(IArchive archive)
    {
        _archive = archive;
        Entries = archive.Entries.Select(entry => new AircraftPackageArchiveEntry(entry)).ToArray();
    }

    public IReadOnlyList<AircraftPackageArchiveEntry> Entries { get; }

    public static AircraftPackageArchive Open(string path)
    {
        try
        {
            return new AircraftPackageArchive(ArchiveFactory.OpenArchive(path));
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or NotSupportedException)
        {
            throw new InvalidDataException($"Unsupported or unreadable aircraft package archive: {Path.GetFileName(path)}", ex);
        }
    }

    public void Dispose() => _archive.Dispose();
}

internal sealed class AircraftPackageArchiveEntry
{
    private readonly IArchiveEntry _entry;

    public AircraftPackageArchiveEntry(IArchiveEntry entry)
    {
        _entry = entry;
    }

    public string Path => _entry.Key ?? "";

    public long Size => _entry.Size;

    public bool IsDirectory => _entry.IsDirectory;

    public Stream Open() => _entry.OpenEntryStream();
}
