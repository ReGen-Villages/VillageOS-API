using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace vos.Auth.Shared;

// Adds Mycelium-signed JWT auth to handler minimal-API apps. The key arrives as the VerificationKey
// setting from Mycelium at startup; it is the public half of Mycelium's signing pair.
public static class HandlerAuthExtensions
{
    public static WebApplicationBuilder AddMyceliumTokenAuth(
        this WebApplicationBuilder builder,
        string base64VerificationKey,
        string? issuer,
        string? audience)
    {
        // Fail fast: Mycelium passes both. A missing value used to fall back to a wrong default and
        // silently 401 every /handle call (Bug #5390). Now that each handler is addressed by its own
        // name, no shared default could be correct for anyone.
        if (string.IsNullOrEmpty(issuer))
            throw new ArgumentException("JWT issuer is required — Mycelium passes it via --issuer.", nameof(issuer));
        if (string.IsNullOrEmpty(audience))
            throw new ArgumentException("JWT audience is required — Mycelium passes it via --audience.", nameof(audience));

        // Read here rather than inside the options callback, which runs on the first request: a key
        // that cannot be read should stop the service starting, not answer one call with a 500.
        var parameters = ServiceTokenValidator.CreateParameters(base64VerificationKey, issuer, audience);

        builder.Services.AddAuthentication("Bearer")
            .AddJwtBearer("Bearer", options => options.TokenValidationParameters = parameters);

        builder.Services.AddAuthorization();

        return builder;
    }
}
