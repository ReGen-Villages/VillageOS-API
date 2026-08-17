using System.Text;
using System.Text.Json;
using FluentAssertions;
using vos.Service.Shared;
using Xunit;

namespace vos.Service.Shared.Tests;

// A daemon shared by several projects has to know which model a bearer speaks for, and when that bearer
// stops being usable. It reads both off the token itself rather than being told, because the only place
// the model is reliably known is the token Mycelium signed.
public class ModelScopedBearerTests
{
    private static readonly Guid Model = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static string Jwt(object payload)
    {
        static string Segment(string json) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return $"{Segment("{\"alg\":\"HS256\"}")}.{Segment(JsonSerializer.Serialize(payload))}.signature";
    }

    private static long UnixSeconds(DateTimeOffset at) => at.ToUnixTimeSeconds();

    [Fact]
    public void Reads_the_model_and_the_expiry_a_token_carries()
    {
        var expires = DateTimeOffset.UtcNow.AddHours(24);

        var bearer = ModelScopedBearer.Read(Jwt(new Dictionary<string, object>
        {
            ["vos:model_id"] = Model.ToString(),
            ["exp"] = UnixSeconds(expires),
        }));

        bearer.Should().NotBeNull();
        bearer!.ModelId.Should().Be(Model);
        bearer.ExpiresAt.Should().BeCloseTo(expires, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Decodes_a_payload_whose_length_needed_base64_padding()
    {
        // Three of every four payload lengths need padding restored before decoding. Serialising a model
        // id of a different length walks onto one of them, so this is not the same case as the test above.
        var bearer = ModelScopedBearer.Read(Jwt(new Dictionary<string, object>
        {
            ["vos:model_id"] = Model.ToString(),
            ["exp"] = UnixSeconds(DateTimeOffset.UtcNow.AddHours(1)),
            ["vos:scope"] = "endpoint:x:*",
        }));

        bearer.Should().NotBeNull();
        bearer!.ModelId.Should().Be(Model);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("only.two")]
    [InlineData("a.!!!not-base64!!!.c")]
    public void An_unreadable_token_yields_nothing_rather_than_throwing(string token)
    {
        ModelScopedBearer.Read(token).Should().BeNull();
    }

    [Fact]
    public void A_token_naming_no_model_yields_nothing()
    {
        ModelScopedBearer.Read(Jwt(new Dictionary<string, object>
        {
            ["exp"] = UnixSeconds(DateTimeOffset.UtcNow.AddHours(1)),
        })).Should().BeNull();
    }

    [Fact]
    public void A_token_with_no_expiry_yields_nothing_so_it_is_never_trusted_indefinitely()
    {
        ModelScopedBearer.Read(Jwt(new Dictionary<string, object>
        {
            ["vos:model_id"] = Model.ToString(),
        })).Should().BeNull();
    }

    [Fact]
    public void A_bearer_is_due_for_replacement_once_it_is_inside_the_lead_time()
    {
        var now = DateTimeOffset.UtcNow;
        var bearer = new ModelScopedBearer("token", Model, now.AddMinutes(20));

        bearer.IsDueForReplacement(now, TimeSpan.FromMinutes(30)).Should().BeTrue();
    }

    [Fact]
    public void A_bearer_with_more_than_the_lead_time_left_is_left_alone()
    {
        var now = DateTimeOffset.UtcNow;
        var bearer = new ModelScopedBearer("token", Model, now.AddHours(6));

        bearer.IsDueForReplacement(now, TimeSpan.FromMinutes(30)).Should().BeFalse();
    }

    [Fact]
    public void An_expired_bearer_is_due_for_replacement()
    {
        var now = DateTimeOffset.UtcNow;
        var bearer = new ModelScopedBearer("token", Model, now.AddMinutes(-1));

        bearer.IsDueForReplacement(now, TimeSpan.FromMinutes(30)).Should().BeTrue();
    }
}
