using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using static vos.Service.Tributary.Tests.MyceliumStub;

namespace vos.Service.Tributary.Tests;

public class StepHandleTests
{
    private const string ReadingEndpoint = """
    {
      "Endpoint.url":               {"Value":"https://api.test/point?lat={lat}&lon={lon}"},
      "Endpoint.httpMethod":        {"Value":"GET"},
      "Endpoint.responseTransform": {"Value":"{\"properties\": {\"flowRegime\": regime}}"}
    }
    """;

    private sealed class Recorder
    {
        public List<Guid> ObservedOn { get; } = new();
        public List<string> ProviderAddresses { get; } = new();
    }

    private static TributaryWebApplicationFactory FactoryOver(Recorder recorder, Func<HttpResponseMessage> provider)
    {
        var endpointId = Guid.NewGuid();
        var factory = new TributaryWebApplicationFactory();
        factory.HandlerCallback = request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.RequestUri.Host == "api.test")
            {
                recorder.ProviderAddresses.Add(request.RequestUri.ToString());
                return provider();
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/observations"))
            {
                recorder.ObservedOn.Add(Guid.Parse(path.Split('/')[3]));
                return Json("""{"accepted":1}""");
            }
            if (request.Method == HttpMethod.Post && path == "/api/relationships")
                return Json("{\"Id\":\"" + Guid.NewGuid() + "\"}");
            return RouteFindThing(request, endpointId, "EP")
                ?? RouteEffectiveProperties(request, endpointId, ReadingEndpoint)
                ?? RouteKindsFromProperties(request, endpointId, ReadingEndpoint)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        return factory;
    }

    private static object CallFor(Guid subjectId, string endpointName = "EP") => new
    {
        endpointName,
        subjectId,
        addressParameters = new Dictionary<string, string> { ["lat"] = "39.4", ["lon"] = "-8.2" },
        recordId = "carried through untouched",
    };

    private static object StepRequest(object? call) => new
    {
        runId = Guid.NewGuid(),
        nodeId = Guid.NewGuid(),
        inputs = call is null ? new Dictionary<string, object?>() : new Dictionary<string, object?> { ["call"] = call },
    };

    private static async Task<(JsonElement Answer, Recorder Recorder)> StepAsync(object? call, Func<HttpResponseMessage>? provider = null)
    {
        var recorder = new Recorder();
        await using var factory = FactoryOver(recorder, provider ?? (() => Json("""{"regime":"steady"}""")));
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", StepRequest(call));

        response.StatusCode.Should().Be(HttpStatusCode.OK, "a step's failure is said in its answer, never in its status");
        return (await response.Content.ReadFromJsonAsync<JsonElement>(), recorder);
    }

    private static HttpResponseMessage Text(HttpStatusCode status, string words) =>
        new(status) { Content = new StringContent(words, Encoding.UTF8, "text/plain") };

    [Fact]
    public async Task A_step_request_fetches_its_call_and_writes_the_readings_onto_the_subject()
    {
        var subject = Guid.NewGuid();

        var (answer, recorder) = await StepAsync(CallFor(subject));

        answer.GetProperty("result").GetString().Should().Be("success");
        recorder.ProviderAddresses.Should().Equal("https://api.test/point?lat=39.4&lon=-8.2");
        recorder.ObservedOn.Should().Equal(subject);
        var outcome = answer.GetProperty("outputs").GetProperty("outcome");
        outcome.GetProperty("call").GetProperty("recordId").GetString().Should().Be("carried through untouched");
        outcome.GetProperty("call").GetProperty("subjectId").GetGuid().Should().Be(subject);
        outcome.GetProperty("written").GetProperty("flowRegime").GetString().Should().Be("steady");
    }

    [Fact]
    public async Task A_provider_that_is_down_is_answered_as_a_failure_that_may_be_tried_again_with_the_wait_it_asked_for()
    {
        var (answer, recorder) = await StepAsync(CallFor(Guid.NewGuid()), () =>
        {
            var down = Text(HttpStatusCode.ServiceUnavailable, "maintenance until noon");
            down.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return down;
        });

        answer.GetProperty("result").GetString().Should().Be("fail");
        answer.GetProperty("error").GetString().Should().Be("503: maintenance until noon");
        answer.GetProperty("retry").GetProperty("afterSeconds").GetDouble().Should().Be(30);
        var outcome = answer.GetProperty("outputs").GetProperty("outcome");
        outcome.GetProperty("call").GetProperty("endpointName").GetString().Should().Be("EP");
        outcome.GetProperty("reason").GetString().Should().Be("503: maintenance until noon");
        recorder.ObservedOn.Should().BeEmpty();
    }

    [Fact]
    public async Task A_provider_that_cannot_be_reached_is_answered_as_a_failure_that_may_be_tried_again()
    {
        var (answer, _) = await StepAsync(CallFor(Guid.NewGuid()), () => throw new HttpRequestException("Connection refused"));

        answer.GetProperty("result").GetString().Should().Be("fail");
        answer.TryGetProperty("retry", out _).Should().BeTrue();
        answer.GetProperty("error").GetString().Should().Contain("Connection refused");
    }

    [Fact]
    public async Task A_provider_refusing_the_question_is_answered_as_a_final_failure()
    {
        var (answer, _) = await StepAsync(CallFor(Guid.NewGuid()), () => Text(HttpStatusCode.BadRequest, "lat out of range"));

        answer.GetProperty("result").GetString().Should().Be("fail");
        answer.GetProperty("error").GetString().Should().Be("400: lat out of range");
        answer.TryGetProperty("retry", out _).Should().BeFalse();
        answer.GetProperty("outputs").GetProperty("outcome").GetProperty("call").GetProperty("endpointName").GetString().Should().Be("EP");
    }

    [Fact]
    public async Task A_call_naming_no_registration_the_model_holds_is_answered_as_a_final_failure()
    {
        var (answer, recorder) = await StepAsync(CallFor(Guid.NewGuid(), endpointName: "Nowhere"));

        answer.GetProperty("result").GetString().Should().Be("fail");
        answer.GetProperty("error").GetString().Should().Be("404: Endpoint thing not found: Nowhere");
        answer.TryGetProperty("retry", out _).Should().BeFalse();
        recorder.ProviderAddresses.Should().BeEmpty();
    }

    [Fact]
    public async Task A_step_request_with_no_call_fails_saying_so()
    {
        var (answer, recorder) = await StepAsync(call: null);

        answer.GetProperty("result").GetString().Should().Be("fail");
        answer.GetProperty("error").GetString().Should().Be("Tributary's step takes one call on its 'call' input: the registration, the subject and the address values.");
        recorder.ProviderAddresses.Should().BeEmpty();
    }

    [Fact]
    public async Task A_call_that_is_not_an_object_fails_saying_so()
    {
        var (answer, _) = await StepAsync(call: "EP");

        answer.GetProperty("error").GetString().Should().Be("Tributary's step takes one call on its 'call' input: the registration, the subject and the address values.");
    }

    [Fact]
    public async Task The_manifest_declares_one_call_in_and_its_outcome_out()
    {
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var ports = await client.GetFromJsonAsync<JsonElement>("/manifest");

        ports.GetRawText().Should().Be(
            """[{"portName":"call","direction":"in","type":"object","required":true,"collection":true},{"portName":"outcome","direction":"out","type":"object","required":false}]""");
    }

    [Fact]
    public async Task A_direct_request_is_still_answered_as_before()
    {
        var recorder = new Recorder();
        var subject = Guid.NewGuid();
        await using var factory = FactoryOver(recorder, () => Json("""{"regime":"steady"}"""));
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", CallFor(subject));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("success").GetBoolean().Should().BeTrue();
        body.GetProperty("written").GetProperty("flowRegime").GetString().Should().Be("steady");
        recorder.ObservedOn.Should().Equal(subject);
    }

    [Theory]
    [InlineData("not json at all", "application/json", HttpStatusCode.BadRequest)]
    [InlineData("", "application/json", HttpStatusCode.BadRequest)]
    [InlineData("""{"endpointName":"EP"}""", "text/plain", HttpStatusCode.UnsupportedMediaType)]
    public async Task A_direct_request_that_cannot_be_read_is_refused_as_before(string body, string mediaType, HttpStatusCode expected)
    {
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/handle", new StringContent(body, Encoding.UTF8, mediaType));

        response.StatusCode.Should().Be(expected);
    }
}
