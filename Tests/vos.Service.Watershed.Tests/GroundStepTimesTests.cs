using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Shared;
using vos.Service.Shared.DagNode;
using vos.Service.Watershed.Services;
using vos.Service.Watershed.Terrain;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Watershed.Tests;

public class GroundStepTimesTests
{
    private const string MyceliumUrl = "http://localhost:7243";
    private static readonly Guid Run = Guid.Parse("87850000-0000-0000-0000-0000000000aa");
    private static readonly Guid Node = Guid.Parse("87850000-0000-0000-0000-0000000000bb");
    private static readonly IReadOnlyList<Position> Border = MadeUpBorders.Rectangle(1700, 700);
    private static readonly string[] TilesTheBorderTouches = ["N39W033", "N39W030"];

    private static readonly TimeSpan ModelRead = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan HeightCall = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan ObservationWrite = TimeSpan.FromMilliseconds(11);
    private static readonly TimeSpan TileRead = TimeSpan.FromMilliseconds(5);
    private static readonly TimeSpan ReadBack = TimeSpan.FromMilliseconds(7);
    private static readonly TimeSpan ClockRead = TimeSpan.FromMilliseconds(2);
    private static readonly TimeSpan Fragment = TimeSpan.FromMilliseconds(40);

    private sealed class ClockMovingLogger(SteppingClock clock, TimeSpan eachWarning) : ILogger<WatershedNode>
    {
        public CapturingLogger<WatershedNode> Captured { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) clock.Advance(eachWarning);
            Captured.Log(logLevel, eventId, state, exception, formatter);
        }
    }

    private sealed class Step
    {
        public WatershedModel Model { get; init; } = new(Border);
        public SteppingClock Clock { get; } = new();
        public TimeSpan EachWarning { get; init; } = TimeSpan.Zero;
        public int ReadBacksUntilStored { get; init; } = 1;
        public bool CancelledDuringTheFirstReadBack { get; init; }
        public int ReadBacks { get; private set; }

        private readonly CancellationTokenSource _aborted = new();

        public async Task<(NodeResponse Response, IReadOnlyList<string> Lines)> RunAsync()
        {
            var factory = new PerCallHttpClientFactory(new MockHttpMessageHandler(Answer));
            var logger = new ClockMovingLogger(Clock, EachWarning);
            var heights = new GroundHeightReader(factory, NullLogger<GroundHeightReader>.Instance, MyceliumUrl, new ClockThatWaitsNoTime(), new LaunchToken("svc-jwt"));
            var node = new WatershedNode(factory, logger, MyceliumUrl, heights, new LandCoverReader(factory), Clock, new LaunchToken("svc-jwt"));
            var response = await node.HandleNodeAsync(JsonDocument.Parse(
                $$$"""{"runId":"{{{Run}}}","nodeId":"{{{Node}}}","params":{},"inputs":{"subject":"{{{WatershedModel.Study}}}"}}""").RootElement,
                PassedOnRequestDeadline.None(_aborted.Token), _aborted.Token);
            return (response, [.. logger.Captured.Lines.Where(line => line.StartsWith($"Ground step {Node}", StringComparison.Ordinal))]);
        }

        private HttpResponseMessage Answer(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.RequestUri.Host == WatershedModel.LandCoverHost)
                Clock.Advance(TileRead);
            else if (path == "/api/snapshots")
            {
                if (Model.Observed.Count == 0)
                    Clock.Advance(ModelRead);
                else
                {
                    ReadBacks++;
                    Clock.Advance(ReadBack);
                    if (CancelledDuringTheFirstReadBack)
                    {
                        _aborted.Cancel();
                        throw new OperationCanceledException(_aborted.Token);
                    }
                    if (ReadBacks == ReadBacksUntilStored)
                        foreach (var (thing, values) in Model.Observed)
                            foreach (var (property, value) in values)
                                Model.Holds(thing, property, value);
                }
            }
            else if (path == $"/api/endpoints/{OutsideCalls.FetcherSubdomain}")
                Clock.Advance(HeightCall);
            else if (path.EndsWith("/observations", StringComparison.Ordinal))
                Clock.Advance(ObservationWrite);
            else if (path == "/api/time")
                Clock.Advance(ClockRead);
            else if (path == "/api/model/fragment")
                Clock.Advance(Fragment);
            return Model.Answer(request);
        }
    }

    private static string Part(string part, TimeSpan taken, int calls) =>
        $"Ground step {Node} (run {Run}): {part} took {taken.TotalMilliseconds:0} ms over {calls} calls";

    private static string InAll(TimeSpan taken) => $"Ground step {Node} (run {Run}) took {taken.TotalMilliseconds:0} ms in all";

    private static byte TreesWestBareEast(Position centre) => centre.Longitude < MadeUpBorders.Longitude ? (byte)10 : (byte)60;

    private static WatershedModel CoveringLandWithStoresThatLag()
    {
        var model = new WatershedModel(Border) { DropsObservations = true };
        model.CoversLand(bindsTheRaster: true);
        foreach (var name in TilesTheBorderTouches)
            model.LandCoverTiles[name] = BuiltTile.Named(name).Over(Border, TreesWestBareEast).Build().Bytes;
        return model;
    }

    [Fact]
    public async Task Each_part_of_the_ground_step_is_logged_with_its_milliseconds_and_calls_and_the_step_with_its_total()
    {
        var step = new Step { Model = CoveringLandWithStoresThatLag(), ReadBacksUntilStored = 3 };

        var (response, lines) = await step.RunAsync();

        response.Result.Should().Be(NodeResponse.Succeeded, response.Error);
        var heightRequests = step.Model.HeightCalls;
        var writes = step.Model.Observed.Count;
        var tileReads = step.Model.LandCoverCalls;
        var pause = WatershedNode.ReadBackEvery;
        lines.Should().Equal(
            Part("the model read", 2 * ModelRead, 2),
            Part("the height calls", heightRequests * HeightCall, 2),
            Part("the observation writes", writes * ObservationWrite, writes),
            Part("the land cover read", tileReads * TileRead, 1),
            Part("the read back", 3 * ReadBack, 3),
            Part("the pauses between read backs", 2 * pause, 2),
            Part("the stamp", ClockRead + Fragment, 2),
            InAll(2 * ModelRead + heightRequests * HeightCall + writes * ObservationWrite + tileReads * TileRead + 3 * ReadBack + 2 * pause + ClockRead + Fragment));
    }

    [Fact]
    public async Task A_step_cancelled_during_its_read_back_logs_the_parts_that_finished_and_names_the_read_back_as_under_way()
    {
        var step = new Step { CancelledDuringTheFirstReadBack = true };

        var (response, lines) = await step.RunAsync();

        response.Result.Should().Be(NodeResponse.Failed);
        var heightRequests = step.Model.HeightCalls;
        var writes = step.Model.Observed.Count;
        lines.Should().Equal(
            Part("the model read", 2 * ModelRead, 2),
            Part("the height calls", heightRequests * HeightCall, 2),
            Part("the observation writes", writes * ObservationWrite, writes),
            $"Ground step {Node} (run {Run}): the read back took {ReadBack.TotalMilliseconds:0} ms over 0 finished calls and was under way when the step stopped",
            InAll(2 * ModelRead + heightRequests * HeightCall + writes * ObservationWrite + ReadBack));
    }

    [Fact]
    public async Task A_study_with_no_border_logs_only_its_model_read_and_its_stamp()
    {
        var step = new Step { Model = new WatershedModel(border: null) };

        var (response, lines) = await step.RunAsync();

        response.Result.Should().Be(NodeResponse.Succeeded, response.Error);
        lines.Should().Equal(
            Part("the model read", ModelRead, 1),
            Part("the stamp", ClockRead + Fragment, 2),
            InAll(ModelRead + ClockRead + Fragment));
    }

    [Fact]
    public async Task Time_spent_between_calls_counts_in_the_total_and_in_no_part()
    {
        var computing = TimeSpan.FromMilliseconds(500);
        var model = new WatershedModel(Border, bindsAHeightsSource: false);
        model.HoldsALayerFeature("""{"type":"Point","coordinates":[-30,40]}""");
        var step = new Step { Model = model, EachWarning = computing };

        var (response, lines) = await step.RunAsync();

        response.Result.Should().Be(NodeResponse.Succeeded, response.Error);
        lines.Should().Equal(
            Part("the model read", ModelRead, 1),
            Part("the stamp", ClockRead + Fragment, 2),
            InAll(ModelRead + ClockRead + Fragment + computing));
    }
}
