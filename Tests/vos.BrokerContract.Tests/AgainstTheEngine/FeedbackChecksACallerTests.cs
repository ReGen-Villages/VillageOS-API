using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using vos.Auth.Shared;
using vos.Service.Feedback;
using vos.Tests.Shared;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// The feedback relay knows a caller is signed in only because the engine answers a signed-in route
// to their token. Its own suite answers that route with a stand-in, so this is where a change to what
// the route admits would show.
public class FeedbackChecksACallerTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public FeedbackChecksACallerTests(TheEngine engine) => _engine = engine;

    private PlatformCallers Callers() => new(_engine.ClientFactory, TheEngine.Url);

    [Fact]
    public async Task A_person_signed_in_to_the_engine_is_accepted_under_their_own_name()
    {
        var (verdict, holder) = await Callers().CheckAsync(_engine.AdminToken, CancellationToken.None);

        verdict.Should().Be(CallerVerdict.Accepted);
        holder!.Kind.Should().Be(TokenHolderKind.Person);
        holder.Name.Should().StartWith("host-admin-");
        holder.Role.Should().NotBeNullOrEmpty();
        holder.ModelName.Should().NotBeNullOrEmpty("the engine names the model the token was issued for");
    }

    [Fact]
    public async Task A_service_the_engine_dispatched_to_is_accepted_as_a_service()
    {
        var (verdict, holder) = await Callers().CheckAsync(_engine.ServiceTokenFor("kiosk-gateway"), CancellationToken.None);

        verdict.Should().Be(CallerVerdict.Accepted);
        holder!.Kind.Should().Be(TokenHolderKind.Service);
        holder.Name.Should().Be("service:kiosk-gateway");
    }

    [Fact]
    public async Task An_event_stream_token_is_refused()
    {
        var minted = await _engine.Admin.PostAsync("/api/auth/stream-token", null);
        minted.EnsureSuccessStatusCode();
        var streamToken = (await minted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;

        var (verdict, _) = await Callers().CheckAsync(streamToken, CancellationToken.None);

        verdict.Should().Be(CallerVerdict.Refused);
    }

    [Fact]
    public async Task A_token_signed_by_anyone_but_the_engine_is_refused()
    {
        var forged = new MyceliumSigner().Token("VillageOS", "VosClients",
            new Claim(ClaimTypes.Name, "ada"), new Claim(VosClaims.TokenType, "user"), new Claim(ClaimTypes.Role, "admin"));

        var (verdict, _) = await Callers().CheckAsync(forged, CancellationToken.None);

        verdict.Should().Be(CallerVerdict.Refused);
    }
}
