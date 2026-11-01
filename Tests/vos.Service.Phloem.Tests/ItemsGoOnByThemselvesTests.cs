using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Shared;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Phloem.Tests;

public class ItemsGoOnByThemselvesTests
{
    private static readonly JsonElement Readings = JsonSerializer.SerializeToElement(new { readings = new[] { "North", "South", "East" } });

    private static NodeCallResult Echo(JsonElement envelope) =>
        new(200, $"{{\"result\":\"success\",\"outputs\":{{\"out\":{ItemOf(envelope).GetRawText()}}}}}");

    private static NodeCallResult Refuse() => new(200, "{\"result\":\"fail\",\"error\":\"reading refused\"}");

    private static JsonElement ItemOf(JsonElement envelope) => envelope.GetProperty("inputs").GetProperty("in");

    private static string ReadingOf(JsonElement envelope) => ItemOf(envelope).GetString()!;

    private static TaskCompletionSource<NodeCallResult> Held() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static (FakeGateway Gateway, PipelineExecutor Executor) Arrange(
        (GraphFixture Fixture, Guid PipelineId) drawing, Func<string, JsonElement, Task<NodeCallResult>> answer)
    {
        var gateway = new FakeGateway(drawing.Fixture.Build()) { OnCallAsync = answer };
        return (gateway, new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock()));
    }

    private static List<string> Called(FakeGateway gateway, string subdomain)
    {
        lock (gateway.Called)
            return gateway.Envelopes.Where(call => call.Subdomain == subdomain).Select(call => ReadingOf(call.Envelope)).ToList();
    }

    [Fact]
    public async Task An_item_that_answers_early_reaches_the_next_node_while_a_slower_item_is_still_calling()
    {
        var drawing = TestGraphs.ItemsGoOnPipeline();
        var north = Held();
        var (gateway, executor) = Arrange(drawing, (subdomain, envelope) =>
            subdomain == "check" && ReadingOf(envelope) == "North" ? north.Task : Task.FromResult(Echo(envelope)));

        var running = executor.RunAsync(drawing.PipelineId, Readings, CancellationToken.None);
        try
        {
            await Settle.UntilAsync(() => Called(gateway, "record").Contains("South"), "South reaches 'Record level' while North is still being checked");
        }
        finally
        {
            north.TrySetResult(Echo(JsonSerializer.SerializeToElement(new { inputs = new { @in = "North" } })));
        }
        var result = await running;

        result.Success.Should().BeTrue();
        Called(gateway, "record").Should().BeEquivalentTo("North", "South", "East");
    }

    [Fact]
    public async Task No_item_of_a_node_set_to_fail_as_a_whole_goes_on_before_every_item_has_answered()
    {
        var drawing = TestGraphs.ItemsGoOnPipeline(onItemError: "fail");
        var north = Held();
        var (gateway, executor) = Arrange(drawing, (subdomain, envelope) =>
            subdomain == "check" && ReadingOf(envelope) == "North" ? north.Task : Task.FromResult(Echo(envelope)));

        var running = executor.RunAsync(drawing.PipelineId, Readings, CancellationToken.None);
        await Settle.UntilAsync(() => Called(gateway, "check").Count == 3, "every reading is being checked");
        await Settle.BeforeAssertingAbsenceAsync(TimeSpan.FromMilliseconds(200));
        var recordedWhileNorthIsChecked = Called(gateway, "record");
        north.TrySetResult(Echo(JsonSerializer.SerializeToElement(new { inputs = new { @in = "North" } })));
        var result = await running;

        recordedWhileNorthIsChecked.Should().BeEmpty();
        result.Success.Should().BeTrue();
        Called(gateway, "record").Should().BeEquivalentTo("North", "South", "East");
    }

    [Fact]
    public async Task Items_meet_on_a_wire_that_carries_a_transform_so_it_sees_the_whole_list()
    {
        var drawing = TestGraphs.ItemsGoOnPipeline(transform: "$reverse($)");
        var north = Held();
        var (gateway, executor) = Arrange(drawing, (subdomain, envelope) =>
            subdomain == "check" && ReadingOf(envelope) == "North" ? north.Task : Task.FromResult(Echo(envelope)));

        var running = executor.RunAsync(drawing.PipelineId, Readings, CancellationToken.None);
        await Settle.UntilAsync(() => Called(gateway, "check").Count == 3, "every reading is being checked");
        await Settle.BeforeAssertingAbsenceAsync(TimeSpan.FromMilliseconds(200));
        var recordedWhileNorthIsChecked = Called(gateway, "record");
        north.TrySetResult(Echo(JsonSerializer.SerializeToElement(new { inputs = new { @in = "North" } })));
        var result = await running;

        recordedWhileNorthIsChecked.Should().BeEmpty();
        result.Success.Should().BeTrue();
        gateway.Envelopes.Where(call => call.Subdomain == "record")
            .Select(call => (ReadingOf(call.Envelope), call.Envelope.GetProperty("index").GetInt32()))
            .Should().BeEquivalentTo([("East", 0), ("South", 1), ("North", 2)]);
    }

    [Theory]
    [InlineData(TestGraphs.AlsoFillingRecordLevel.ASecondWire, false)]
    [InlineData(TestGraphs.AlsoFillingRecordLevel.AParameter, false)]
    [InlineData(TestGraphs.AlsoFillingRecordLevel.AChoice, false)]
    [InlineData(TestGraphs.AlsoFillingRecordLevel.Nothing, true)]
    public async Task Items_meet_where_the_next_node_takes_more_than_one_value_per_item(TestGraphs.AlsoFillingRecordLevel alsoFilling, bool checkAnswersAList)
    {
        var drawing = TestGraphs.ItemsGoOnPipeline(alsoFilling: alsoFilling, checkAnswersAList: checkAnswersAList);
        var north = Held();
        var (gateway, executor) = Arrange(drawing, (subdomain, envelope) =>
            subdomain == "check" && ItemOf(envelope).GetString() == "North" ? north.Task : Task.FromResult(Echo(envelope)));
        var parameters = JsonSerializer.SerializeToElement(new { readings = new[] { "North", "South", "East" }, spare = "Spare" });

        var running = executor.RunAsync(drawing.PipelineId, parameters, CancellationToken.None);
        await Settle.UntilAsync(() => Called(gateway, "check").Count == 3, "every reading is being checked");
        await Settle.BeforeAssertingAbsenceAsync(TimeSpan.FromMilliseconds(200));
        var recordedWhileNorthIsChecked = gateway.Envelopes.Count(call => call.Subdomain == "record");
        north.TrySetResult(Echo(JsonSerializer.SerializeToElement(new { inputs = new { @in = "North" } })));
        await running;

        recordedWhileNorthIsChecked.Should().Be(0);
        gateway.Envelopes.Should().Contain(call => call.Subdomain == "record");
    }

    [Fact]
    public async Task A_list_answered_by_a_node_that_ran_once_reaches_the_next_node_as_items_numbered_afresh()
    {
        var drawing = TestGraphs.ItemsGoOnPipeline(readingsReachCheck: false);
        var (gateway, executor) = Arrange(drawing, (subdomain, envelope) => Task.FromResult(subdomain == "check"
            ? new NodeCallResult(200, "{\"result\":\"success\",\"outputs\":{\"out\":[\"North\",\"South\",\"East\"]}}")
            : Echo(envelope)));

        var result = await executor.RunAsync(drawing.PipelineId, Readings, CancellationToken.None);

        result.Error.Should().BeNull();
        gateway.CalledSoFar().Count(called => called == "check").Should().Be(1);
        gateway.Envelopes.Where(call => call.Subdomain == "record")
            .Select(call => (ReadingOf(call.Envelope), call.Envelope.GetProperty("index").GetInt32()))
            .Should().BeEquivalentTo([("North", 0), ("South", 1), ("East", 2)]);
        gateway.ItemRecordsOf("Record level").Should().Equal((0, 3), (1, 3), (2, 3));
    }

    [Fact]
    public async Task An_item_going_on_by_itself_carries_the_part_its_wire_picks_out()
    {
        var drawing = TestGraphs.ItemsGoOnPipeline(fromPath: "level");
        var (gateway, executor) = Arrange(drawing, (subdomain, envelope) => Task.FromResult(subdomain == "check"
            ? new NodeCallResult(200, $"{{\"result\":\"success\",\"outputs\":{{\"out\":{{\"level\":\"{ReadingOf(envelope)} high\"}}}}}}")
            : Echo(envelope)));

        await executor.RunAsync(drawing.PipelineId, Readings, CancellationToken.None);

        Called(gateway, "record").Should().BeEquivalentTo("North high", "South high", "East high");
    }

    [Fact]
    public async Task A_node_set_to_fail_as_a_whole_that_failed_sends_on_only_its_failed_items()
    {
        var drawing = TestGraphs.ItemsGoOnPipeline(onItemError: "fail");
        var (gateway, executor) = Arrange(drawing, (subdomain, envelope) =>
            Task.FromResult(subdomain == "check" && ReadingOf(envelope) == "South" ? Refuse() : Echo(envelope)));

        await executor.RunAsync(drawing.PipelineId, Readings, CancellationToken.None);

        Called(gateway, "record").Should().BeEmpty();
        gateway.ItemRecordsOf("Tell operator").Should().Equal((1, 3));
    }

    [Fact]
    public async Task A_node_waiting_for_its_first_item_holds_no_calling_place_so_other_nodes_run_meanwhile()
    {
        var drawing = TestGraphs.ItemsGoOnPipeline(springPathBeside: true);
        var (spring, readings) = (Held(), Held());
        var gateway = new FakeGateway(drawing.Fixture.Build())
        {
            OnCallAsync = (subdomain, envelope) => subdomain switch
            {
                "spring" => spring.Task,
                "check" => readings.Task,
                _ => Task.FromResult(new NodeCallResult(200, "{\"result\":\"success\",\"outputs\":{\"out\":\"level\"}}")),
            },
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock(), maxConcurrency: 2);

        var running = executor.RunAsync(drawing.PipelineId, Readings, CancellationToken.None);
        try
        {
            await Settle.UntilAsync(() => gateway.CalledSoFar().Count(called => called == "check") == 2, "two readings are being checked");
            await Settle.BeforeAssertingAbsenceAsync(TimeSpan.FromMilliseconds(100));
            spring.TrySetResult(new NodeCallResult(200, "{\"result\":\"success\",\"outputs\":{\"out\":\"spring\"}}"));
            await Settle.UntilAsync(() => gateway.CalledSoFar().Contains("level"), "'Read level' runs while the readings are still being checked");
        }
        finally
        {
            spring.TrySetResult(new NodeCallResult(200, "{\"result\":\"success\",\"outputs\":{\"out\":\"spring\"}}"));
            readings.TrySetResult(new NodeCallResult(200, "{\"result\":\"success\",\"outputs\":{\"out\":\"level\"}}"));
        }
        (await running).Success.Should().BeTrue();
    }

    [Fact]
    public async Task A_node_gathers_what_its_items_answered_in_item_order_whatever_order_they_answered_in()
    {
        var drawing = TestGraphs.ItemsGoOnPipeline();
        var north = Held();
        var (gateway, executor) = Arrange(drawing, (subdomain, envelope) =>
            subdomain == "check" && ReadingOf(envelope) == "North" ? north.Task : Task.FromResult(Echo(envelope)));

        var running = executor.RunAsync(drawing.PipelineId, Readings, CancellationToken.None);
        await Settle.UntilAsync(() => gateway.ItemRecordsOf("Record level").Count == 2, "South and East reach 'Record level' before North");
        north.TrySetResult(Echo(JsonSerializer.SerializeToElement(new { inputs = new { @in = "North" } })));
        var result = await running;

        result.Nodes.Single(node => node.Name == "Record level").Outputs["out"].EnumerateArray().Select(reading => reading.GetString())
            .Should().Equal("North", "South", "East");
    }

    [Fact]
    public async Task A_node_fed_items_one_at_a_time_that_no_item_reaches_is_recorded_not_taken()
    {
        var drawing = TestGraphs.ItemsGoOnPipeline();
        var (gateway, executor) = Arrange(drawing, (subdomain, envelope) => Task.FromResult(subdomain == "check" ? Refuse() : Echo(envelope)));

        var result = await executor.RunAsync(drawing.PipelineId, Readings, CancellationToken.None);

        result.Nodes.Single(node => node.Name == "Record level").Status.Should().Be(RunStatus.NotTaken);
        Called(gateway, "record").Should().BeEmpty();
        gateway.ItemRecordsOf("Tell operator").Should().BeEquivalentTo([(0, 3), (1, 3), (2, 3)]);
    }

    [Fact]
    public async Task A_node_given_an_empty_list_still_succeeds_over_no_items()
    {
        var drawing = TestGraphs.ResultPathsPipeline(perItem: true, onItemError: "continue");
        var gateway = new FakeGateway(drawing.Fixture.Build());
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var result = await executor.RunAsync(drawing.PipelineId, JsonSerializer.SerializeToElement(new { readings = Array.Empty<string>() }), CancellationToken.None);

        result.Nodes.Single(node => node.Name == "Check reading").Status.Should().Be(RunStatus.Succeeded);
        result.Success.Should().BeFalse();
        result.Error.Should().Be("'Check reading' succeeded, and no success wire leaves it.");
    }

    [Fact]
    public async Task A_stop_starts_no_more_items_on_any_node()
    {
        var drawing = TestGraphs.ItemsGoOnPipeline();
        var north = Held();
        var gateway = new FakeGateway(drawing.Fixture.Build())
        {
            OnCallAsync = (subdomain, envelope) =>
                subdomain == "check" && ReadingOf(envelope) == "North" ? north.Task : Task.FromResult(Echo(envelope)),
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock(), maxConcurrency: 1);
        var runId = Guid.NewGuid();

        var running = executor.RunAsync(drawing.PipelineId, Readings, CancellationToken.None, runId: runId);
        await Settle.UntilAsync(() => Called(gateway, "check").Contains("North"), "North is being checked while South and East wait their turn");
        var stopping = executor.StopAsync(Guid.NewGuid(), runId, CancellationToken.None);
        north.TrySetResult(Echo(JsonSerializer.SerializeToElement(new { inputs = new { @in = "North" } })));
        await running;
        await stopping;

        Called(gateway, "check").Should().Equal("North");
        Called(gateway, "record").Should().BeEmpty();
        gateway.RunStatusOf(runId).Should().Be(RunStatus.Cancelled);
    }

    [Fact]
    public async Task An_item_keeps_its_number_on_every_node_it_reaches()
    {
        var drawing = TestGraphs.ItemsGoOnPipeline();
        var (gateway, executor) = Arrange(drawing, (_, envelope) =>
            Task.FromResult(ReadingOf(envelope) == "South" ? Refuse() : Echo(envelope)));

        var result = await executor.RunAsync(drawing.PipelineId, Readings, CancellationToken.None);

        result.Success.Should().BeTrue();
        gateway.Envelopes.Where(call => call.Subdomain == "record")
            .Select(call => (ReadingOf(call.Envelope), call.Envelope.GetProperty("index").GetInt32()))
            .Should().BeEquivalentTo([("North", 0), ("East", 2)]);
        gateway.ItemRecordsOf("Record level").Should().BeEquivalentTo([(0, 3), (2, 3)]);
    }
}
