using System.Security.Cryptography;
using System.Text.Json;

namespace LevelUp.NavTableUpdater.Core.Content;

// Release evidence shipped with the application, not inferred from the user's
// installed version, writable state, package cache or a structurally valid file.
internal sealed class KnownAircraftBaselines(IReadOnlyList<KnownAircraftBaseline> entries)
{
    public static KnownAircraftBaselines BuiltIn { get; } = Load();

    public KnownAircraftBaseline? Match(string productId, string relativePath, byte[] bytes)
    {
        var candidates = entries.Where(e => e.ProductId == productId && e.Path == relativePath && e.Size == bytes.LongLength).ToArray();
        if (candidates.Length == 0) return null;
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        return candidates.FirstOrDefault(e => hash.Equals(e.Sha256, StringComparison.OrdinalIgnoreCase));
    }

    private static KnownAircraftBaselines Load()
    {
        using var stream = typeof(KnownAircraftBaselines).Assembly.GetManifestResourceStream(
            "LevelUp.NavTableUpdater.Core.Content.KnownAircraftBaselines.json")
            ?? throw new InvalidOperationException("Bundled aircraft baseline evidence is missing.");
        return new(JsonSerializer.Deserialize<KnownAircraftBaseline[]>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!);
    }
}

internal sealed record KnownAircraftBaseline(string ProductId, string Release, string Path,
    long Size, string Sha256, string SourceManifest, string SourceManifestSha256);
