using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using vos.Mcp.Tools.Process;
using vos.Tests.Shared;
using Xunit;

namespace vos.Mcp.Tests;

public class ShowCallsTests
{
    private const string Tool = "show_calls";

    private readonly SelectionFromASeed _model = new(TestSeeds.Catchments);
    private readonly Guid _record;
    private readonly Queue<string> _statesAnswered = new();
    private string _lastState = "Pending";
    private bool _bindsAConnectionKind = true;

    public ShowCallsTests()
    {
        _record = _model.Relate(Catchment, _model.IdOf("handledBy"), _model.IdOf("surveysCatchment"));
    }

    private Guid Catchment => _model.IdOf("Ålvik south slope");

    private HttpResponseMessage Mycelium(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        var query = Uri.UnescapeDataString(request.RequestUri.Query);

        if (path == "/api/things" && query.Contains("boundBy="))
            return _bindsAConnectionKind && query.Contains(ServiceCalls.ConnectionRole.ToString())
                ? ServerUnderTest.Json($$"""{"Id":"{{_model.IdOf("PlatformServiceConnection")}}","Name":"PlatformServiceConnection"}""")
                : new HttpResponseMessage(HttpStatusCode.NotFound);

        if (path == "/api/things" && query.Contains("name=North pond"))
            return ServerUnderTest.Json(
                $$"""{"error":"ambiguous","matchingIds":["{{_model.IdOf("North pond")}}","{{_model.IdOf("South pond")}}"]}""",
                HttpStatusCode.Conflict);
        if (path == "/api/things" && query.Contains("name=Ålvik south slope"))
            return ServerUnderTest.Json($$"""{"Id":"{{Catchment}}","Name":"Ålvik south slope"}""");
        if (path == "/api/things" && query.Contains("name="))
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        if (path == $"/api/things/{Catchment}")
            return ServerUnderTest.Json($$"""{"Id":"{{Catchment}}","Name":"Ålvik south slope"}""");

        if (path == "/api/snapshots")
            return ServerUnderTest.Json(JsonSerializer.Serialize(new { watermark = 1, snapshot = _model.Selection() }));

        if (path == $"/api/relationships/{_record}")
        {
            if (_statesAnswered.TryDequeue(out var next)) _lastState = next;
            return ServerUnderTest.Json(JsonSerializer.Serialize(new
            {
                Id = _record,
                Properties = new Dictionary<string, string>
                {
                    ["__CallState"] = _lastState, ["__CallRecordState"] = "CatchmentAwaitingSurvey",
                },
            }));
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private Task<ServerUnderTest> ServerAsync(string role = "viewer", ClockThatWaitsNoTime? clock = null) =>
        ServerUnderTest.StartAsync(
            ServerUnderTest.MyceliumFor(() => role, Mycelium),
            configure: services => { if (clock is not null) services.AddSingleton<TimeProvider>(clock); });

    private static async Task<CallToolResult> ShowAsync(ServerUnderTest server, params (string Name, object Value)[] arguments) =>
        await server.Client.CallToolAsync(Tool, arguments.ToDictionary(each => each.Name, each => (object?)each.Value));

    [Fact]
    public async Task It_is_listed_for_a_viewer_where_the_model_binds_a_connection_kind()
    {
        await using var server = await ServerAsync();

        (await server.ListedToolNamesAsync()).Should().Contain(Tool);
    }

    [Fact]
    public async Task It_is_not_listed_where_the_model_binds_no_connection_kind_whatever_else_it_binds()
    {
        _bindsAConnectionKind = false;
        await using var server = await ServerAsync();

        (await server.ListedToolNamesAsync()).Should().NotContain(Tool);
        (await ShowAsync(server, ("thing", Catchment.ToString()))).IsError.Should().BeTrue();
    }

    [Fact]
    public async Task It_answers_the_call_in_words_and_as_data()
    {
        await using var server = await ServerAsync();

        var answer = await ShowAsync(server, ("thing", "Ålvik south slope"));

        answer.IsError.Should().NotBe(true);
        ServerUnderTest.TextOf(answer).Should().Contain("- surveysCatchment, service Echo service: Pending.");
        var call = answer.StructuredContent!.Value.GetProperty("calls")[0];
        call.GetProperty("state").GetString().Should().Be("Pending");
        call.GetProperty("record").GetGuid().Should().Be(_record);
        call.GetProperty("keptAcrossARestart").GetBoolean().Should().BeTrue();
        answer.StructuredContent.Value.GetProperty("waited").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_name_no_Thing_holds_is_refused_in_the_tools_words()
    {
        await using var server = await ServerAsync();

        var answer = await ShowAsync(server, ("thing", "No such catchment"));

        answer.IsError.Should().BeTrue();
        ServerUnderTest.TextOf(answer).Should().Be($"An error occurred invoking '{Tool}': No Thing is named 'No such catchment'.");
    }

    [Fact]
    public async Task A_name_several_Things_hold_is_refused_naming_each_candidate_with_its_kind()
    {
        await using var server = await ServerAsync();

        var answer = await ShowAsync(server, ("thing", "North pond"));

        answer.IsError.Should().BeTrue();
        ServerUnderTest.TextOf(answer).Should().Contain("More than one Thing is named 'North pond'")
            .And.Contain($"{_model.IdOf("North pond")} (a Reservoir)")
            .And.Contain($"{_model.IdOf("South pond")} (a Reservoir)");
    }

    [Fact]
    public async Task A_wait_reads_again_until_no_call_is_still_going_and_says_so()
    {
        _statesAnswered.Enqueue("Pending");
        _statesAnswered.Enqueue("Sent");
        _statesAnswered.Enqueue("Completed");
        var clock = new ClockThatWaitsNoTime();
        await using var server = await ServerAsync(clock: clock);

        var answer = await ShowAsync(server, ("thing", Catchment.ToString()), ("wait_seconds", 60));

        var waited = answer.StructuredContent!.Value.GetProperty("waited");
        waited.GetProperty("settled").GetBoolean().Should().BeTrue();
        answer.StructuredContent.Value.GetProperty("calls")[0].GetProperty("state").GetString().Should().Be("Completed");
        clock.Waits.Should().Equal(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        ServerUnderTest.TextOf(answer).Should().Contain("No call was still going after");
    }

    [Fact]
    public async Task A_wait_that_reaches_its_limit_says_what_it_saw_last()
    {
        var clock = new ClockThatAdvancesAsItWaits();
        await using var server = await ServerUnderTest.StartAsync(
            ServerUnderTest.MyceliumFor(() => "viewer", Mycelium),
            configure: services => services.AddSingleton<TimeProvider>(clock));

        var answer = await ShowAsync(server, ("thing", Catchment.ToString()), ("wait_seconds", 5));

        var waited = answer.StructuredContent!.Value.GetProperty("waited");
        waited.GetProperty("settled").GetBoolean().Should().BeFalse();
        waited.GetProperty("sawLast").GetString().Should().Be("surveysCatchment: Pending");
        clock.Waited.Should().Be(TimeSpan.FromSeconds(4), "a third wait of two seconds would pass the limit of five");
        ServerUnderTest.TextOf(answer).Should().Contain("Still going after 4 seconds; last seen: surveysCatchment: Pending.");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(121)]
    public async Task A_wait_outside_its_bounds_is_refused_before_anything_is_read(int seconds)
    {
        await using var server = await ServerAsync();

        var answer = await ShowAsync(server, ("thing", Catchment.ToString()), ("wait_seconds", seconds));

        answer.IsError.Should().BeTrue();
        ServerUnderTest.TextOf(answer).Should().Contain($"wait_seconds is from 0 to 120; {seconds} was given.");
        server.Mycelium.Requests.Should().NotContain(request => request.RequestUri!.AbsolutePath == "/api/snapshots");
    }
}

public sealed class ClockThatAdvancesAsItWaits : TimeProvider
{
    private long _ticks;

    public TimeSpan Waited => TimeSpan.FromTicks(Interlocked.Read(ref _ticks));

    public override long GetTimestamp() => Interlocked.Read(ref _ticks);

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Interlocked.Add(ref _ticks, dueTime.Ticks);
        _ = Task.Run(() => callback(state));
        return new NothingToStop();
    }

    private sealed class NothingToStop : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
