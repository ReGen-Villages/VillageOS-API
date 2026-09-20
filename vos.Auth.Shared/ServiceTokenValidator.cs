using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace vos.Auth.Shared;

// What a handler requires of an inbound Mycelium call. The key checks a signature and
// cannot produce one, so a handler holds exactly what its job needs and nothing more.
public static class ServiceTokenValidator
{
    // The one signature algorithm a handler accepts. Named rather than left to whatever the
    // token claims for itself: a checker that honours the token's own claim would accept a token
    // signed with the verification key used as a plain shared secret, which every handler holds.
    private const string Algorithm = SecurityAlgorithms.EcdsaSha256;

    // base64VerificationKey: Mycelium's public signing key, base64 of its
    // SubjectPublicKeyInfo encoding — the value Mycelium sets as VerificationKey.
    // audience: The name this handler is addressed by. A token naming any other
    // recipient is refused, including a signed-in person's browser token.
    public static TokenValidationParameters CreateParameters(
        string base64VerificationKey, string issuer, string audience) =>
        new()
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = VerificationKey(base64VerificationKey),
            ValidAlgorithms = [Algorithm],
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

    private static ECDsaSecurityKey VerificationKey(string base64VerificationKey)
    {
        var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(base64VerificationKey), out _);
        return new ECDsaSecurityKey(key);
    }
}
