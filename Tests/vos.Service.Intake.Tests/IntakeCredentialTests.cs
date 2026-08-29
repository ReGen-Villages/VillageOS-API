using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using vos.Tests.Shared;
using Xunit;
using static vos.Service.Intake.Tests.ModelStub;

namespace vos.Service.Intake.Tests;

/// <summary>
/// What this service authenticates with. Nothing launches it, so no short-lived token is ever minted
/// for it and an API key is the only credential it can hold — which is why the key reaching the clients
/// it calls the broker with is worth a test of its own.
///
/// These go through the host rather than building a client by hand. A credential that never leaves
/// configuration is invisible to a test that constructs the client itself and passes the key in.
/// </summary>
public class IntakeCredentialTests
{
    private const string TicketHeader = "X-Submission-Ticket";
    private const string KeyHeader = "X-API-Key";
    private const string TheKey = "vos_sk_a-key-the-broker-would-accept";

    /// <summary>The address the submission names, and therefore the one it verifies.</summary>
    private const string AnasAddress = "ana.ferreira@example.pt";

    private static readonly string WillowBendDocument =
        ("{'submissionId':'" + WillowBend.SubmissionId + "',"
         + "'project':{'name':'Willow Bend Regeneration'},"
         + "'contact':{'name':'Ana Ferreira','emailAddress':'" + AnasAddress + "'},"
         + "'site':{'name':'Willow Bend','population':320},"
         + "'parcel':{'boundarySource':'generated-from-stated-area','boundary':["
         + "{'latitude':39.4988,'longitude':-8.4168},{'latitude':39.5036,'longitude':-8.4168},"
         + "{'latitude':39.5036,'longitude':-8.4106},{'latitude':39.4988,'longitude':-8.4106}]}}")
        .Replace('\'', '"');

    private static bool IsTokenExchange(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath == "/api/auth/token";

    [Fact]
    public async Task A_service_holding_only_a_key_reaches_a_broker_that_demands_one()
    {
        await using var factory = new IntakeWebApplicationFactory
        {
            Token = null,
            ApiKey = TheKey,
            HandlerCallback = BrokerDemandingTheKey,
        };
        using var client = factory.CreateClient();

        var response = await SubmitAsync(factory, client, WillowBendDocument);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the key is the only credential this service can hold, so a broker that demands one must be reachable");
    }

    [Fact]
    public async Task Every_exchange_the_service_makes_carries_the_key_it_was_given()
    {
        var exchangesWithoutTheKey = 0;
        await using var factory = new IntakeWebApplicationFactory
        {
            Token = null,
            ApiKey = TheKey,
        };
        factory.HandlerCallback = request =>
        {
            if (IsTokenExchange(request) && !CarriesTheKey(request)) exchangesWithoutTheKey++;
            return BrokerDemandingTheKey(request);
        };
        using var client = factory.CreateClient();

        await SubmitAsync(factory, client, WillowBendDocument);

        exchangesWithoutTheKey.Should().Be(0,
            "a client built without the key authenticates as nobody, and a submission is refused for a "
            + "reason whoever sent it cannot act on");
    }

    /// <summary>A broker that mints a token for a caller presenting the key and refuses one for a caller
    /// presenting none — which is what the deployed broker does. The token states an expiry, because a
    /// token that states none is deliberately never held and the exchange would read as a refusal.
    /// The instant is read off the real clock rather than the service's, which stands still: only the
    /// ticket is judged against the service's clock, and the credential is not.</summary>
    private static HttpResponseMessage BrokerDemandingTheKey(HttpRequestMessage request)
    {
        if (!IsTokenExchange(request)) return Holds(request);

        return CarriesTheKey(request)
            ? Json($$"""{"token":"{{TestTokens.For(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(30))}}"}""")
            : new HttpResponseMessage(HttpStatusCode.Unauthorized);
    }

    private static bool CarriesTheKey(HttpRequestMessage request) =>
        request.Headers.TryGetValues(KeyHeader, out var values)
        && values.Contains(TheKey);

    /// <summary>The whole exchange a submitter makes. Only the last of it reaches the broker — verifying
    /// an address is between this service and the person reading their mail — which is what leaves the
    /// token exchange below as the one thing these tests are about.</summary>
    private static async Task<HttpResponseMessage> SubmitAsync(
        IntakeWebApplicationFactory factory, HttpClient client, string document)
    {
        (await client.PostAsJsonAsync("/submissions/verification", new { emailAddress = AnasAddress }))
            .EnsureSuccessStatusCode();

        var ticket = await client.PostAsJsonAsync(
            "/submissions/ticket",
            new { emailAddress = AnasAddress, code = factory.Mailer.CodeSentTo(AnasAddress) });
        ticket.EnsureSuccessStatusCode();
        var value = (await ticket.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ticket").GetString()!;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/submissions")
        {
            Content = new StringContent(document, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(TicketHeader, value);
        return await client.SendAsync(request);
    }
}
