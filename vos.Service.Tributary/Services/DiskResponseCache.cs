using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace vos.Service.Tributary.Services;

public readonly record struct CachedResponse(byte[] Bytes, string ContentType);

// The on-disk store behind the DiskCache kind (#5918). Entries are real files with real
// extensions — a cached tile opens in any viewer — under one directory per endpoint. The
// extension is derived from the media type by a small generic map (media types, not sources);
// a type the map does not know falls back to `.bin` plus a `.meta.json` sidecar carrying the
// exact Content-Type, so nothing is ever unservable. Freshness is judged from the file's
// write time against the caller's TTL; an expired entry simply misses and the next write
// replaces it, so no sweeper is needed.
public sealed class DiskResponseCache
{
    private readonly string _rootDirectory;
    private readonly TimeProvider _time;

    private static readonly Dictionary<string, string> ExtensionByMediaType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = "png",
        ["image/jpeg"] = "jpg",
        ["image/webp"] = "webp",
        ["image/tiff"] = "tif",
        ["application/vnd.mapbox-vector-tile"] = "pbf",
        ["application/json"] = "json",
    };

    private static readonly Dictionary<string, string> MediaTypeByExtension =
        ExtensionByMediaType.ToDictionary(entry => entry.Value, entry => entry.Key, StringComparer.OrdinalIgnoreCase);

    public DiskResponseCache(string rootDirectory, TimeProvider time)
    {
        _rootDirectory = rootDirectory;
        _time = time;
    }

    // The Accept header is part of the identity: the same address can legitimately answer with
    // different formats (#5913), and serving one negotiation's bytes to another would be a lie.
    public static string CacheKey(Uri address, string? acceptHeader) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(address.AbsoluteUri + "\n" + (acceptHeader ?? string.Empty)))).ToLowerInvariant();

    public CachedResponse? TryRead(string endpointName, string key, TimeSpan timeToLive)
    {
        var directory = DirectoryFor(endpointName);
        if (!Directory.Exists(directory)) return null;

        foreach (var path in Directory.EnumerateFiles(directory, key + ".*"))
        {
            if (path.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase)) continue;
            if (_time.GetUtcNow().UtcDateTime - File.GetLastWriteTimeUtc(path) > timeToLive) return null;

            var extension = Path.GetExtension(path).TrimStart('.');
            var contentType = MediaTypeByExtension.TryGetValue(extension, out var known)
                ? known
                : SidecarContentType(directory, key) ?? "application/octet-stream";
            return new CachedResponse(File.ReadAllBytes(path), contentType);
        }
        return null;
    }

    public void Write(string endpointName, string key, byte[] bytes, string contentType)
    {
        var directory = DirectoryFor(endpointName);
        Directory.CreateDirectory(directory);

        // A rewrite under a different media type must not leave a stale twin under the old extension.
        foreach (var stale in Directory.EnumerateFiles(directory, key + ".*").ToList())
            File.Delete(stale);

        if (ExtensionByMediaType.TryGetValue(BareMediaType(contentType), out var extension))
        {
            WriteStamped(Path.Combine(directory, key + "." + extension), bytes);
            return;
        }

        WriteStamped(Path.Combine(directory, key + ".bin"), bytes);
        WriteStamped(Path.Combine(directory, key + ".meta.json"),
            JsonSerializer.SerializeToUtf8Bytes(new { contentType }));
    }

    // The write time is the store's only clock, so it is stamped from the injected TimeProvider —
    // that is what lets expiry be tested by moving time instead of sleeping.
    private void WriteStamped(string path, byte[] bytes)
    {
        File.WriteAllBytes(path, bytes);
        File.SetLastWriteTimeUtc(path, _time.GetUtcNow().UtcDateTime);
    }

    private string? SidecarContentType(string directory, string key)
    {
        var sidecar = Path.Combine(directory, key + ".meta.json");
        if (!File.Exists(sidecar)) return null;
        using var document = JsonDocument.Parse(File.ReadAllBytes(sidecar));
        return document.RootElement.TryGetProperty("contentType", out var value) ? value.GetString() : null;
    }

    private static string BareMediaType(string contentType)
    {
        var separator = contentType.IndexOf(';');
        return (separator >= 0 ? contentType[..separator] : contentType).Trim();
    }

    // The endpoint name comes from the model, so it cannot be trusted as a path: anything that is
    // not a letter, digit, dash or underscore collapses to '_', which keeps the entry one directory
    // under the root whatever the name contains.
    private string DirectoryFor(string endpointName)
    {
        var sanitized = new StringBuilder(endpointName.Length);
        foreach (var character in endpointName)
            sanitized.Append(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' ? character : '_');
        return Path.Combine(_rootDirectory, sanitized.ToString());
    }
}
