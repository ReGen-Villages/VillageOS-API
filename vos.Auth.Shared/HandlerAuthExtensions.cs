using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace vos.Auth.Shared;

/// <summary>Adds Mycelium-signed JWT auth to handler minimal-API apps; the signing key arrives as --signingKey from Mycelium at startup.</summary>
public static class HandlerAuthExtensions
{
    /// <summary>base64SigningKey wraps the UTF-8 bytes of Mycelium's key (decode base64, then read as UTF-8 — not the raw key bytes).</summary>
    public static WebApplicationBuilder AddMyceliumTokenAuth(
        this WebApplicationBuilder builder,
        string base64SigningKey,
        string? issuer,
        string? audience)
    {
        // Fail fast: the broker passes issuer/audience (Bug #5390). A missing value previously fell back
        // to a wrong default and silently 401'd every /handle call — surface it at startup instead.
        if (string.IsNullOrEmpty(issuer))
            throw new ArgumentException("JWT issuer is required — the broker passes it via --issuer.", nameof(issuer));
        if (string.IsNullOrEmpty(audience))
            throw new ArgumentException("JWT audience is required — the broker passes it via --audience.", nameof(audience));

        var keyBytes = Convert.FromBase64String(base64SigningKey);
        var keyString = Encoding.UTF8.GetString(keyBytes);

        builder.Services.AddAuthentication("Bearer")
            .AddJwtBearer("Bearer", options =>
            {
                options.TokenValidationParameters = ServiceTokenValidator.CreateParameters(
                    keyString, issuer, audience);
            });

        builder.Services.AddAuthorization();

        return builder;
    }
}
