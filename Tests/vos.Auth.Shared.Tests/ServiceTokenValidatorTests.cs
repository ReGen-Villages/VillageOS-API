using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using vos.Auth.Shared;
using vos.Tests.Shared;
using Xunit;

namespace vos.Auth.Shared.Tests;

public class ServiceTokenValidatorTests
{
    private const string TestIssuer = "VillageOS";
    private const string ThisHandler = "catchment-handler";

    private readonly MyceliumSigner _mycelium = new();

    private TokenValidationParameters Parameters(string audience = ThisHandler) =>
        ServiceTokenValidator.CreateParameters(_mycelium.VerificationKey, TestIssuer, audience);

    private Action Validating(string token) =>
        () => new JwtSecurityTokenHandler().ValidateToken(token, Parameters(), out _);

    [Fact]
    public void CreateParameters_EnablesAllValidationFlags()
    {
        var p = Parameters();

        p.ValidateIssuerSigningKey.Should().BeTrue();
        p.ValidateIssuer.Should().BeTrue();
        p.ValidateAudience.Should().BeTrue();
        p.ValidateLifetime.Should().BeTrue();
    }

    [Fact]
    public void CreateParameters_BindsIssuerAndAudienceFromArguments()
    {
        var p = ServiceTokenValidator.CreateParameters(_mycelium.VerificationKey, "iss-x", "aud-y");

        p.ValidIssuer.Should().Be("iss-x");
        p.ValidAudience.Should().Be("aud-y");
    }

    [Fact]
    public void CreateParameters_TakesTheKeyAsAPublicEllipticCurveKey()
    {
        Parameters().IssuerSigningKey.Should().BeOfType<ECDsaSecurityKey>();
    }

    [Fact]
    public void CreateParameters_AcceptsOnlyTheEllipticCurveAlgorithm()
    {
        Parameters().ValidAlgorithms.Should().Equal(SecurityAlgorithms.EcdsaSha256);
    }

    [Fact]
    public void CreateParameters_ClockSkewIs30Seconds()
    {
        Parameters().ClockSkew.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ValidateToken_AcceptsACallMyceliumSignedForThisHandler()
    {
        Validating(_mycelium.Token(TestIssuer, ThisHandler, Subject)).Should().NotThrow();
    }

    /// <summary>The handler is addressed by name, so a token minted for a different handler — signed
    /// by the same Mycelium and otherwise perfectly good — buys nothing here.</summary>
    [Fact]
    public void ValidateToken_RejectsATokenAddressedToAnotherService()
    {
        Validating(_mycelium.Token(TestIssuer, "spring-handler", Subject))
            .Should().Throw<SecurityTokenInvalidAudienceException>();
    }

    /// <summary>A signed-in person's browser token names the recipient every user token names, which
    /// is not this handler's, so a handler no longer answers a call a person could make.</summary>
    [Fact]
    public void ValidateToken_RejectsAPersonsBrowserToken()
    {
        Validating(_mycelium.Token(TestIssuer, "VosClients", Subject))
            .Should().Throw<SecurityTokenInvalidAudienceException>();
    }

    /// <summary>The whole reason the key is a public half and the algorithm is pinned. Every handler
    /// holds this key; a checker that honoured the token's own claim would let any of them sign.</summary>
    [Fact]
    public void ValidateToken_RejectsATokenSignedWithTheVerificationKeyAsASharedSecret()
    {
        Validating(_mycelium.TokenForgedFromTheVerificationKey(TestIssuer, ThisHandler, Subject))
            .Should().Throw<SecurityTokenException>();
    }

    [Fact]
    public void ValidateToken_RejectsATokenCarryingNoSignatureAtAll()
    {
        var header = Segment(new { alg = "none", typ = "JWT" });
        var payload = Segment(new
        {
            iss = TestIssuer,
            aud = ThisHandler,
            exp = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()
        });

        Validating($"{header}.{payload}.").Should().Throw<SecurityTokenException>();
    }

    [Fact]
    public void ValidateToken_RejectsTokenWithWrongIssuer()
    {
        Validating(_mycelium.Token("other-issuer", ThisHandler, Subject))
            .Should().Throw<SecurityTokenInvalidIssuerException>();
    }

    [Fact]
    public void ValidateToken_RejectsTokenSignedByAnotherMycelium()
    {
        var stranger = new MyceliumSigner();

        Validating(stranger.Token(TestIssuer, ThisHandler, Subject))
            .Should().Throw<SecurityTokenException>();
    }

    [Fact]
    public void ValidateToken_RejectsExpiredTokenBeyondClockSkew()
    {
        var expired = _mycelium.TokenExpiring(
            TestIssuer, ThisHandler, DateTime.UtcNow.AddMinutes(-5), Subject);

        Validating(expired).Should().Throw<SecurityTokenExpiredException>();
    }

    [Fact]
    public void ValidateToken_AcceptsRecentlyExpiredTokenWithinClockSkew()
    {
        var justExpired = _mycelium.TokenExpiring(
            TestIssuer, ThisHandler, DateTime.UtcNow.AddSeconds(-10), Subject);

        Validating(justExpired).Should().NotThrow();
    }

    private static Claim Subject => new("sub", "test");

    private static string Segment(object value) =>
        Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)));
}
