using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace vos.Service.Tributary.Tests;

public class HistoryChangedStepTests
{
    private static async Task<(JsonElement Answer, List<string> Asked)> StepAsync(
        string? step, Dictionary<string, object?> inputs, Func<HttpResponseMessage>? broker = null)
    {
        var asked = new List<string>();
        await using var factory = new TributaryWebApplicationFactory();
        factory.HandlerCallback = request =>
        {
            if (request.Method == HttpMethod.Post) asked.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            return broker?.Invoke() ?? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"series":24}""", Encoding.UTF8, "application/json"),
            };
        };
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new
        {
            runId = Guid.NewGuid(),
            nodeId = Guid.NewGuid(),
            inputs,
            @params = step is null ? new Dictionary<string, object>() : new Dictionary<string, object> { ["step"] = step },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "a step's failure is said in its answer, never in its status");
        return (await response.Content.ReadFromJsonAsync<JsonElement>(), asked);
    }

    private static Dictionary<string, object?> Subject(object subject) => new() { ["subject"] = subject };

    private static HttpResponseMessage Refusal(HttpStatusCode status, string error, TimeSpan? retryAfter = null)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { error }), Encoding.UTF8, "application/json"),
        };
        if (retryAfter is { } wait) response.Headers.RetryAfter = new RetryConditionHeaderValue(wait);
        return response;
    }

    [Fact]
    public async Task A_history_changed_box_asks_for_its_subjects_history_to_be_judged_and_passes_the_subject_on()
    {
        var site = Guid.NewGuid();

        var (answer, asked) = await StepAsync("historyChanged", Subject(site.ToString()));

        answer.GetProperty("result").GetString().Should().Be("success");
        asked.Should().Equal($"POST /api/things/{site}/history/changed");
        answer.GetProperty("outputs").GetProperty("subject").GetString().Should().Be(site.ToString());
    }

    [Fact]
    public async Task A_subject_given_as_an_object_naming_its_id_is_judged_and_passed_on_as_given()
    {
        var site = Guid.NewGuid();

        var (answer, asked) = await StepAsync("historyChanged", Subject(new { id = site, name = "Willow Bend" }));

        answer.GetProperty("result").GetString().Should().Be("success");
        asked.Should().Equal($"POST /api/things/{site}/history/changed");
        answer.GetProperty("outputs").GetProperty("subject").GetProperty("name").GetString().Should().Be("Willow Bend");
    }

    [Fact]
    public async Task A_thing_the_broker_does_not_find_fails_the_box_for_good()
    {
        var (answer, _) = await StepAsync("historyChanged", Subject(Guid.NewGuid().ToString()),
            () => Refusal(HttpStatusCode.NotFound, "Thing not found"));

        answer.GetProperty("result").GetString().Should().Be("fail");
        answer.GetProperty("error").GetString().Should().Contain("404: Thing not found");
        answer.TryGetProperty("retry", out _).Should().BeFalse();
    }

    [Fact]
    public async Task A_full_queue_fails_the_box_so_it_may_be_tried_again_after_the_brokers_wait()
    {
        var (answer, _) = await StepAsync("historyChanged", Subject(Guid.NewGuid().ToString()),
            () => Refusal(HttpStatusCode.ServiceUnavailable, "The readings queue is full", TimeSpan.FromSeconds(2)));

        answer.GetProperty("result").GetString().Should().Be("fail");
        answer.GetProperty("retry").GetProperty("afterSeconds").GetDouble().Should().Be(2);
    }

    [Fact]
    public async Task A_box_given_no_subject_is_refused_naming_the_port()
    {
        var (answer, asked) = await StepAsync("historyChanged", new Dictionary<string, object?>());

        answer.GetProperty("result").GetString().Should().Be("fail");
        answer.GetProperty("error").GetString().Should().Contain("'subject'");
        asked.Should().BeEmpty();
    }

    [Fact]
    public async Task A_box_naming_a_step_tributary_does_not_offer_is_refused_naming_the_steps_it_does()
    {
        var (answer, asked) = await StepAsync("judgeEverything", Subject(Guid.NewGuid().ToString()));

        answer.GetProperty("result").GetString().Should().Be("fail");
        answer.GetProperty("error").GetString().Should().Contain("fetch").And.Contain("historyChanged");
        asked.Should().BeEmpty();
    }

    [Theory]
    [InlineData("historyChanged", "subject", "subject")]
    [InlineData("fetch", "call", "outcome")]
    public async Task Each_step_declares_its_ports_and_its_step_setting(string step, string input, string output)
    {
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var manifest = await client.GetFromJsonAsync<JsonElement>($"/manifest/{step}");

        manifest.GetProperty("ports").EnumerateArray()
            .Select(port => (port.GetProperty("portName").GetString(), port.GetProperty("direction").GetString()))
            .Should().Equal((input, "in"), (output, "out"));
        var setting = manifest.GetProperty("settings").EnumerateArray().Should().ContainSingle().Subject;
        setting.GetProperty("name").GetString().Should().Be("step");
        setting.GetProperty("default").GetString().Should().Be(step);
        (await client.GetAsync("/manifest/judgeEverything")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
