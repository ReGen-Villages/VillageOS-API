using System.Text;
using System.Text.Json;

namespace vos.Service.Shared;

/// <summary>
/// A bearer a daemon holds on behalf of one project, and when it stops being usable.
///
/// A daemon shared by several projects is never told which model a call is for — the only place that is
/// reliably stated is the token Mycelium signed, so the model is read back out of it. The signature is
/// not checked here: Mycelium validated the token on the way in, and this is the daemon reading its own
/// credential to decide which project it belongs to and when to replace it.
/// </summary>
public sealed record ModelScopedBearer(string Token, Guid ModelId, DateTimeOffset ExpiresAt)
{
    private const string ModelIdClaim = "vos:model_id";
    private const string ExpiryClaim = "exp";

    /// <summary>Null when the token cannot be read, names no model, or states no expiry. A token with no
    /// stated end would otherwise be held forever and fail only once something depended on it.</summary>
    public static ModelScopedBearer? Read(string? token)
    {
        var payload = Payload(token);
        if (payload is null) return null;

        if (!payload.Value.TryGetProperty(ModelIdClaim, out var modelId) ||
            !Guid.TryParse(modelId.GetString(), out var model))
            return null;

        if (!payload.Value.TryGetProperty(ExpiryClaim, out var expiry) ||
            !expiry.TryGetInt64(out var secondsSinceEpoch))
            return null;

        return new ModelScopedBearer(token!, model, DateTimeOffset.FromUnixTimeSeconds(secondsSinceEpoch));
    }

    public bool IsDueForReplacement(DateTimeOffset now, TimeSpan leadTime) => ExpiresAt - now <= leadTime;

    private static JsonElement? Payload(string? token)
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
