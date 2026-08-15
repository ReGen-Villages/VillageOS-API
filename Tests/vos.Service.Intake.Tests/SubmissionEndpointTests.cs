using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using vos.Service.Intake.Helpers;
using Xunit;
using static vos.Service.Intake.Tests.ModelStub;

namespace vos.Service.Intake.Tests;

public class SubmissionEndpointTests
{
    private static StringContent Submission(string document) =>
        new(document, Encoding.UTF8, "application/json");

    [Fact]
    public async Task An_accepted_submission_answers_with_the_things_it_wrote()
    {
        await using var factory = new IntakeWebApplicationFactory { HandlerCallback = Holds };
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/submissions", Submission(
            """{"submissionId":"willow-bend-2026-08","site":{"name":"Willow Bend","population":320}}"""));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var accepted = await response.Content.ReadFromJsonAsync<JsonElement>();
        accepted.GetProperty("siteId").GetGuid()
            .Should().Be(StableIdentity.Derive("willow-bend-2026-08", "site"));
        accepted.GetProperty("parcelId").ValueKind.Should().Be(JsonValueKind.Null,
            "no boundary was drawn, so there is no parcel to answer with");
    }

    [Fact]
    public async Task A_submission_that_cannot_be_read_is_refused_with_the_reason()
    {
        await using var factory = new IntakeWebApplicationFactory { HandlerCallback = Holds };
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/submissions", Submission(
            """{"submissionId":"willow-bend-2026-08","budgetEuros":250000}"""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("budgetEuros");
    }

    [Fact]
    public async Task A_body_beyond_the_cap_is_refused_before_it_is_read()
    {
        await using var factory = new IntakeWebApplicationFactory { HandlerCallback = Holds };
        using var client = factory.CreateClient();

        var corners = string.Join(",", Enumerable.Repeat("""{"latitude":39.5,"longitude":-8.4}""", 20_000));
        var document = """{"submissionId":"willow-bend-2026-08","site":{"name":"Willow Bend"},"parcel":{"boundarySource":"drawn-by-hand","boundary":["""
                       + corners + "]}}";

        var response = await client.PostAsync("/submissions", Submission(document));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge,
            "a submission is a form's worth of answers and a boundary; anything larger must not be read into memory first");
    }

    [Fact]
    public async Task A_submission_without_a_token_is_refused_where_a_key_is_configured()
    {
        await using var factory = new IntakeWebApplicationFactory
        {
            HandlerCallback = Holds,
            SigningKey = Convert.ToBase64String(new byte[32]),
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/submissions", Submission(
            """{"submissionId":"willow-bend-2026-08","site":{"name":"Willow Bend"}}"""));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "anonymous submission is a decision this service has yet to be given the guards for");
        (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK,
            "liveness cannot be checked from behind authentication");
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

        var response = await client.PostAsync("/submissions", Submission(
            """{"submissionId":"willow-bend-2026-08","site":{"name":"Willow Bend"}}"""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Duplicate Thing id");
    }
}
