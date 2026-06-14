using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace vos.Auth.Shared;

/// <summary>
/// Extension methods for adding mycelium-signed JWT auth to relationship service minimal API apps.
/// Handlers receive --signingKey (base64-encoded) from Mycelium at startup.
/// </summary>
public static class HandlerAuthExtensions
{
    /// <summary>
    /// Add JWT Bearer authentication to a handler, validating tokens signed by Mycelium.
    /// The signing key is base64-encoded (wrapping the UTF-8 bytes of Mycelium's key).
    /// </summary>
    public static WebApplicationBuilder AddMyceliumTokenAuth(
        this WebApplicationBuilder builder,
        string base64SigningKey,
        string issuer = "VillageOS",
        string audience = "VosClients")
    {
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
