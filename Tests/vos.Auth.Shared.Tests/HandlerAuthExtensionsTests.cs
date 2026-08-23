using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using vos.Auth.Shared;
using vos.Tests.Shared;
using Xunit;

namespace vos.Auth.Shared.Tests;

public class HandlerAuthExtensionsTests
{
    private static readonly string VerificationKey = new MyceliumSigner().VerificationKey;

    private static WebApplicationBuilder NewBuilder() =>
        WebApplication.CreateBuilder(Array.Empty<string>());

    [Fact]
    public void AddMyceliumTokenAuth_ReturnsSameBuilderForFluentChaining()
    {
        var builder = NewBuilder();

        var result = builder.AddMyceliumTokenAuth(VerificationKey, "VillageOS", "VillageOSClients");

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void AddMyceliumTokenAuth_ThrowsWhenIssuerOrAudienceMissing()
    {
        var missingIssuer = () => NewBuilder().AddMyceliumTokenAuth(VerificationKey, issuer: null, audience: "aud");
        missingIssuer.Should().Throw<ArgumentException>();

        var missingAudience = () => NewBuilder().AddMyceliumTokenAuth(VerificationKey, issuer: "iss", audience: "");
        missingAudience.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddMyceliumTokenAuth_AppliesCustomIssuerAndAudience()
    {
        var builder = NewBuilder();

        builder.AddMyceliumTokenAuth(VerificationKey, issuer: "custom-iss", audience: "custom-aud");

        var p = ResolveBearerValidationParameters(builder);
        p.ValidIssuer.Should().Be("custom-iss");
        p.ValidAudience.Should().Be("custom-aud");
    }

    /// <summary>The key a handler is given checks a signature and cannot make one, and the checker
    /// names the one algorithm it accepts rather than honouring whatever the token claims.</summary>
    [Fact]
    public void AddMyceliumTokenAuth_ChecksWithAPublicKeyAndOneNamedAlgorithm()
    {
        var builder = NewBuilder();

        builder.AddMyceliumTokenAuth(VerificationKey, "VillageOS", "VillageOSClients");

        var p = ResolveBearerValidationParameters(builder);
        p.IssuerSigningKey.Should().BeOfType<ECDsaSecurityKey>();
        p.ValidAlgorithms.Should().Equal(SecurityAlgorithms.EcdsaSha256);
    }

    [Fact]
    public async Task AddMyceliumTokenAuth_RegistersBearerScheme()
    {
        var builder = NewBuilder();

        builder.AddMyceliumTokenAuth(VerificationKey, "VillageOS", "VillageOSClients");

        using var app = builder.Build();
        var provider = app.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        var scheme = await provider.GetSchemeAsync(JwtBearerDefaults.AuthenticationScheme);
        scheme.Should().NotBeNull();
        scheme!.Name.Should().Be("Bearer");
    }

    [Fact]
    public void AddMyceliumTokenAuth_RegistersAuthorizationServices()
    {
        var builder = NewBuilder();

        builder.AddMyceliumTokenAuth(VerificationKey, "VillageOS", "VillageOSClients");

        using var app = builder.Build();
        app.Services.GetService<Microsoft.AspNetCore.Authorization.IAuthorizationService>()
            .Should().NotBeNull("AddAuthorization() registers IAuthorizationService");
    }

    [Fact]
    public void AddMyceliumTokenAuth_RefusesAKeyThatIsNotBase64()
    {
        var act = () => NewBuilder().AddMyceliumTokenAuth("not!valid!base64!", "VillageOS", "VillageOSClients");

        act.Should().Throw<FormatException>();
    }

    /// <summary>At startup, not on the first call it fails to answer.</summary>
    [Fact]
    public void AddMyceliumTokenAuth_RefusesBase64ThatIsNotAPublicKey()
    {
        var act = () => NewBuilder().AddMyceliumTokenAuth(
            Convert.ToBase64String("not a public key"u8.ToArray()), "VillageOS", "VillageOSClients");

        act.Should().Throw<CryptographicException>();
    }

    private static TokenValidationParameters ResolveBearerValidationParameters(WebApplicationBuilder builder)
    {
        var app = builder.Build();
        var monitor = app.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>();
        var options = monitor.Get(JwtBearerDefaults.AuthenticationScheme);
        return options.TokenValidationParameters;
    }
}
