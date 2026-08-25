using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using vos.Service.Intake;
using vos.Tests.Shared;
using Xunit;
using static vos.Service.Intake.Tests.ModelStub;

namespace vos.Service.Intake.Tests;

public class SubmissionEndpointTests
{
    private const string TicketHeader = "X-Submission-Ticket";

    private static readonly string WillowBendDocument =
        Document("'site':{'name':'Willow Bend','population':320}");

    /// <summary>A submission written with apostrophes where JSON wants quotation marks, so a fragment reads
    /// as the object it is rather than as escaping.</summary>
    private static string Document(string shape) =>
        ("{'submissionId':'" + WillowBend.SubmissionId + "'," + shape + "}").Replace('\'', '"');

    private static StringContent Submission(string document) =>
        new(document, Encoding.UTF8, "application/json");

    /// <summary>A form's whole exchange: ask for a ticket, then post under it.</summary>
    private static async Task<HttpResponseMessage> SubmitAsync(HttpClient client, string document)
    {
        var ticket = await TicketFrom(client);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/submissions")
        {
            Content = Submission(document),
        };
        request.Headers.Add(TicketHeader, ticket);
        return await client.SendAsync(request);
    }

    private static async Task<string> TicketFrom(HttpClient client)
    {
        var response = await client.GetAsync("/submissions/ticket");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ticket").GetString()!;
    }

    [Fact]
    public async Task An_accepted_submission_answers_with_a_reference_and_nothing_of_the_model()
    {
        await using var factory = new IntakeWebApplicationFactory { HandlerCallback = Holds };
        using var client = factory.CreateClient();

        var response = await SubmitAsync(client, WillowBendDocument);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(WillowBend.SubmissionId,
            "the submitter is answered with something they can quote to whoever reviews it");
        body.Should().NotContain("siteId").And.NotContain("studyId").And.NotContain("parcelId",
            "a stranger is not told what the model called the Things their submission became");
    }

    // The route this service exists for. A stranger holds no credential, and a verification key left in
    // this service's configuration — it is one setting of the set every service is launched with — must
    // not quietly close the one route that is meant to be open.
    [Fact]
    public async Task A_submission_carrying_no_credential_is_accepted_even_where_a_key_is_configured()
    {
        await using var factory = new IntakeWebApplicationFactory
        {
            HandlerCallback = Holds,
            VerificationKey = new MyceliumSigner().VerificationKey,
        };
        using var client = factory.CreateClient();

        (await SubmitAsync(client, WillowBendDocument)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_submission_that_cannot_be_read_is_refused_with_the_reason()
    {
        await using var factory = new IntakeWebApplicationFactory { HandlerCallback = Holds };
        using var client = factory.CreateClient();

        var response = await SubmitAsync(client, Document("'budgetEuros':250000"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("budgetEuros");
    }

    [Fact]
    public async Task A_body_beyond_the_cap_is_refused_before_it_is_read()
    {
        await using var factory = new IntakeWebApplicationFactory { HandlerCallback = Holds };
        using var client = factory.CreateClient();

        var corners = string.Join(",", Enumerable.Repeat("{'latitude':39.5,'longitude':-8.4}", 20_000));
        var document = Document("'site':{'name':'Willow Bend'},"
                                + "'parcel':{'boundarySource':'drawn-by-hand','boundary':[" + corners + "]}");

        var response = await SubmitAsync(client, document);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge,
            "a submission is a form's worth of answers and a boundary; anything larger must not be read into memory first");
    }

    [Fact]
    public async Task A_model_refusal_is_answered_with_its_text_rather_than_a_bare_failure()
    {
        await using var factory = new IntakeWebApplicationFactory
        {
            HandlerCallback = request => IsFragment(request)
                ? new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("""{"error":"Duplicate Thing id within the fragment"}"""),
                }
                : Holds(request),
        };
        using var client = factory.CreateClient();

        var response = await SubmitAsync(client, WillowBendDocument);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Duplicate Thing id");
    }

    // A broker that is not answering is nothing whoever filled the form in can correct, and what is wrong
    // with the deployment is not theirs to be told either.
    [Fact]
    public async Task A_broker_that_cannot_be_reached_is_answered_without_saying_what_is_wrong()
    {
        await using var factory = new IntakeWebApplicationFactory
        {
            HandlerCallback = request => IsFragment(request)
                ? throw new HttpRequestException("Connection refused (localhost:7243)")
                : Holds(request),
        };
        using var client = factory.CreateClient();

        var response = await SubmitAsync(client, WillowBendDocument);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("7243");
        factory.Log.Lines.Should().Contain(line => line.Contains("Connection refused"),
            "what is actually wrong belongs in the log, where whoever runs the deployment reads it");
    }

    [Fact]
    public async Task A_ticket_is_offered_to_whoever_asks_for_one()
    {
        await using var factory = new IntakeWebApplicationFactory { HandlerCallback = Holds };
        using var client = factory.CreateClient();

        var offered = await client.GetFromJsonAsync<JsonElement>("/submissions/ticket");

        offered.GetProperty("ticket").GetString().Should().NotBeNullOrWhiteSpace();
        offered.GetProperty("validForSeconds").GetInt32()
            .Should().Be((int)SubmissionTicket.ValidFor.TotalSeconds);
    }

    [Fact]
    public async Task A_submission_carrying_no_ticket_is_refused()
    {
        await using var factory = new IntakeWebApplicationFactory { HandlerCallback = Holds };
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/submissions", Submission(WillowBendDocument));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a post that never asked this service for anything first did not come from the form");
    }

    // Whatever a caller puts in the header, the service answers rather than falling over: a ticket is
    // read from what a stranger sent, and nothing about it is known before it has been read.
    [Theory]
    [InlineData("not a ticket at all")]
    [InlineData("AAAA")]
    public async Task Anything_in_the_header_that_is_not_a_ticket_is_refused(string presented)
    {
        await using var factory = new IntakeWebApplicationFactory { HandlerCallback = Holds };
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/submissions")
        {
            Content = Submission(WillowBendDocument),
        };
        request.Headers.Add(TicketHeader, presented);

        (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_ticket_this_service_did_not_issue_is_refused()
    {
        await using var factory = new IntakeWebApplicationFactory { HandlerCallback = Holds };
        using var client = factory.CreateClient();

        var ticket = await TicketFrom(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/submissions")
        {
            Content = Submission(WillowBendDocument),
        };
        request.Headers.Add(TicketHeader, ticket.Replace(ticket[^4..], "AAAA"));

        (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_form_left_open_longer_than_a_session_is_refused()
    {
        await using var factory = new IntakeWebApplicationFactory { HandlerCallback = Holds };
        using var client = factory.CreateClient();

        var ticket = await TicketFrom(client);
        factory.Clock.Advance(SubmissionTicket.ValidFor + TimeSpan.FromMinutes(1));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/submissions")
        {
            Content = Submission(WillowBendDocument),
        };
        request.Headers.Add(TicketHeader, ticket);

        (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_source_that_asks_for_more_than_its_share_is_told_when_to_come_back()
    {
        await using var factory = new IntakeWebApplicationFactory { HandlerCallback = Holds };
        using var client = factory.CreateClient();

        HttpResponseMessage? refused = null;
        for (var attempt = 0; attempt <= SubmissionRate.RequestsAllowed; attempt++)
            refused = await AskForATicket(client, from: "203.0.113.5");

        refused!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        refused.Headers.RetryAfter.Should().NotBeNull("a refusal a caller cannot time is a refusal it retries into");
    }

    [Fact]
    public async Task One_source_asking_for_more_than_its_share_leaves_another_alone()
    {
        await using var factory = new IntakeWebApplicationFactory { HandlerCallback = Holds };
        using var client = factory.CreateClient();

        for (var attempt = 0; attempt <= SubmissionRate.RequestsAllowed; attempt++)
            await AskForATicket(client, from: "203.0.113.5");

        (await AskForATicket(client, from: "203.0.113.6")).StatusCode.Should().Be(HttpStatusCode.OK,
            "one source spending its budget must not close the service to everybody behind it");
    }

    // The service run with nothing in front of it. There is no address to tell callers apart by, so they
    // are one source and the log says as much — rather than the route failing on the address it expected.
    [Fact]
    public async Task A_request_arriving_with_no_address_is_one_source_like_any_other()
    {
        await using var factory = new IntakeWebApplicationFactory
        {
            HandlerCallback = Holds,
            ArrivesThroughAProxy = false,
        };
        using var client = factory.CreateClient();

        (await SubmitAsync(client, WillowBendDocument)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync("/submissions", Submission(WillowBendDocument))).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        factory.Log.Lines.Should().Contain(line => line.Contains("source unknown"));
    }

    private static async Task<HttpResponseMessage> AskForATicket(HttpClient client, string from)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/submissions/ticket");
        request.Headers.Add("X-Forwarded-For", from);
        return await client.SendAsync(request);
    }

    /// <summary>A synthetic enquiry. Contact details reach this service by design, and a log line is the
    /// one place they would leave the model behind.</summary>
    private static string WithAContact(string sitePart) =>
        Document("'project':{'name':'Willow Bend Regeneration'},"
                 + "'contact':{'name':'Ana Ferreira','emailAddress':'ana.ferreira@example.pt'},"
                 + "'site':{'name':'Willow Bend'" + sitePart + "}");

    [Fact]
    public async Task An_accepted_submission_is_logged_by_reference_and_by_nothing_else_it_carries()
    {
        await using var factory = new IntakeWebApplicationFactory { HandlerCallback = Holds };
        using var client = factory.CreateClient();

        (await SubmitAsync(client, WithAContact(""))).StatusCode.Should().Be(HttpStatusCode.OK);

        factory.Log.Lines.Should().Contain(line => line.Contains(WillowBend.SubmissionId),
            "a reviewer looks the submission up under what the submitter was answered with");
        factory.Log.Lines.Should().NotContain(line =>
            line.Contains("Ana Ferreira") || line.Contains("ana.ferreira@example.pt"));
    }

    [Fact]
    public async Task A_refused_submission_is_logged_by_its_reason_and_by_none_of_what_was_submitted()
    {
        await using var factory = new IntakeWebApplicationFactory { HandlerCallback = Holds };
        using var client = factory.CreateClient();

        var refused = await SubmitAsync(client, WithAContact(",'latitude':91.0"));

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        factory.Log.Lines.Should().Contain(line => line.Contains("site.latitude"));
        factory.Log.Lines.Should().NotContain(line =>
            line.Contains("Ana Ferreira") || line.Contains("ana.ferreira@example.pt")
            || line.Contains("submissionId\":"));
    }
}
