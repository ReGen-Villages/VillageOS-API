using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Intake.Helpers;
using vos.Service.Intake.Services;
using vos.Tests.Shared;
using Xunit;
using static vos.Service.Intake.Tests.ModelStub;

namespace vos.Service.Intake.Tests;

public class SubmissionIntakeServiceTests
{
    private const string Document = $$"""
        {
          "submissionId": "{{WillowBend.SubmissionId}}",
          "site": { "name": "Willow Bend", "statedAreaHectares": 24.0, "population": 320 }
        }
        """;

    private static SubmissionIntakeService Service(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new IntakeMyceliumClient(
            new PerCallHttpClientFactory(new MockHttpMessageHandler(respond)),
            NullLogger<IntakeMyceliumClient>.Instance,
            "http://localhost",
            "test-token"));

    // The posted document is read inside the responder: the client disposes the request content once the
    // call returns, so reading it afterwards finds nothing.
    private static (SubmissionIntakeService Service, Func<string?> PostedFragment) ServiceCapturingFragment()
    {
        string? captured = null;
        var service = Service(request =>
        {
            if (IsFragment(request))
                captured = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Holds(request);
        });
        return (service, () => captured);
    }

    [Fact]
    public async Task The_whole_submission_is_applied_as_one_fragment()
    {
        var applications = 0;
        var service = Service(request =>
        {
            if (IsFragment(request)) applications++;
            return Holds(request);
        });

        await service.SubmitAsync(Document, CancellationToken.None);

        applications.Should().Be(1);
    }

    [Fact]
    public async Task A_predicate_the_model_holds_is_related_through_rather_than_built_again()
    {
        var studies = Guid.NewGuid();
        var service = Service(request => IsFragment(request)
            ? Json("{}")
            : Json($$"""{"Id":"{{studies}}","Name":"studies"}"""));

        var composed = await service.SubmitAsync(Document, CancellationToken.None);

        composed.Fragment.Relationships.Single().Predicate.Should().Be(studies);
        composed.Fragment.Things.Should().NotContain(thing => thing.Id == studies,
            "a predicate the model already holds must not be built a second time beside it");
    }

    [Fact]
    public async Task A_predicate_the_model_lacks_is_minted_under_a_derived_identifier()
    {
        var service = Service(request => IsFragment(request)
            ? Json("{}")
            : new HttpResponseMessage(HttpStatusCode.NotFound));

        var composed = await service.SubmitAsync(Document, CancellationToken.None);

        composed.Fragment.Relationships.Single().Predicate
            .Should().Be(StableIdentity.DerivePredicate("studies"),
                "two submissions into a model that holds no `studies` yet must agree on which Thing it is");
        composed.Fragment.Things.Should().Contain(thing => thing.Name == "studies");
    }

    [Fact]
    public async Task A_lookup_that_failed_is_not_read_as_a_predicate_the_model_lacks()
    {
        var service = Service(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.SubmitAsync(Document, CancellationToken.None));
    }

    [Fact]
    public async Task The_model_refusal_reaches_the_caller_in_its_own_words()
    {
        var service = Service(request => IsFragment(request)
            ? new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"error":"Property 'statedAreaHectares' on 'Willow Bend' is computed."}"""),
            }
            : Holds(request));

        var refusal = await Assert.ThrowsAsync<SubmissionError>(
            () => service.SubmitAsync(Document, CancellationToken.None));

        refusal.Message.Should().Contain("statedAreaHectares").And.Contain("Willow Bend");
    }

    [Fact]
    public async Task A_lookup_that_answers_with_no_thing_is_read_as_a_predicate_the_model_lacks()
    {
        var service = Service(request => IsFragment(request) ? Json("{}") : Json("null"));

        var composed = await service.SubmitAsync(Document, CancellationToken.None);

        composed.Fragment.Relationships.Single().Predicate
            .Should().Be(StableIdentity.DerivePredicate("studies"),
                "the model answers a name it does not hold with an empty body as readily as with a 404");
    }

    [Fact]
    public async Task The_posted_fragment_declares_the_computed_output_without_a_value()
    {
        var (service, fragment) = ServiceCapturingFragment();

        await service.SubmitAsync(Document, CancellationToken.None);

        var study = JsonDocument.Parse(fragment()!).RootElement.GetProperty("Things").EnumerateArray()
            .Single(thing => thing.GetProperty("Properties").TryGetProperty("energySelfSufficiencyPct", out _));
        var declared = study.GetProperty("Properties").GetProperty("energySelfSufficiencyPct");

        declared.GetProperty("typeInfo").GetString().Should().Be("vos.Double");
        declared.TryGetProperty("value", out _).Should().BeFalse();
    }

    [Fact]
    public async Task The_posted_fragment_names_the_things_by_the_keys_the_model_reads()
    {
        var (service, fragment) = ServiceCapturingFragment();

        await service.SubmitAsync(Document, CancellationToken.None);

        var posted = JsonDocument.Parse(fragment()!).RootElement;
        posted.GetProperty("Things").EnumerateArray().First().TryGetProperty("Id", out _).Should().BeTrue();
        var edge = posted.GetProperty("Relationships").EnumerateArray().Single();
        foreach (var key in new[] { "Subject", "Predicate", "Target" })
            edge.TryGetProperty(key, out _).Should().BeTrue($"the model reads an edge's {key} under that name");
    }
}
