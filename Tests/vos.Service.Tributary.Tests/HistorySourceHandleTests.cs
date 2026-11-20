using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Xunit;
using static vos.Service.Tributary.Tests.MyceliumStub;

namespace vos.Service.Tributary.Tests;

public class HistorySourceHandleTests
{
    private const string TimedSliceProperties = """
        {
          "Endpoint.url":               {"Value":"https://api.test/history"},
          "Endpoint.httpMethod":        {"Value":"GET"},
          "Endpoint.responseTransform": {"Value":"[{\"observedAt\": at, \"properties\": {\"temperatureCelsius\": t}}]"}
        }
        """;

    private sealed record Written(HttpStatusCode Answered, string Said, List<string> Deposits, List<string> Readings);

    private static async Task<Written> HandleAsync(bool historySource, HttpResponseMessage? depositAnswer = null)
    {
        var endpoint = Guid.NewGuid();
        var site = Guid.NewGuid();
        var deposits = new List<string>();
        var readings = new List<string>();
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = request => request.RequestUri!.Host == "api.test"
            ? Json("""{"at":"2024-01-01T00:00:00Z","t":1.5}""")
            : RouteFindThing(request, endpoint, "EP")
              ?? RouteEffectiveProperties(request, endpoint, TimedSliceProperties)
              ?? RouteKindsFromProperties(request, endpoint, TimedSliceProperties, alreadyObserved: [site], historySource: historySource)
              ?? (depositAnswer is not null && request.RequestUri.AbsolutePath == "/api/sediment" ? depositAnswer : null)
              ?? RouteDeposit(request, deposits)
              ?? RouteObservationWrite(request, site, readings)
              ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var answer = await client.PostAsJsonAsync("/handle", new { endpointName = "EP", subjectId = site });
        return new Written(answer.StatusCode, await answer.Content.ReadAsStringAsync(), deposits, readings);
    }

    private static HttpResponseMessage Refused(HttpStatusCode status, string error) =>
        new(status) { Content = new StringContent($$"""{"error":"{{error}}"}""", Encoding.UTF8, "application/json") };

    [Fact]
    public async Task A_history_sources_answer_is_deposited_and_not_written_as_readings()
    {
        var written = await HandleAsync(historySource: true);

        written.Answered.Should().Be(HttpStatusCode.OK, written.Said);
        written.Deposits.Should().ContainSingle().Which.Should().Contain("temperatureCelsius").And.Contain("2024-01-01T00:00:00");
        written.Readings.Should().BeEmpty();
    }

    [Fact]
    public async Task Another_sources_answer_is_written_as_readings_and_nothing_is_deposited()
    {
        var written = await HandleAsync(historySource: false);

        written.Answered.Should().Be(HttpStatusCode.OK, written.Said);
        written.Readings.Should().ContainSingle();
        written.Deposits.Should().BeEmpty();
    }

    [Fact]
    public async Task A_deposit_the_broker_can_take_later_answers_so_the_call_is_tried_again()
    {
        var written = await HandleAsync(historySource: true,
            Refused(HttpStatusCode.ServiceUnavailable, "The readings queue is full; nothing in this request was queued."));

        ((int)written.Answered).Should().BeGreaterThanOrEqualTo(500, written.Said);
        written.Said.Should().Contain("The readings queue is full");
    }

    [Fact]
    public async Task A_deposit_the_broker_refuses_for_good_answers_so_the_call_is_not_tried_again()
    {
        var written = await HandleAsync(historySource: true,
            Refused(HttpStatusCode.Forbidden, "Only an admin writes a directory."));

        ((int)written.Answered).Should().BeInRange(400, 499, written.Said);
        written.Said.Should().Contain("Only an admin writes a directory.");
    }
}
