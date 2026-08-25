using vos.Auth.Shared;

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
    /// <summary>Null when the token cannot be read, names no model, or states no expiry. A token with no
    /// stated end would otherwise be held forever and fail only once something depended on it.</summary>
    public static ModelScopedBearer? Read(string? token)
    {
        var payload = JwtPayload.Read(token);
        if (payload is null) return null;

        if (!payload.Value.TryGetProperty(VosClaims.ModelId, out var modelId) ||
            !Guid.TryParse(modelId.GetString(), out var model))
            return null;

        if (JwtPayload.ExpiryOf(payload.Value) is not { } expiresAt) return null;

        return new ModelScopedBearer(token!, model, expiresAt);
    }

    public bool IsDueForReplacement(DateTimeOffset now, TimeSpan leadTime) => ExpiresAt - now <= leadTime;
}
