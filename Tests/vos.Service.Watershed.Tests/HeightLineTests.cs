using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using vos.Service.Shared;
using vos.Service.Shared.DagNode;
using vos.Service.Watershed.Services;
using vos.Service.Watershed.Terrain;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Watershed.Tests;

public class HeightLineTests
{
    private const string MyceliumUrl = "http://localhost:7243";
    private static readonly IReadOnlyList<Position> Border = MadeUpBorders.Rectangle(1700, 700);
    private static readonly TimeSpan BrokersLimit = TimeSpan.FromSeconds(30);

    private static int CallsOfOneStep =>
        GroundHeightReader.CallsFor(HeightGridLayout.Over(Border).PointsRowByRowFromTheSouthWest.Count)
        + GroundHeightReader.CallsFor(2 * ProfileLines.PointsAlongALine);

    private static string Envelope() =>
        $$$"""{"runId":"{{{Guid.NewGuid()}}}","nodeId":"{{{Guid.NewGuid()}}}","params":{},"inputs":{"subject":"{{{WatershedModel.Study}}}"}}""";

    private sealed class Step
    {
        private readonly PerCallHttpClientFactory _factory;
        private readonly ClockThatWaitsNoTime _clock = new();
        private readonly FakeTimeProvider _deadlineClock = new();
        private readonly GroundHeightReader _heights;

        public TimeSpan ModelReadTakes { get; init; } = TimeSpan.Zero;

        public Step(WatershedModel model, int callsAhead)
        {
            _factory = new PerCallHttpClientFactory(new MockHttpMessageHandler(model.Answer));
            _heights = new GroundHeightReader(_factory, NullLogger<GroundHeightReader>.Instance, MyceliumUrl, _clock, new LaunchToken("svc-jwt"));
            _heights.Join(callsAhead, timeLeft: null);
            model.OnSnapshot = () => _deadlineClock.Advance(ModelReadTakes);
        }

        public async Task<NodeResponse> RunAsync()
        {
            var node = new WatershedNode(_factory, NullLogger<WatershedNode>.Instance, MyceliumUrl, _heights,
                new LandCoverReader(_factory), _clock, new LaunchToken("svc-jwt"));
            using var deadline = PassedOnRequestDeadline.Start(BrokersLimit, _deadlineClock, CancellationToken.None);
            return await node.HandleNodeAsync(JsonDocument.Parse(Envelope()).RootElement, deadline);
        }
    }

    [Fact]
    public async Task A_step_behind_a_line_too_long_for_its_time_left_answers_at_once_to_try_again_and_asks_and_writes_nothing()
    {
        var model = new WatershedModel(Border);

        var response = await new Step(model, callsAhead: 40).RunAsync();

        response.Result.Should().Be(NodeResponse.Failed);
        response.Error.Should().StartWith($"The height line is full: 40 calls are ahead of this step's {CallsOfOneStep}");
        JsonSerializer.Serialize(response).Should().Contain("\"retry\":{\"afterSeconds\":40}");
        model.HeightCalls.Should().Be(0);
        model.Observed.Should().BeEmpty();
        model.Fragments.Should().BeEmpty();
    }

    [Fact]
    public async Task A_site_holding_its_grid_and_profiles_joins_no_line_so_a_long_one_does_not_stop_it()
    {
        var first = new WatershedModel(Border);
        await new Step(first, callsAhead: 0).RunAsync();
        var written = first.Observed.Single(batch => batch.Thing == WatershedModel.Site).Values;
        var again = new WatershedModel(Border);
        foreach (var grid in new[] { "terrainHeightGrid", "terrainProfileWestToEast", "terrainProfileSouthToNorth" })
            again.Holds(WatershedModel.Site, grid, written[grid]);

        var response = await new Step(again, callsAhead: 40).RunAsync();

        response.Result.Should().Be(NodeResponse.Succeeded, response.Error);
        again.HeightCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(12, false)]
    public async Task The_time_left_counts_from_when_the_request_arrived(int modelReadSeconds, bool joins)
    {
        var model = new WatershedModel(Border);
        var callsAhead = (int)(BrokersLimit - PassedOnRequestDeadline.MarginForTheAnswer).TotalSeconds - CallsOfOneStep - 5;

        var response = await new Step(model, callsAhead) { ModelReadTakes = TimeSpan.FromSeconds(modelReadSeconds) }.RunAsync();

        if (joins)
            response.Result.Should().Be(NodeResponse.Succeeded, response.Error);
        else
            response.Error.Should().StartWith("The height line is full");
    }

    private sealed class WatershedGivenTheBrokersLimit : ServiceWebApplicationFactory<Program>
    {
        protected override void AddSettings(IWebHostBuilder builder) => builder.UseSetting("PassedOnRequestLimitSeconds", "5");
    }

    [Fact]
    public async Task The_node_host_gives_each_request_the_limit_it_was_started_with()
    {
        var model = new WatershedModel(Border);
        using var heightsAsked = new SemaphoreSlim(0);
        using var answerTheHeights = new ManualResetEventSlim();
        var heightCalls = 0;
        await using var factory = new WatershedGivenTheBrokersLimit
        {
            HandlerCallback = request =>
            {
                if (request.RequestUri!.AbsolutePath != $"/api/endpoints/{OutsideCalls.FetcherSubdomain}" || Interlocked.Increment(ref heightCalls) > 1)
                    return model.Answer(request);
                heightsAsked.Release();
                answerTheHeights.Wait(Settle.Ceiling);
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            },
        };
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var first = client.PostAsync("/handle", new StringContent(Envelope(), Encoding.UTF8, "application/json"));
        await Settle.ForAsync(heightsAsked.WaitAsync(), "the first step asks for its heights");
        try
        {
            var second = client.PostAsync("/handle", new StringContent(Envelope(), Encoding.UTF8, "application/json"));
            await Settle.ForAsync(second, "the second step answers without waiting behind the first");

            var answer = await (await second).Content.ReadFromJsonAsync<JsonElement>();
            answer.GetProperty("error").GetString().Should().StartWith($"The height line is full: {CallsOfOneStep} calls are ahead");
            answer.GetProperty("retry").GetProperty("afterSeconds").GetDouble().Should().Be(CallsOfOneStep);
        }
        finally
        {
            answerTheHeights.Set();
            await first;
        }
    }
}
