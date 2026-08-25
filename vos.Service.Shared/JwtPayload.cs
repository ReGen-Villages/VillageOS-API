using System.Text;
using System.Text.Json;

namespace vos.Service.Shared;

// Reads a JWT's payload without checking the signature: the holder is reading a credential the
// broker signed for it, to learn what the credential says about itself.
internal static class JwtPayload
{
    private const string ExpiryClaim = "exp";

    /// <summary>When the token says it stops being usable, or null when it says nothing readable. An
    /// expiry too far from now for a <see cref="DateTimeOffset"/> to hold reads as saying nothing, so a
    /// caller reading a credential never has to catch.</summary>
    internal static DateTimeOffset? ExpiryOf(JsonElement payload)
    {
        if (!payload.TryGetProperty(ExpiryClaim, out var expiry) || !expiry.TryGetInt64(out var secondsSinceEpoch))
            return null;

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(secondsSinceEpoch);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    internal static JsonElement? Read(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;

        var segments = token.Split('.');
        if (segments.Length != 3) return null;

        try
        {
            return JsonDocument.Parse(Encoding.UTF8.GetString(DecodeSegment(segments[1]))).RootElement.Clone();
        }
        catch (Exception exception) when (exception is FormatException or JsonException or DecoderFallbackException)
        {
            return null;
        }
    }

    private static byte[] DecodeSegment(string segment)
    {
        var unpadded = segment.Replace('-', '+').Replace('_', '/');
        var padding = (4 - unpadded.Length % 4) % 4;
        return Convert.FromBase64String(unpadded + new string('=', padding));
    }
}
