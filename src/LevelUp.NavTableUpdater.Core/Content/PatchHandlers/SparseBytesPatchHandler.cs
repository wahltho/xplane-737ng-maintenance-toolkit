using System.Security.Cryptography;
using System.Text.Json;

namespace LevelUp.NavTableUpdater.Core.Content.PatchHandlers;

public sealed class SparseBytesPatchHandler : IContentPatchHandler
{
    public string Operation => "sparse-bytes-v1";

    public bool SupportsStructuralSourceValidation => false;

    public byte[] Apply(byte[] source, JsonElement payload)
    {
        if (!payload.RequiredString("format").Equals(Operation, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Unsupported sparse-byte patch format.");
        }

        var sourceHash = Sha256(source);
        if (source.LongLength != payload.RequiredInt64("sourceSize")
            || !sourceHash.Equals(payload.RequiredString("sourceSha256"), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Sparse byte patch source does not match.");
        }

        var result = source.ToArray();
        foreach (var hunk in payload.RequiredArray("hunks").EnumerateArray())
        {
            var offset = hunk.RequiredInt32("offset");
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(hunk.RequiredString("data"));
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException("Sparse byte patch contains invalid Base64 data.", ex);
            }

            if (offset < 0 || offset > result.Length - bytes.Length)
            {
                throw new InvalidOperationException("Sparse byte patch hunk is outside the source file.");
            }

            bytes.CopyTo(result, offset);
        }

        if (result.LongLength != payload.RequiredInt64("resultSize")
            || !Sha256(result).Equals(payload.RequiredString("resultSha256"), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Sparse byte patch result failed its integrity check.");
        }

        return result;
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
