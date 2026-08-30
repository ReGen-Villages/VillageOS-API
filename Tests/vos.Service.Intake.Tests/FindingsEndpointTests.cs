using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using vos.Service.Intake;
using vos.Service.Intake.Services;
using Xunit;

namespace vos.Service.Intake.Tests;

// The route that answers the person who submitted, and nobody else. It demands no credential for the same
// reason the submission route demands none — the page asking is a stranger's browser — so what stands in
// place of one is the pair it checks: the reference names a submission, the ticket proves a mailbox, and
// the submission has to name that mailbox.
public class FindingsEndpointTests
{
    private const string TicketHeader = "X-Submission-Ticket";

    private static readonly string ThisAddress = BrokerSnapshot.AddressOn("Willow Bend");
    private static readonly string OtherAddress = BrokerSnapshot.AddressOn("Alder Rise");
    private static readonly Guid ThisSite = BrokerSnapshot.SiteOf(WillowBend.SubmissionId);
    private static readonly Guid OtherSite = BrokerSnapshot.SiteOf(BrokerSnapshot.OtherSubmissionId);

    /// <summary>A broker holding two submissions. The declaration read is answered whole; the findings
    /// read is answered with what a walk rooted at the site the selector names would have reached, which
    /// is what the platform does with the selector this service sends.</summary>
    private static IntakeWebApplicationFactory Holding(BrokerSnapshot? model = null) => new()
    {
        HandlerCallback = request => Answer(request, model ?? BrokerSnapshot.WithTwoSubmissions()),
    };

    private static HttpResponseMessage Answer(HttpRequestMessage request, BrokerSnapshot model)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Post && path == "/api/subscriptions")
            return ModelStub.Json(Reading(request, model));
        if (path.EndsWith("/ranges", StringComparison.Ordinal))
            return ModelStub.Json("""{"ThingId":"x","ThingName":"Study","OwnRanges":[],"InheritedRanges":[]}""");
        return new HttpResponseMessage(HttpStatusCode.OK);
    }

    // The platform resolves a selector into an object closure. A selector rooted at one site reaches that
    // site and what its walks lead to; the declaration read names no site and takes the model whole.
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

    private static async Task<HttpResponseMessage> AskAsync(
        IntakeWebApplicationFactory factory, HttpClient client, string submissionId, string emailAddress,
        string? ticket = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/submissions/findings")
        {
            Content = JsonContent.Create(new { submissionId, emailAddress }),
        };
        if (ticket is not null) request.Headers.Add(TicketHeader, ticket);
        else request.Headers.Add(TicketHeader, await TicketFor(factory, client, emailAddress));
        return await client.SendAsync(request);
    }

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

    [Fact]
    public async Task A_submitter_reads_the_page_the_model_declares_and_the_site_it_is_about()
    {
        await using var factory = Holding();
        using var client = factory.CreateClient();

        var response = await AskAsync(factory, client, WillowBend.SubmissionId, ThisAddress);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var answered = await response.Content.ReadFromJsonAsync<JsonElement>();
        answered.GetProperty("spec").GetString().Should().Contain("Site submission");
        answered.GetProperty("scopeId").GetString().Should().Be(ThisSite.ToString());
        Identifiers(answered, "things").Should().Contain(ThisSite.ToString());
    }

    // The judged Thing's ranges travel with the reading: a verdict reads its target off the comparison a
    // range makes, and those ranges sit on an archetype rather than on the Thing.
    [Fact]
    public async Task The_ranges_of_the_thing_the_analysis_judged_travel_with_it()
    {
        await using var factory = Holding();
        using var client = factory.CreateClient();

        var answered = await (await AskAsync(factory, client, WillowBend.SubmissionId, ThisAddress))
            .Content.ReadFromJsonAsync<JsonElement>();

        answered.GetProperty("ranges").EnumerateObject().Select(range => range.Name).Should()
            .Equal(BrokerSnapshot.StudyOf(WillowBend.SubmissionId).ToString());
    }

    [Fact]
    public async Task Nothing_of_another_submitters_land_is_in_the_answer()
    {
        await using var factory = Holding();
        using var client = factory.CreateClient();

        var response = await AskAsync(factory, client, WillowBend.SubmissionId, ThisAddress);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(OtherSite.ToString()).And.NotContain("Alder Rise");
    }

    [Fact]
    public async Task No_contact_detail_is_in_the_answer()
    {
        await using var factory = Holding();
        using var client = factory.CreateClient();

        var response = await AskAsync(factory, client, WillowBend.SubmissionId, ThisAddress);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(ThisAddress, "the address is what the ticket is checked against, not something answered back");
        body.Should().NotContain(BrokerSnapshot.ContactOf(WillowBend.SubmissionId).ToString());
    }

    [Fact]
    public async Task A_request_carrying_no_ticket_is_refused()
    {
        await using var factory = Holding();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/submissions/findings")
        {
            Content = JsonContent.Create(new { submissionId = WillowBend.SubmissionId, emailAddress = ThisAddress }),
        };
        var refused = await client.SendAsync(request);

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_ticket_issued_against_another_address_is_refused()
    {
        await using var factory = Holding();
        using var client = factory.CreateClient();
        var ticket = await TicketFor(factory, client, "somebody.else@example.pt");

        var refused = await AskAsync(factory, client, WillowBend.SubmissionId, ThisAddress, ticket);

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // Whoever holds a mailbox may ask about any reference they like. What they are told is the same
    // whether the reference exists or not, or answering would say which references exist to anybody who
    // tried one.
    [Fact]
    public async Task A_reference_belonging_to_somebody_else_and_a_reference_belonging_to_nobody_read_alike()
    {
        await using var factory = Holding();
        using var client = factory.CreateClient();

        var somebodyElses = await AskAsync(factory, client, BrokerSnapshot.OtherSubmissionId, ThisAddress);
        var nobodys = await AskAsync(factory, client, "00000000-0000-0000-0000-000000000000", ThisAddress);

        somebodyElses.StatusCode.Should().Be(HttpStatusCode.NotFound);
        nobodys.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await somebodyElses.Content.ReadAsStringAsync()).Should()
            .Be(await nobodys.Content.ReadAsStringAsync());
    }

    // A submission cleared at the end of its retention period is out of the live model, so the reference
    // and the address name nothing — which is what the same wording says.
    [Fact]
    public async Task A_submission_already_cleared_reads_as_one_that_never_existed()
    {
        await using var factory = Holding();
        using var client = factory.CreateClient();
        var whileItStood = await AskAsync(factory, client, WillowBend.SubmissionId, ThisAddress);
        whileItStood.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var afterDisposal = Holding(BrokerSnapshot.WithTwoSubmissions().Without(ThisSite));
        using var later = afterDisposal.CreateClient();
        var response = await AskAsync(afterDisposal, later, WillowBend.SubmissionId, ThisAddress);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should()
            .Contain(SubmissionFindingsService.NotYourSubmission);
    }

    // A deployment fault, not a request anybody can correct — and a stranger is told neither what is wrong
    // nor anything about the model they asked about.
    [Fact]
    public async Task A_model_never_seeded_for_this_answers_that_it_cannot_and_says_no_more()
    {
        await using var factory = Holding(BrokerSnapshot.WithTwoSubmissions().WithoutTheMarks());
        using var client = factory.CreateClient();

        var response = await AskAsync(factory, client, WillowBend.SubmissionId, ThisAddress);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(FindingsReader.FindingsDashboardFlag).And.NotContain("archetype");
    }

    // The broker being unreachable is a deployment's problem, not the caller's, and is answered the way
    // an unseeded model is.
    [Fact]
    public async Task A_model_this_service_cannot_read_answers_that_it_cannot_and_says_no_more()
    {
        await using var factory = new IntakeWebApplicationFactory
        {
            HandlerCallback = request => request.RequestUri!.AbsolutePath == "/api/subscriptions"
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : new HttpResponseMessage(HttpStatusCode.OK),
        };
        using var client = factory.CreateClient();

        var response = await AskAsync(factory, client, WillowBend.SubmissionId, ThisAddress);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("subscription");
    }

    // A verdict the model holds still reads without the target it names, so a Thing whose ranges the
    // broker will not answer about leaves that row without its figure rather than refusing the page.
    [Fact]
    public async Task Ranges_the_broker_refuses_leave_the_page_standing()
    {
        var model = BrokerSnapshot.WithTwoSubmissions();
        await using var factory = new IntakeWebApplicationFactory
        {
            HandlerCallback = request => request.RequestUri!.AbsolutePath.EndsWith("/ranges", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : Answer(request, model),
        };
        using var client = factory.CreateClient();

        var response = await AskAsync(factory, client, WillowBend.SubmissionId, ThisAddress);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var answered = await response.Content.ReadFromJsonAsync<JsonElement>();
        answered.GetProperty("ranges").EnumerateObject().Should().BeEmpty();
        Identifiers(answered, "things").Should().Contain(ThisSite.ToString());
    }

    // The broker reaps what a caller leaves behind, so a release nobody could make is worth a line in the
    // log and nothing more. Losing the read with it would be a page a submitter cannot open because a
    // subscription could not be tidied up.
    [Fact]
    public async Task A_release_that_fails_does_not_lose_the_read_that_succeeded()
    {
        var model = BrokerSnapshot.WithTwoSubmissions();
        await using var factory = new IntakeWebApplicationFactory
        {
            HandlerCallback = request => request.Method == HttpMethod.Delete
                ? throw new HttpRequestException("mycelium is already gone")
                : Answer(request, model),
        };
        using var client = factory.CreateClient();

        var response = await AskAsync(factory, client, WillowBend.SubmissionId, ThisAddress);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task It_counts_against_the_callers_budget()
    {
        await using var factory = Holding();
        using var client = factory.CreateClient();

        HttpResponseMessage? refused = null;
        for (var attempt = 0; attempt <= SubmissionRate.RequestsAllowed; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/submissions/findings")
            {
                Content = JsonContent.Create(new { submissionId = WillowBend.SubmissionId, emailAddress = ThisAddress }),
            };
            refused = await client.SendAsync(request);
        }

        refused!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    private static IEnumerable<string> Identifiers(JsonElement answered, string section) =>
        answered.GetProperty(section).EnumerateArray().Select(thing => thing.GetProperty("Id").GetString()!);
}
