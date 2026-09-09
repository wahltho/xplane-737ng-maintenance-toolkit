using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Manifest;
using LevelUp.NavTableUpdater.Core.Transactions;

namespace LevelUp.NavTableUpdater.Core.Content.PatchHandlers;

public sealed class VnavManifestPatchHandler : IContentPatchHandler
{
    public string Operation => "vnav-manifest-v1";
    public bool SupportsStructuralSourceValidation => true;
    public byte[] Apply(byte[] source, JsonElement payload)
    {
        if (payload.GetProperty("format").GetString() != Operation) throw new InvalidOperationException("Invalid VNAV operation.");
        var manifest = ManifestParser.ParsePipeManifest(payload.GetProperty("manifestText").GetString()!);
        var raw = JsonSerializer.Deserialize<Dictionary<string, byte[]>>(payload.GetProperty("payloads"))!;
        var files = raw.ToDictionary(p => p.Key, p => new PackagePayload(p.Key, p.Value, "catalog source"));
        foreach (var item in manifest.Payloads)
        {
            if (!files.TryGetValue(item.FileName, out var file)) throw new InvalidOperationException("Missing VNAV payload.");
            PackagePayloadValidator.ValidatePayload(item, file.Bytes, file.Source);
        }
        return VnavLuaPatchTransaction.PrepareApply(source, manifest, files).Bytes;
    }
}
