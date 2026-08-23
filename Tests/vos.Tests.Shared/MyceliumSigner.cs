using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace vos.Tests.Shared;

/// <summary>Stands in for Mycelium in a handler's tests: holds a key pair on the P-256 curve, hands
/// out the public half exactly as a daemon receives it, and signs the tokens a handler is meant to
/// accept — and the ones it must refuse.</summary>
public sealed class MyceliumSigner
{
    private readonly ECDsa _pair = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>The public half, encoded as the <c>VerificationKey</c> setting a daemon is launched with.</summary>
    public string VerificationKey => Convert.ToBase64String(_pair.ExportSubjectPublicKeyInfo());

    public string Token(string issuer, string audience, params Claim[] claims) =>
        TokenExpiring(issuer, audience, DateTime.UtcNow.AddMinutes(5), claims);

    public string TokenExpiring(
        string issuer, string audience, DateTime expiresUtc, params Claim[] claims) =>
        Write(
            new SigningCredentials(new ECDsaSecurityKey(_pair), SecurityAlgorithms.EcdsaSha256),
            issuer, audience, expiresUtc, claims);

    /// <summary>What someone who reads the verification key off a daemon can produce: a token signed
    /// with that key used as a plain shared secret. A handler that accepted it would have handed an
    /// attacker a signing key.</summary>
    public string TokenForgedFromTheVerificationKey(
        string issuer, string audience, params Claim[] claims) =>
        Write(
            new SigningCredentials(
                new SymmetricSecurityKey(Convert.FromBase64String(VerificationKey)),
                SecurityAlgorithms.HmacSha256),
            issuer, audience, DateTime.UtcNow.AddMinutes(5), claims);

    private static string Write(
        SigningCredentials credentials, string issuer, string audience,
        DateTime expiresUtc, Claim[] claims) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer, audience, claims,
            notBefore: expiresUtc.AddMinutes(-10),
            expires: expiresUtc,
            signingCredentials: credentials));
}
