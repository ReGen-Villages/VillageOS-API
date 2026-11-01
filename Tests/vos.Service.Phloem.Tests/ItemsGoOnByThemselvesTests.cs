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
