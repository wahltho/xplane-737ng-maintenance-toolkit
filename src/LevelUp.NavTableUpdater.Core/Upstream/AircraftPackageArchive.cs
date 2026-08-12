using SharpCompress.Archives;
using SharpCompress.Common;

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
            ValidateArchiveSignature(path);
            return new AircraftPackageArchive(ArchiveFactory.OpenArchive(path));
        }
        catch (Exception ex) when (ex is ArchiveOperationException or InvalidOperationException or InvalidDataException or NotSupportedException)
        {
            throw new InvalidDataException($"Unsupported or unreadable aircraft package archive: {Path.GetFileName(path)}", ex);
        }
    }

    private static void ValidateArchiveSignature(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[512];
        var bytesRead = stream.Read(header);
        var bytes = header[..bytesRead];

        if (IsZip(bytes)
            || bytes.StartsWith(new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C })
            || bytes.StartsWith(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07 })
            || bytes.StartsWith(new byte[] { 0x1F, 0x8B })
            || IsTar(bytes))
        {
            return;
        }

        throw new InvalidDataException("File content does not have a supported aircraft archive signature.");
    }

    private static bool IsZip(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith(new byte[] { 0x50, 0x4B, 0x03, 0x04 })
        || bytes.StartsWith(new byte[] { 0x50, 0x4B, 0x05, 0x06 })
        || bytes.StartsWith(new byte[] { 0x50, 0x4B, 0x07, 0x08 });

    private static bool IsTar(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 262
        && bytes.Slice(257, 5).SequenceEqual("ustar"u8);

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
