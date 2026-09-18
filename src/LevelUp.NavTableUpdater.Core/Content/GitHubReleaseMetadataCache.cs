using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LevelUp.NavTableUpdater.Core.Content;

public sealed partial class GitHubContentPatchReleaseSource
{
    // Serializing metadata requests also coalesces concurrent checks for the same release.
    private static readonly SemaphoreSlim MetadataGate = new(1, 1);
    private static readonly TimeSpan MetadataLifetime = TimeSpan.FromMinutes(10);

    private async Task<byte[]> GetReleaseMetadataAsync(HttpRequestMessage request, CancellationToken token)
    {
        await MetadataGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var directory = Path.Combine(_cacheRoot, "release-metadata");
            CreateSafeCacheDirectory(directory);
            var url = request.RequestUri!.AbsoluteUri;
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
            var cachePath = Path.Combine(directory, key + ".json");
            var cooldownPath = Path.Combine(directory, "github-cooldown.json");
            RejectLink(cachePath, "Release metadata cache");
            RejectLink(cooldownPath, "GitHub cooldown cache");
            var now = _timeProvider.GetUtcNow();
            var cached = ReadMetadataCache<ReleaseMetadataCache>(cachePath);
            if (cached is not null && cached.Url == url && cached.Bytes is { Length: <= MaximumMetadataBytes }
                && cached.CheckedAt <= now && now - cached.CheckedAt < MetadataLifetime)
                return cached.Bytes;

            var cooldown = ReadMetadataCache<GitHubCooldown>(cooldownPath);
            if (cooldown is not null && cooldown.Until > now)
                throw RateLimitError(cooldown.Until);

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests
                || (response.StatusCode == HttpStatusCode.Forbidden
                    && (response.ReasonPhrase?.Contains("rate limit", StringComparison.OrdinalIgnoreCase) == true
                        || response.Headers.RetryAfter is not null
                        || response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining) && remaining.Contains("0"))))
            {
                var until = now.AddMinutes(1);
                if (response.Headers.TryGetValues("x-ratelimit-reset", out var reset)
                    && long.TryParse(reset.FirstOrDefault(), out var seconds)
                    && seconds > now.ToUnixTimeSeconds() && seconds <= DateTimeOffset.MaxValue.ToUnixTimeSeconds())
                    until = DateTimeOffset.FromUnixTimeSeconds(seconds);
                var retry = response.Headers.RetryAfter;
                var retryAt = retry?.Date ?? (retry?.Delta is { } delay ? now.Add(delay) : (DateTimeOffset?)null);
                if (retryAt > until) until = retryAt.Value;
                WriteMetadataCache(cooldownPath, new GitHubCooldown(until));
                throw RateLimitError(until);
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaximumMetadataBytes)
                throw new InvalidDataException("GitHub release metadata exceeds the size limit.");
            await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                if (output.Length + read > MaximumMetadataBytes)
                    throw new InvalidDataException("GitHub release metadata exceeds the size limit.");
                output.Write(buffer, 0, read);
            }
            var bytes = output.ToArray();
            // Cached metadata still passes all existing release/asset validation on every use.
            try { using var document = JsonDocument.Parse(bytes); }
            catch (JsonException ex) { throw new InvalidDataException("GitHub release metadata is invalid JSON.", ex); }
            WriteMetadataCache(cachePath, new ReleaseMetadataCache(url, now, bytes));
            return bytes;
        }
        finally { MetadataGate.Release(); }
    }

    private static HttpRequestException RateLimitError(DateTimeOffset until) => new(
        $"GitHub temporarily limited release checks. Retry after {until.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}. "
        + "Required patches are still pending. Do not reinstall the aircraft or delete Toolkit configuration/backups.");

    private static T? ReadMetadataCache<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > MaximumMetadataBytes * 2) return null;
            return JsonSerializer.Deserialize<T>(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private static void WriteMetadataCache<T>(string path, T value)
    {
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(value));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed record ReleaseMetadataCache(string Url, DateTimeOffset CheckedAt, byte[] Bytes);
    private sealed record GitHubCooldown(DateTimeOffset Until);
}
