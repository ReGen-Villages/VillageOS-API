using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using vos.Service.Auth;
using vos.Service.Forage.Steps;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Forage.Tests;

// Forage is launched with a verification key in production; no other test starts it so.
public class AuthWireupTests
{
    private const string Issuer = "VillageOS";
    private const string Audience = "forage-handler";

    private static readonly MyceliumSigner Mycelium = new();

    private static ForageWebApplicationFactory Keyed() =>
        new() { VerificationKey = Mycelium.VerificationKey, Issuer = Issuer, Audience = Audience };

    [Fact]
    public async Task A_keyed_Forage_refuses_a_call_with_no_token()
    {
        await using var factory = Keyed();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { subjectId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_keyed_Forage_answers_a_call_signed_by_its_launch_key()
    {
        await using var factory = Keyed();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            Mycelium.Token(Issuer, Audience, new Claim(VosClaims.ModelId, Guid.NewGuid().ToString())));

        var response = await client.PostAsJsonAsync("/handle", new { subjectId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(ForageSteps.RelationshipCallRefusal,
            "the call got past authentication to Forage's own answer");
    }
}
