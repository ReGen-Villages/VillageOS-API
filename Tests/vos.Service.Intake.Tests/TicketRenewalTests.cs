using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using vos.Service.Intake;
using Xunit;

namespace vos.Service.Intake.Tests;

// Feature #6906 — a fresh ticket rides back on every act performed with a live one, so a person
// adjusting their submission and re-reading their findings stays in the exchange they already
// completed, while a ticket nobody uses still dies at the age it always did.
public class TicketRenewalTests
{
    private const string TicketHeader = SubmissionTicket.HeaderName;
    private const string AnasAddress = "ana.ferreira@example.pt";

    private static readonly string WillowBendDocument =
        ("{'submissionId':'" + WillowBend.SubmissionId + "',"
         + "'project':{'name':'Willow Bend Regeneration'},"
         + "'contact':{'name':'Ana Ferreira','emailAddress':'" + AnasAddress + "'},"
         + "'site':{'name':'Willow Bend'},"
         + "'parcel':{'boundarySource':'generated-from-stated-area','boundary':["
         + "{'latitude':39.4988,'longitude':-8.4168},{'latitude':39.5036,'longitude':-8.4168},"
         + "{'latitude':39.5036,'longitude':-8.4106}]}}").Replace('\'', '"');

    private static async Task<string> TicketFor(
        IntakeWebApplicationFactory factory, HttpClient client, string emailAddress)
    {
        (await client.PostAsJsonAsync("/submissions/verification", new { emailAddress }))
            .EnsureSuccessStatusCode();
        var exchanged = await client.PostAsJsonAsync(
            "/submissions/ticket",
            new { emailAddress, code = factory.Mailer.CodeSentTo(emailAddress) });
        exchanged.EnsureSuccessStatusCode();
        return (await exchanged.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ticket").GetString()!;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string ticket)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/submissions")
        {
            Content = new StringContent(WillowBendDocument, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(TicketHeader, ticket);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task An_accepted_submission_hands_back_a_ticket_good_after_the_presented_one_has_died()
    {
        await using var factory = new IntakeWebApplicationFactory
        {
            HandlerCallback = ModelStub.Seeded(ModelStub.Holds),
        };
        using var client = factory.CreateClient();
        var first = await TicketFor(factory, client, AnasAddress);

        // The renewed ticket's age runs from the act that handed it back, so the act has to happen
        // later than the exchange — nine minutes of slider-thinking here — for renewal to buy anything.
        factory.Clock.Advance(TimeSpan.FromMinutes(9));
        var accepted = await PostAsync(client, first);
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        var renewed = accepted.Headers.GetValues(TicketHeader).Single();

        factory.Clock.Advance(TimeSpan.FromMinutes(2));

        (await PostAsync(client, first)).StatusCode.Should()
            .Be(HttpStatusCode.Forbidden, "the ticket that was presented still dies at its own age");
        var carriedOn = await PostAsync(client, renewed);
        carriedOn.StatusCode.Should().Be(HttpStatusCode.OK,
            "the act it was handed back on keeps a live exchange live");
        carriedOn.Headers.Contains(TicketHeader).Should().BeTrue("every accepted act hands one back");
    }

    // A refused submission hands nothing back: the act it would have ridden on did not happen, and a
    // caller collecting tickets off refusals would have found a way to keep one alive for nothing.
    [Fact]
    public async Task A_refused_submission_hands_no_ticket_back()
    {
        await using var factory = new IntakeWebApplicationFactory
        {
            HandlerCallback = ModelStub.Seeded(ModelStub.Holds),
        };
        using var client = factory.CreateClient();
        var ticket = await TicketFor(factory, client, AnasAddress);

        var refused = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/submissions")
        {
            Content = new StringContent(
                WillowBendDocument.Replace("Willow Bend Regeneration", ""), Encoding.UTF8, "application/json"),
            Headers = { { TicketHeader, ticket } },
        });

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        refused.Headers.Contains(TicketHeader).Should().BeFalse();
    }

    [Fact]
    public async Task Read_findings_hand_back_a_ticket_the_way_an_accepted_submission_does()
    {
        var model = BrokerSnapshot.WithTwoSubmissions();
        await using var factory = new IntakeWebApplicationFactory
        {
            HandlerCallback = request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (request.Method == HttpMethod.Post && path == "/api/subscriptions")
                    return ModelStub.Json(Reading(request, model));
                if (path.EndsWith("/ranges", StringComparison.Ordinal))
                    return ModelStub.Json(
                        """{"ThingId":"x","ThingName":"Study","OwnRanges":[],"InheritedRanges":[]}""");
                return new HttpResponseMessage(HttpStatusCode.OK);
            },
        };
        using var client = factory.CreateClient();
        var address = BrokerSnapshot.AddressOn("Willow Bend");
        var ticket = await TicketFor(factory, client, address);

        factory.Clock.Advance(TimeSpan.FromMinutes(9));
        var read = await AskForFindingsAsync(client, address, ticket);
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        var renewed = read.Headers.GetValues(TicketHeader).Single();
        renewed.Should().NotBe(ticket);

        factory.Clock.Advance(TimeSpan.FromMinutes(2));
        (await AskForFindingsAsync(client, address, ticket)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await AskForFindingsAsync(client, address, renewed)).StatusCode
            .Should().Be(HttpStatusCode.OK, "a page re-read on the renewed ticket carries on");
    }

    private static async Task<HttpResponseMessage> AskForFindingsAsync(
        HttpClient client, string address, string ticket)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/submissions/findings")
        {
            Content = JsonContent.Create(
                new { submissionId = WillowBend.SubmissionId, emailAddress = address }),
            Headers = { { TicketHeader, ticket } },
        };
        return await client.SendAsync(request);
    }

    private static string Reading(HttpRequestMessage request, BrokerSnapshot model)
    {
        var asked = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        using var selector = JsonDocument.Parse(asked);
        var rootedAt = selector.RootElement.TryGetProperty("traverse", out var traverse)
                       && traverse.ValueKind == JsonValueKind.Array
                       && traverse.GetArrayLength() > 0
            ? Guid.Parse(selector.RootElement.GetProperty("ids")[0].GetString()!)
            : (Guid?)null;

        return rootedAt is { } site ? model.OpenedReaching(site) : model.Opened();
    }
}
