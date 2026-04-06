using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace vos.Auth.Shared;

public static class ServiceTokenValidator
{
    public static TokenValidationParameters CreateParameters(string key, string issuer, string audience)
    {
        return new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    }
}
