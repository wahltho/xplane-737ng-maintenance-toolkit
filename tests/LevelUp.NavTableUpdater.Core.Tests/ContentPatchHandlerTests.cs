using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Content.PatchHandlers;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class ContentPatchHandlerTests
{
    [Fact]
    public void Registry_WhenOperationIsUnknown_RejectsIt()
    {
        var registry = ContentPatchHandlerRegistry.CreateBuiltIn();

        var error = Assert.Throws<InvalidOperationException>(() => registry.GetRequired("execute-script"));

        Assert.Contains("Unsupported", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExactText_AppliesEveryUniqueBlockAndPreservesUnrelatedLines()
    {
        var source = Encoding.UTF8.GetBytes("header\r\nold selector\r\nmiddle\r\nold switch\r\nfooter\r\n");
        using var payload = TwoBlockTextPayload();

        var result = Encoding.UTF8.GetString(new ExactTextReplacementsPatchHandler().Apply(source, payload.RootElement));

        Assert.Equal("header\r\nnew selector\r\nmiddle\r\nnew switch\r\nfooter\r\n", result);
    }

    [Fact]
    public void ExactText_WhenEveryInstalledBlockIsUnique_IsIdempotent()
    {
        var source = Encoding.UTF8.GetBytes("header\nnew selector\nmiddle\nnew switch\nfooter\n");
        using var payload = TwoBlockTextPayload();

        var result = new ExactTextReplacementsPatchHandler().Apply(source, payload.RootElement);

        Assert.Equal(source, result);
    }

    [Theory]
    [InlineData("old selector\n")]
    [InlineData("old selector\nold selector\nold switch\n")]
    public void ExactText_WhenARequiredBlockIsMissingOrAmbiguous_RejectsIt(string sourceText)
    {
        using var payload = TwoBlockTextPayload();

        var error = Assert.Throws<InvalidOperationException>(() =>
            new ExactTextReplacementsPatchHandler().Apply(Encoding.UTF8.GetBytes(sourceText), payload.RootElement));

        Assert.Contains("expected exactly one", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExactText_WhenOldBlockOccursInsideInstalledBlock_RejectsNonIdempotentPayload()
    {
        using var payload = JsonDocument.Parse("""
            {
              "format": "exact-text-replacements-v1",
              "replacements": [
                {
                  "name": "overlap",
                  "oldLines": ["old"],
                  "newLines": ["old", "additional"]
                }
              ]
            }
            """);

        var error = Assert.Throws<InvalidOperationException>(() =>
            new ExactTextReplacementsPatchHandler().Apply(Encoding.UTF8.GetBytes("old\n"), payload.RootElement));

        Assert.Contains("cannot be classified idempotently", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SparseBytes_AppliesBoundedHunkAndVerifiesResult()
    {
        var source = new byte[] { 1, 2, 3, 4 };
        var expected = new byte[] { 1, 9, 8, 4 };
        using var payload = JsonDocument.Parse($$"""
            {
              "format": "sparse-bytes-v1",
              "sourceSize": 4,
              "sourceSha256": "{{Sha256(source)}}",
              "resultSize": 4,
              "resultSha256": "{{Sha256(expected)}}",
              "hunks": [{ "offset": 1, "data": "{{Convert.ToBase64String([9, 8])}}" }]
            }
            """);

        var result = new SparseBytesPatchHandler().Apply(source, payload.RootElement);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Obj8Handler_AppliesDeclaredStructuralTransform()
    {
        var source = Encoding.UTF8.GetBytes("""
            A
            800
            OBJ
            POINT_COUNTS 1 0 3 3
            VT 0 0 0 0 0 1 0 0
            IDX 0
            IDX 0
            IDX 0
            TRIS 0 3
            """);
        var moved = new byte[12];
        using var payload = JsonDocument.Parse($$"""
            {
              "format": "obj8-fans-label-switch-v1",
              "source": {
                "vertexCount": 1,
                "indexCount": 3,
                "pointCountsLine": "POINT_COUNTS 1 0 3 3"
              },
              "moveIndexRangesToEnd": {
                "ranges": [[0, 3]],
                "sha256": "{{Sha256(moved)}}"
              },
              "replaceFinalDraw": {
                "old": "TRIS 0 3",
                "newLines": ["TRIS 0 6"]
              },
              "addedVertices": ["VT 1 0 0 0 0 1 1 0"],
              "addedIndices": [1, 1, 1],
              "result": {
                "vertexCount": 2,
                "indexCount": 6,
                "pointCountsLine": "POINT_COUNTS 2 0 6 6"
              }
            }
            """);

        var result = Encoding.UTF8.GetString(new Obj8FansLabelsPatchHandler().Apply(source, payload.RootElement));

        Assert.Contains("POINT_COUNTS 2 0 6 6", result, StringComparison.Ordinal);
        Assert.Contains("VT 1 0 0 0 0 1 1 0", result, StringComparison.Ordinal);
        Assert.Contains("IDX 1", result, StringComparison.Ordinal);
        Assert.EndsWith("TRIS 0 6", result.TrimEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void PngHandler_ReplacesDeclaredRgbaRegion()
    {
        var sourcePixel = new byte[] { 1, 2, 3, 4 };
        var resultPixel = new byte[] { 5, 6, 7, 8 };
        var source = CreateOnePixelPng(sourcePixel);
        var compressedRegion = CompressZlib(resultPixel);
        using var payload = JsonDocument.Parse($$"""
            {
              "format": "png-rgba-region-v1",
              "sourceSha256": "{{Sha256(source)}}",
              "sourcePixelSha256": "{{Sha256(sourcePixel)}}",
              "resultPixelSha256": "{{Sha256(resultPixel)}}",
              "dimensions": [1, 1],
              "region": [0, 0, 1, 1],
              "rgbaZlibBase64": "{{Convert.ToBase64String(compressedRegion)}}"
            }
            """);

        var result = new PngRgbaRegionPatchHandler().Apply(source, payload.RootElement);

        Assert.NotEqual(Sha256(source), Sha256(result));
        Assert.True(result.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47 }));
    }

    private static JsonDocument TwoBlockTextPayload() =>
        JsonDocument.Parse("""
            {
              "format": "exact-text-replacements-v1",
              "replacements": [
                {
                  "name": "selector",
                  "oldLines": ["old selector"],
                  "newLines": ["new selector"]
                },
                {
                  "name": "switch",
                  "oldLines": ["old switch"],
                  "newLines": ["new switch"]
                }
              ]
            }
            """);

    private static byte[] CreateOnePixelPng(byte[] rgba)
    {
        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), 1);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(output, "IHDR", header);
        WriteChunk(output, "IDAT", CompressZlib([0, .. rgba]));
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, checked((uint)data.Length));
        output.Write(number);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc32(typeBytes.Concat(data)));
        output.Write(number);
    }

    private static byte[] CompressZlib(byte[] data)
    {
        using var output = new MemoryStream();
        using (var stream = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            stream.Write(data);
        }

        return output.ToArray();
    }

    private static uint Crc32(IEnumerable<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
