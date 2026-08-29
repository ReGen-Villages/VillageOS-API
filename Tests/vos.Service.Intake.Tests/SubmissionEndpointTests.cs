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

    private static readonly string WillowBendDocument = AboutTheSite(",'population':320");

    /// <summary>The project and contact every submission carries. The details are a synthetic enquiry:
    /// they reach this service by design, and a log line is the one place they would leave the model
    /// behind.</summary>
    private const string ProjectAndContact =
        "'project':{'name':'Willow Bend Regeneration'},"
        + "'contact':{'name':'Ana Ferreira','emailAddress':'ana.ferreira@example.pt'},";

    /// <summary>The land itself, which every submission describes — an analysis divides its area, so a
    /// submission without one is refused. Generated from the stated area rather than surveyed, which is
    /// what the boundary source records.</summary>
    private const string TheLand =
        ",'parcel':{'boundarySource':'generated-from-stated-area','boundary':["
        + "{'latitude':39.4988,'longitude':-8.4168},{'latitude':39.5036,'longitude':-8.4168},"
        + "{'latitude':39.5036,'longitude':-8.4106},{'latitude':39.4988,'longitude':-8.4106}]}";

    /// <summary>A whole submission, varying only what is known about the land.</summary>
    private static string AboutTheSite(string sitePart) =>
        Document(ProjectAndContact + "'site':{'name':'Willow Bend'" + sitePart + "}" + TheLand);

    /// <summary>A submission written with apostrophes where JSON wants quotation marks, so a fragment reads
    /// as the object it is rather than as escaping.</summary>
    private static string Document(string shape) =>
        ("{'submissionId':'" + WillowBend.SubmissionId + "'," + shape + "}").Replace('\'', '"');

    private static StringContent Submission(string document) =>
        new(document, Encoding.UTF8, "application/json");

    /// <summary>The address the submissions here name, which is therefore the one they verify.</summary>
    private const string AnasAddress = "ana.ferreira@example.pt";

    /// <summary>A form's whole exchange: ask for a code, answer it, then post under the ticket that
    /// comes back.</summary>
    private static async Task<HttpResponseMessage> SubmitAsync(
        IntakeWebApplicationFactory factory, HttpClient client, string document) =>
        await PostAsync(client, document, await TicketFor(factory, client, AnasAddress));

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, string document, string ticket)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/submissions")
        {
            Content = Submission(document),
        };
        request.Headers.Add(TicketHeader, ticket);
        return await client.SendAsync(request);
    }

    private static async Task<string> TicketFor(
        IntakeWebApplicationFactory factory, HttpClient client, string emailAddress)
    {
        (await AskForACodeAsync(client, emailAddress)).EnsureSuccessStatusCode();

        var exchanged = await client.PostAsJsonAsync(
            "/submissions/ticket",
            new { emailAddress, code = factory.Mailer.CodeSentTo(emailAddress) });
        exchanged.EnsureSuccessStatusCode();
        return (await exchanged.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ticket").GetString()!;
    }

    private static Task<HttpResponseMessage> AskForACodeAsync(HttpClient client, string emailAddress) =>
        client.PostAsJsonAsync("/submissions/verification", new { emailAddress });

    [Fact]
    public async Task An_accepted_submission_answers_with_a_reference_and_nothing_of_the_model()
    {
        await using var factory = new IntakeWebApplicationFactory { HandlerCallback = Holds };
        using var client = factory.CreateClient();

        var response = await SubmitAsync(factory, client, WillowBendDocument);

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

        (await SubmitAsync(factory, client, WillowBendDocument)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_submission_that_cannot_be_read_is_refused_with_the_reason()
    {
        await using var factory = new IntakeWebApplicationFactory { HandlerCallback = Holds };
        using var client = factory.CreateClient();

        var response = await SubmitAsync(factory, client, Document("'budgetEuros':250000"));

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

        var response = await SubmitAsync(factory, client, document);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge,
            "a submission is a form's worth of answers and a boundary; anything larger must not be read into memory first");
    }

    // A refusal is not a partial write. Every bound is checked before the fragment is composed, so a
    // submission the service will not take never reaches the model at all.
    [Theory]
    [InlineData("'budgetEuros':250000")]
    [InlineData("'site':{'name':'Willow Bend','latitude':91.0}")]
    [InlineData("'allocations':[{'category':'residential','sharePct':900}]")]
    public async Task A_refused_submission_writes_nothing(string shape)
    {
        var written = false;
        await using var factory = new IntakeWebApplicationFactory
        {
            HandlerCallback = request =>
            {
                written |= IsFragment(request);
                return Holds(request);
            },
        };
        using var client = factory.CreateClient();

        (await SubmitAsync(factory, client, Document(shape))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        written.Should().BeFalse();
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

        var response = await SubmitAsync(factory, client, WillowBendDocument);

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

        var response = await SubmitAsync(factory, client, AboutTheSite(""));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("7243", "what is wrong with the deployment is not the submitter's to be told");
        body.Should().NotContain("Ana Ferreira").And.NotContain("ana.ferreira@example.pt",
            "a failure nobody planned for is where a submitted value gets echoed back");
        factory.Log.Lines.Should().Contain(line => line.Contains("Connection refused"),
            "what is actually wrong belongs in the log, where whoever runs the deployment reads it");
    }

    [Fact]
    public async Task A_ticket_is_offered_to_whoever_answers_the_code_that_was_sent()
    {
        await using var factory = new IntakeWebApplicationFactory { HandlerCallback = Holds };
        using var client = factory.CreateClient();

        (await AskForACodeAsync(client, AnasAddress)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var exchanged = await client.PostAsJsonAsync(
            "/submissions/ticket",
            new { emailAddress = AnasAddress, code = factory.Mailer.CodeSentTo(AnasAddress) });

        var offered = await exchanged.Content.ReadFromJsonAsync<JsonElement>();
        offered.GetProperty("ticket").GetString().Should().NotBeNullOrWhiteSpace();
        offered.GetProperty("validForSeconds").GetInt32()
            .Should().Be((int)SubmissionTicket.ValidFor.TotalSeconds);
    }

    [Fact]
    public async Task A_ticket_is_refused_to_whoever_cannot_answer_the_code()
    {
        await using var factory = new IntakeWebApplicationFactory { HandlerCallback = Holds };
        using var client = factory.CreateClient();

        await AskForACodeAsync(client, AnasAddress);
        var refused = await client.PostAsJsonAsync(
            "/submissions/ticket", new { emailAddress = AnasAddress, code = "000000" });

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the code is the whole of what says this address can be read by whoever is submitting");
    }

    // The ticket says which address it was issued against, so it cannot be spent on a submission naming
    // somebody else's. Without this, one verified address would be a ticket to submit under any.
    [Fact]
    public async Task A_submission_naming_an_address_the_ticket_was_not_issued_for_is_refused()
    {
        await using var factory = new IntakeWebApplicationFactory { HandlerCallback = Holds };
        using var client = factory.CreateClient();

        var ticket = await TicketFor(factory, client, "somebody.else@example.pt");

        var refused = await PostAsync(client, WillowBendDocument, ticket);

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await refused.Content.ReadAsStringAsync()).Should().NotContain(AnasAddress,
            "a refusal names no address, whether the submitted one or the verified one");
    }

    // The address a code was sent to and the address on the submission are the same mailbox however each
    // was typed, so a ticket has to survive the difference rather than turn a submitter away over it.
    [Fact]
    public async Task An_address_verified_under_another_spelling_still_carries_its_ticket()
    {
        await using var factory = new IntakeWebApplicationFactory { HandlerCallback = Holds };
        using var client = factory.CreateClient();

        var ticket = await TicketFor(factory, client, "  Ana.Ferreira@Example.PT  ");

        (await PostAsync(client, WillowBendDocument, ticket)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_address_asked_for_more_codes_than_the_window_allows_is_sent_no_more()
    {
        await using var factory = new IntakeWebApplicationFactory { HandlerCallback = Holds };
        using var client = factory.CreateClient();

        for (var asked = 0; asked < AddressVerification.CodesPerAddress; asked++)
            (await AskForACodeAsync(client, AnasAddress)).EnsureSuccessStatusCode();

        var refused = await AskForACodeAsync(client, AnasAddress);

        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        factory.Mailer.Sent.Should().HaveCount(AddressVerification.CodesPerAddress,
            "a route that sends on demand is a way to post to a mailbox its owner never gave us");
    }

    [Fact]
    public async Task A_code_answered_wrongly_too_often_stops_working()
    {
        await using var factory = new IntakeWebApplicationFactory { HandlerCallback = Holds };
        using var client = factory.CreateClient();

        await AskForACodeAsync(client, AnasAddress);
        var code = factory.Mailer.CodeSentTo(AnasAddress);
        for (var guess = 0; guess < AddressVerification.AnswersAllowed; guess++)
            await client.PostAsJsonAsync(
                "/submissions/ticket", new { emailAddress = AnasAddress, code = "000000" });

        var refused = await client.PostAsJsonAsync(
            "/submissions/ticket", new { emailAddress = AnasAddress, code });

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "guessing a six-figure code has to run out rather than merely be unlikely");
    }

    // The composer refuses a submission carrying no address, by name. Asking the ticket first would answer
    // a missing field with a rule about a ticket, which is not what whoever filled the form in can correct.
    [Fact]
    public async Task A_submission_carrying_no_address_at_all_is_refused_by_the_field_it_is_missing()
    {
        await using var factory = new IntakeWebApplicationFactory { HandlerCallback = Holds };
        using var client = factory.CreateClient();
        var ticket = await TicketFor(factory, client, AnasAddress);

        var refused = await PostAsync(
            client,
            Document("'project':{'name':'Willow Bend Regeneration'},'site':{'name':'Willow Bend'}"),
            ticket);

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("contact");
    }

    // Three failures nobody saw would otherwise leave the address unable to be sent anything for the hour,
    // with no code ever delivered to show for it.
    [Fact]
    public async Task A_send_that_failed_costs_the_address_none_of_its_budget()
    {
        await using var factory = new IntakeWebApplicationFactory { HandlerCallback = Holds };
        factory.Mailer.Refuses = new InvalidOperationException("the relay is not answering");
        using var client = factory.CreateClient();

        for (var failed = 0; failed < AddressVerification.CodesPerAddress; failed++)
            (await AskForACodeAsync(client, AnasAddress)).StatusCode
                .Should().Be(HttpStatusCode.ServiceUnavailable);

        factory.Mailer.Refuses = null;

        (await AskForACodeAsync(client, AnasAddress)).StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task A_deployment_whose_mail_is_not_working_says_nothing_of_what_is_wrong()
    {
        await using var factory = new IntakeWebApplicationFactory { HandlerCallback = Holds };
        factory.Mailer.Refuses = new InvalidOperationException("relay access denied for intake@example.test");
        using var client = factory.CreateClient();

        var response = await AskForACodeAsync(client, AnasAddress);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("relay access denied");
        factory.Log.Lines.Should().Contain(line => line.Contains("relay access denied"),
            "what is wrong with the deployment belongs where whoever runs it reads it");
    }

    [Fact]
    public async Task Asking_to_verify_something_that_is_not_an_address_is_refused_by_name()
    {
        await using var factory = new IntakeWebApplicationFactory { HandlerCallback = Holds };
        using var client = factory.CreateClient();

        var refused = await AskForACodeAsync(client, "ana ferreira");

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("emailAddress").And.NotContain("ana ferreira");
        factory.Mailer.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Neither_an_address_nor_a_code_is_ever_written_down()
    {
        await using var factory = new IntakeWebApplicationFactory { HandlerCallback = Holds };
        using var client = factory.CreateClient();

        await AskForACodeAsync(client, AnasAddress);
        var code = factory.Mailer.CodeSentTo(AnasAddress);
        await client.PostAsJsonAsync("/submissions/ticket", new { emailAddress = AnasAddress, code = "000000" });
        await client.PostAsJsonAsync("/submissions/ticket", new { emailAddress = AnasAddress, code });

        factory.Log.Lines.Should().NotContain(line => line.Contains(AnasAddress) || line.Contains(code));
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

        var ticket = await TicketFor(factory, client, AnasAddress);
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

        var ticket = await TicketFor(factory, client, AnasAddress);
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
            refused = await AskFromAsync(client, from: "203.0.113.5", attempt);

        refused!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        refused.Headers.RetryAfter.Should().NotBeNull("a refusal a caller cannot time is a refusal it retries into");
    }

    [Fact]
    public async Task One_source_asking_for_more_than_its_share_leaves_another_alone()
    {
        await using var factory = new IntakeWebApplicationFactory { HandlerCallback = Holds };
        using var client = factory.CreateClient();

        for (var attempt = 0; attempt <= SubmissionRate.RequestsAllowed; attempt++)
            await AskFromAsync(client, from: "203.0.113.5", attempt);

        (await AskFromAsync(client, from: "203.0.113.6", attempt: 0)).StatusCode
            .Should().Be(HttpStatusCode.Accepted,
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

        (await SubmitAsync(factory, client, WillowBendDocument)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync("/submissions", Submission(WillowBendDocument))).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        factory.Log.Lines.Should().Contain(line => line.Contains("source unknown"));
    }

    /// <summary>One request against a source's budget. A different address each time, so what runs out is
    /// the source's share of the route and never the budget one address has for codes.</summary>
    private static async Task<HttpResponseMessage> AskFromAsync(HttpClient client, string from, int attempt)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/submissions/verification")
        {
            Content = JsonContent.Create(new { emailAddress = $"asker-{from}-{attempt}@example.pt" }),
        };
        request.Headers.Add("X-Forwarded-For", from);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task An_accepted_submission_is_logged_by_reference_and_by_nothing_else_it_carries()
    {
        await using var factory = new IntakeWebApplicationFactory { HandlerCallback = Holds };
        using var client = factory.CreateClient();

        (await SubmitAsync(factory, client, AboutTheSite(""))).StatusCode.Should().Be(HttpStatusCode.OK);

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

        var refused = await SubmitAsync(factory, client, AboutTheSite(",'latitude':91.0"));

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        factory.Log.Lines.Should().Contain(line => line.Contains("site.latitude"));
        factory.Log.Lines.Should().NotContain(line =>
            line.Contains("Ana Ferreira") || line.Contains("ana.ferreira@example.pt")
            || line.Contains("submissionId\":"));
    }
}
