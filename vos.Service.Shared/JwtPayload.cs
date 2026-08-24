using System.Text;
using System.Text.Json;

namespace vos.Service.Shared;

// Reads a JWT's payload without checking the signature: the holder is reading a credential the
// broker signed for it, to learn what the credential says about itself.
internal static class JwtPayload
{
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
