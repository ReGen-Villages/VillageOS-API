using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using vos.Auth.Shared;
using Xunit;

namespace vos.Auth.Shared.Tests;

public class HandlerAuthExtensionsTests
{
    private const string RawKey = "the-quick-brown-fox-jumps-over-the-lazy-dog-256";

    private static string Base64Of(string raw) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));

    private static WebApplicationBuilder NewBuilder() =>
        WebApplication.CreateBuilder(Array.Empty<string>());

    [Fact]
    public void AddMyceliumTokenAuth_ReturnsSameBuilderForFluentChaining()
    {
        var builder = NewBuilder();

        var result = builder.AddMyceliumTokenAuth(Base64Of(RawKey));

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void AddMyceliumTokenAuth_UsesDefaultIssuerAndAudience()
    {
        var builder = NewBuilder();

        builder.AddMyceliumTokenAuth(Base64Of(RawKey));

        var p = ResolveBearerValidationParameters(builder);
        p.ValidIssuer.Should().Be("VillageOS");
        p.ValidAudience.Should().Be("VosClients");
    }

    [Fact]
    public void AddMyceliumTokenAuth_AppliesCustomIssuerAndAudience()
    {
        var builder = NewBuilder();

        builder.AddMyceliumTokenAuth(Base64Of(RawKey), issuer: "custom-iss", audience: "custom-aud");

        var p = ResolveBearerValidationParameters(builder);
        p.ValidIssuer.Should().Be("custom-iss");
        p.ValidAudience.Should().Be("custom-aud");
    }

    [Fact]
    public void AddMyceliumTokenAuth_DecodesBase64SigningKeyAsUtf8Bytes()
    {
        var builder = NewBuilder();

        builder.AddMyceliumTokenAuth(Base64Of(RawKey));

        var p = ResolveBearerValidationParameters(builder);
        var sym = p.IssuerSigningKey.Should().BeOfType<SymmetricSecurityKey>().Subject;
        sym.Key.Should().Equal(Encoding.UTF8.GetBytes(RawKey));
    }

    [Fact]
    public async Task AddMyceliumTokenAuth_RegistersBearerScheme()
    {
        var builder = NewBuilder();

        builder.AddMyceliumTokenAuth(Base64Of(RawKey));

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

        builder.AddMyceliumTokenAuth(Base64Of(RawKey));

        using var app = builder.Build();
        app.Services.GetService<Microsoft.AspNetCore.Authorization.IAuthorizationService>()
            .Should().NotBeNull("AddAuthorization() registers IAuthorizationService");
    }

    [Fact]
    public void AddMyceliumTokenAuth_ThrowsFormatExceptionOnInvalidBase64()
    {
        var builder = NewBuilder();

        var act = () => builder.AddMyceliumTokenAuth("not!valid!base64!");

        act.Should().Throw<FormatException>();
    }

    private static TokenValidationParameters ResolveBearerValidationParameters(WebApplicationBuilder builder)
    {
        var app = builder.Build();
        var monitor = app.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>();
        var options = monitor.Get(JwtBearerDefaults.AuthenticationScheme);
        return options.TokenValidationParameters;
    }
}
