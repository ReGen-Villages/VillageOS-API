using System.Net;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Shared;
using Xunit;
using TributaryClient = vos.Service.Tributary.Services.MyceliumClient;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// The keep lane meeting the real store for the first time. Until here the deposit contract was
// pinned by two stubs written together: the API suite's Mycelium stand-in answers 201 { hash }
// because the broker's assets route does, and the same hands wrote both — so either repo could
// drift and leave both suites green while every keep in production answered 502. These cases hold
// the handshake itself: the ticket is the hash the engine computed over the bytes, under the
// agreed field name; a repeat deposit is the same ticket; the bytes come back exactly, under the
// exact type they went in with; and the ticket's other half — resolving the subject and landing
// the observation — rides the routes the shipped client actually calls.
public class TributaryKeepsAnAssetTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public TributaryKeepsAnAssetTests(TheEngine engine) => _engine = engine;

    private TributaryClient Tributary() => new(
        _engine.ClientFactory, NullLogger<TributaryClient>.Instance, TheEngine.Url, _engine.AdminToken);

    // Every case deposits its own bytes, so one case's dedup can never answer another's deposit.
    private static byte[] OwnBytes() => Guid.NewGuid().ToByteArray();

    private static string HashOf(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    [Fact]
    public async Task A_deposit_answers_the_name_the_engine_computed_over_the_bytes()
    {
        var bytes = OwnBytes();

        var ticket = await Tributary().DepositAssetAsync(bytes, "image/png");

        // One assertion, three pins: the field is spelled the way the client reads it — a renamed
        // field comes back null — the name carries its algorithm, and the hex is the content's own.
        ticket.Should().Be($"sha256:{HashOf(bytes)}");
    }

    [Fact]
    public async Task Depositing_the_same_bytes_again_is_the_same_ticket()
    {
        var bytes = OwnBytes();
        var client = Tributary();

        var first = await client.DepositAssetAsync(bytes, "image/png");
        var second = await client.DepositAssetAsync(bytes, "image/png");

        first.Should().NotBeNull();
        second.Should().Be(first, "the name is the content, and whether the store answered 201 or "
            + "200 must stay invisible to the client");
    }

    [Fact]
    public async Task The_bytes_come_back_exactly_under_the_type_they_went_in_with()
    {
        var bytes = OwnBytes();
        var ticket = await Tributary().DepositAssetAsync(bytes, "image/png");

        var served = await _engine.Admin.GetAsync($"/api/assets/{ticket}");

        served.StatusCode.Should().Be(HttpStatusCode.OK);
        served.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        (await served.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);
    }

    [Fact]
    public async Task A_type_the_media_map_does_not_know_is_served_back_exactly()
    {
        var bytes = OwnBytes();
        var ticket = await Tributary().DepositAssetAsync(bytes, "application/x-lerc");

        var served = await _engine.Admin.GetAsync($"/api/assets/{ticket}");

        served.StatusCode.Should().Be(HttpStatusCode.OK);
        served.Content.Headers.ContentType!.MediaType.Should().Be("application/x-lerc");
        (await served.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);
    }

    [Fact]
    public async Task Tributary_finds_the_subject_a_keep_names()
    {
        var name = $"KeepSubject_{Guid.NewGuid():N}";
        var declared = await _engine.DeclareAsync(name);

        var found = await Tributary().FindThingByNameAsync(name);

        found.Should().NotBeNull();
        found!.Value.Id.Should().Be(declared);
    }

    // The worked shape of the feature: a blank Sampled string property, and a ticket landing on it
    // as an ordinary observation with no time of its own, left for the model clock.
    [Fact]
    public async Task A_ticket_rides_the_observation_route_the_keep_lane_writes_through()
    {
        var client = Tributary();
        var subject = await _engine.DeclareAsync($"KeepSubject_{Guid.NewGuid():N}",
            properties: new Dictionary<string, object?> { ["surfaceMap"] = "" });
        (await client.SetPropertyModeAsync(subject, "surfaceMap", "Sampled")).Should().BeTrue();

        var ticket = await client.DepositAssetAsync(OwnBytes(), "image/png");
        var landed = await client.SubmitObservationsAsync(
            subject, new[] { new ObservationSample("surfaceMap", ticket) });

        landed.Should().BeTrue();
    }
}
