using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LevelUp.NavTableUpdater.Core.Content.PatchHandlers;

public sealed class PngRgbaRegionPatchHandler : IContentPatchHandler
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = BuildCrcTable();

    public string Operation => "png-rgba-region-v1";

    public bool SupportsStructuralSourceValidation => false;

    public byte[] Apply(byte[] source, JsonElement payload)
    {
        if (!payload.RequiredString("format").Equals(Operation, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Unsupported PNG patch format.");
        }

        if (!Sha256(source).Equals(payload.RequiredString("sourceSha256"), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("PNG patch source does not match.");
        }

        var decoded = Decode(source);
        var dimensions = payload.RequiredArray("dimensions").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        if (dimensions.Length != 2 || decoded.Width != dimensions[0] || decoded.Height != dimensions[1])
        {
            throw new InvalidOperationException("PNG dimensions do not match.");
        }

        if (!Sha256(decoded.Pixels).Equals(payload.RequiredString("sourcePixelSha256"), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("PNG source pixels do not match.");
        }

        var regionDefinition = payload.RequiredArray("region").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        if (regionDefinition.Length != 4)
        {
            throw new InvalidOperationException("PNG patch region must contain x, y, width and height.");
        }

        var (x, y, regionWidth, regionHeight) = (regionDefinition[0], regionDefinition[1], regionDefinition[2], regionDefinition[3]);
        if (x < 0 || y < 0 || regionWidth <= 0 || regionHeight <= 0
            || x > decoded.Width - regionWidth || y > decoded.Height - regionHeight)
        {
            throw new InvalidOperationException("PNG patch region is outside the image.");
        }

        byte[] region;
        try
        {
            var compressed = Convert.FromBase64String(payload.RequiredString("rgbaZlibBase64"));
            region = DecompressZlib(compressed);
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException)
        {
            throw new InvalidOperationException("PNG patch region payload is invalid.", ex);
        }

        if (region.Length != checked(regionWidth * regionHeight * 4))
        {
            throw new InvalidOperationException("PNG patch region payload has an unexpected size.");
        }

        var result = decoded.Pixels.ToArray();
        var sourceStride = decoded.Width * 4;
        var regionStride = regionWidth * 4;
        for (var row = 0; row < regionHeight; row++)
        {
            region.AsSpan(row * regionStride, regionStride)
                .CopyTo(result.AsSpan((y + row) * sourceStride + x * 4, regionStride));
        }

        if (!Sha256(result).Equals(payload.RequiredString("resultPixelSha256"), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("PNG patch result pixels failed their integrity check.");
        }

        return Encode(decoded.Width, decoded.Height, result, decoded.Chunks);
    }

    private static DecodedPng Decode(byte[] bytes)
    {
        if (!bytes.AsSpan().StartsWith(Signature))
        {
            throw new InvalidOperationException("Patch target is not a PNG file.");
        }

        var chunks = new List<PngChunk>();
        var position = Signature.Length;
        while (position < bytes.Length)
        {
            if (position > bytes.Length - 12)
            {
                throw new InvalidOperationException("Truncated PNG chunk.");
            }

            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(position, 4)));
            if (length < 0 || position > bytes.Length - (12 + length))
            {
                throw new InvalidOperationException("Truncated PNG payload.");
            }

            var typeBytes = bytes.AsSpan(position + 4, 4).ToArray();
            var data = bytes.AsSpan(position + 8, length).ToArray();
            var declaredCrc = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(position + 8 + length, 4));
            if (Crc32(typeBytes, data) != declaredCrc)
            {
                throw new InvalidOperationException($"PNG CRC mismatch in {Encoding.ASCII.GetString(typeBytes)}.");
            }

            var type = Encoding.ASCII.GetString(typeBytes);
            chunks.Add(new PngChunk(type, data));
            position += 12 + length;
            if (type == "IEND")
            {
                break;
            }
        }

        var header = chunks.SingleOrDefault(chunk => chunk.Type == "IHDR")
            ?? throw new InvalidOperationException("PNG has no IHDR chunk.");
        if (header.Data.Length != 13)
        {
            throw new InvalidOperationException("PNG IHDR has an invalid size.");
        }

        var width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(header.Data.AsSpan(0, 4)));
        var height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(header.Data.AsSpan(4, 4)));
        if (header.Data[8] != 8 || header.Data[9] != 6 || header.Data[10] != 0 || header.Data[11] != 0 || header.Data[12] != 0)
        {
            throw new InvalidOperationException("Only non-interlaced 8-bit RGBA PNG files are supported.");
        }

        var compressed = chunks.Where(chunk => chunk.Type == "IDAT").SelectMany(chunk => chunk.Data).ToArray();
        if (compressed.Length == 0)
        {
            throw new InvalidOperationException("PNG has no IDAT data.");
        }

        var raw = DecompressZlib(compressed);
        var stride = checked(width * 4);
        if (raw.Length != checked(height * (stride + 1)))
        {
            throw new InvalidOperationException("Unexpected decompressed PNG size.");
        }

        var pixels = new byte[checked(width * height * 4)];
        var previous = new byte[stride];
        var cursor = 0;
        for (var rowIndex = 0; rowIndex < height; rowIndex++)
        {
            var filter = raw[cursor++];
            if (filter > 4)
            {
                throw new InvalidOperationException($"Unsupported PNG filter {filter}.");
            }

            var row = raw.AsSpan(cursor, stride).ToArray();
            cursor += stride;
            for (var index = 0; index < stride; index++)
            {
                var left = index >= 4 ? row[index - 4] : 0;
                var up = previous[index];
                var upperLeft = index >= 4 ? previous[index - 4] : 0;
                row[index] = filter switch
                {
                    0 => row[index],
                    1 => unchecked((byte)(row[index] + left)),
                    2 => unchecked((byte)(row[index] + up)),
                    3 => unchecked((byte)(row[index] + ((left + up) / 2))),
                    4 => unchecked((byte)(row[index] + Paeth(left, up, upperLeft))),
                    _ => throw new InvalidOperationException($"Unsupported PNG filter {filter}.")
                };
            }

            row.CopyTo(pixels, rowIndex * stride);
            previous = row;
        }

        return new DecodedPng(width, height, pixels, chunks);
    }

    private static byte[] Encode(int width, int height, byte[] pixels, IReadOnlyList<PngChunk> chunks)
    {
        var stride = checked(width * 4);
        if (pixels.Length != checked(height * stride))
        {
            throw new InvalidOperationException("PNG encoder input size mismatch.");
        }

        var raw = new byte[checked(height * (stride + 1))];
        for (var row = 0; row < height; row++)
        {
            var destination = row * (stride + 1);
            raw[destination] = 0;
            pixels.AsSpan(row * stride, stride).CopyTo(raw.AsSpan(destination + 1, stride));
        }

        var compressed = CompressZlib(raw);
        using var output = new MemoryStream();
        output.Write(Signature);
        var wroteIdat = false;
        foreach (var chunk in chunks)
        {
            if (chunk.Type == "IDAT")
            {
                if (wroteIdat)
                {
                    continue;
                }

                WriteChunk(output, chunk.Type, compressed);
                wroteIdat = true;
            }
            else
            {
                WriteChunk(output, chunk.Type, chunk.Data);
            }
        }

        if (!wroteIdat)
        {
            throw new InvalidOperationException("PNG has no IDAT chunk.");
        }

        return output.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, checked((uint)data.Length));
        output.Write(number);
        output.Write(typeBytes);
        output.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc32(typeBytes, data));
        output.Write(number);
    }

    private static byte[] DecompressZlib(byte[] bytes)
    {
        using var input = new MemoryStream(bytes, writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] CompressZlib(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(bytes);
        }

        return output.ToArray();
    }

    private static int Paeth(int left, int up, int upperLeft)
    {
        var estimate = left + up - upperLeft;
        var leftDistance = Math.Abs(estimate - left);
        var upDistance = Math.Abs(estimate - up);
        var upperLeftDistance = Math.Abs(estimate - upperLeft);
        return leftDistance <= upDistance && leftDistance <= upperLeftDistance
            ? left
            : upDistance <= upperLeftDistance ? up : upperLeft;
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in type)
        {
            crc = CrcTable[(int)((crc ^ value) & 0xFF)] ^ (crc >> 8);
        }

        foreach (var value in data)
        {
            crc = CrcTable[(int)((crc ^ value) & 0xFF)] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var index = 0; index < table.Length; index++)
        {
            var value = (uint)index;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            }

            table[index] = value;
        }

        return table;
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record PngChunk(string Type, byte[] Data);

    private sealed record DecodedPng(int Width, int Height, byte[] Pixels, IReadOnlyList<PngChunk> Chunks);
}
