using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using vos.Auth.Shared;
using Xunit;

namespace vos.Auth.Shared.Tests;

public class ServiceTokenValidatorTests
{
    // HS256 requires at least 128 bits of key material; broker-supplied keys are always sized appropriately.
    private const string TestKey = "the-quick-brown-fox-jumps-over-the-lazy-dog-256";
    private const string TestIssuer = "VillageOS";
    private const string TestAudience = "VosClients";

    [Fact]
    public void CreateParameters_EnablesAllValidationFlags()
    {
        var p = ServiceTokenValidator.CreateParameters(TestKey, TestIssuer, TestAudience);

        p.ValidateIssuerSigningKey.Should().BeTrue();
        p.ValidateIssuer.Should().BeTrue();
        p.ValidateAudience.Should().BeTrue();
        p.ValidateLifetime.Should().BeTrue();
    }

    [Fact]
    public void CreateParameters_BindsIssuerAndAudienceFromArguments()
    {
        var p = ServiceTokenValidator.CreateParameters(TestKey, "iss-x", "aud-y");

        p.ValidIssuer.Should().Be("iss-x");
        p.ValidAudience.Should().Be("aud-y");
    }

    [Fact]
    public void CreateParameters_WrapsKeyAsSymmetricSecurityKeyOverUtf8Bytes()
    {
        var p = ServiceTokenValidator.CreateParameters(TestKey, TestIssuer, TestAudience);

        var sym = p.IssuerSigningKey.Should().BeOfType<SymmetricSecurityKey>().Subject;
        sym.Key.Should().Equal(Encoding.UTF8.GetBytes(TestKey));
    }

    [Fact]
    public void CreateParameters_ClockSkewIs30Seconds()
    {
        var p = ServiceTokenValidator.CreateParameters(TestKey, TestIssuer, TestAudience);

        p.ClockSkew.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ValidateToken_AcceptsTokenSignedWithSameKey()
    {
        var token = CreateJwt(TestKey, TestIssuer, TestAudience, DateTime.UtcNow.AddMinutes(5));
        var p = ServiceTokenValidator.CreateParameters(TestKey, TestIssuer, TestAudience);

        var act = () => new JwtSecurityTokenHandler().ValidateToken(token, p, out _);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateToken_RejectsTokenWithWrongIssuer()
    {
        var token = CreateJwt(TestKey, "other-issuer", TestAudience, DateTime.UtcNow.AddMinutes(5));
        var p = ServiceTokenValidator.CreateParameters(TestKey, TestIssuer, TestAudience);

        var act = () => new JwtSecurityTokenHandler().ValidateToken(token, p, out _);

        act.Should().Throw<SecurityTokenInvalidIssuerException>();
    }

    [Fact]
    public void ValidateToken_RejectsTokenWithWrongAudience()
    {
        var token = CreateJwt(TestKey, TestIssuer, "other-audience", DateTime.UtcNow.AddMinutes(5));
        var p = ServiceTokenValidator.CreateParameters(TestKey, TestIssuer, TestAudience);

        var act = () => new JwtSecurityTokenHandler().ValidateToken(token, p, out _);

        act.Should().Throw<SecurityTokenInvalidAudienceException>();
    }

    [Fact]
    public void ValidateToken_RejectsTokenSignedWithDifferentKey()
    {
        var token = CreateJwt("a-completely-different-key-256-bits-long-enough", TestIssuer, TestAudience, DateTime.UtcNow.AddMinutes(5));
        var p = ServiceTokenValidator.CreateParameters(TestKey, TestIssuer, TestAudience);

        var act = () => new JwtSecurityTokenHandler().ValidateToken(token, p, out _);

        act.Should().Throw<SecurityTokenSignatureKeyNotFoundException>();
    }

    [Fact]
    public void ValidateToken_RejectsExpiredTokenBeyondClockSkew()
    {
        // 5 minutes in the past — well past the 30s skew tolerance.
        var token = CreateJwt(TestKey, TestIssuer, TestAudience, DateTime.UtcNow.AddMinutes(-5));
        var p = ServiceTokenValidator.CreateParameters(TestKey, TestIssuer, TestAudience);

        var act = () => new JwtSecurityTokenHandler().ValidateToken(token, p, out _);

        act.Should().Throw<SecurityTokenExpiredException>();
    }

    [Fact]
    public void ValidateToken_AcceptsRecentlyExpiredTokenWithinClockSkew()
    {
        // Expired 10s ago — still inside the 30s skew window.
        var token = CreateJwt(TestKey, TestIssuer, TestAudience, DateTime.UtcNow.AddSeconds(-10));
        var p = ServiceTokenValidator.CreateParameters(TestKey, TestIssuer, TestAudience);

        var act = () => new JwtSecurityTokenHandler().ValidateToken(token, p, out _);

        act.Should().NotThrow();
    }

    private static string CreateJwt(string key, string issuer, string audience, DateTime expiresUtc)
    {
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: new[] { new Claim("sub", "test") },
            notBefore: expiresUtc.AddMinutes(-10),
            expires: expiresUtc,
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}
