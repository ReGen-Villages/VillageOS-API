using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Shared;
using Xunit;

namespace vos.Service.Phloem.Tests;

public class BlockResultsTests
{
    private static NodeCallResult Answer(string json) => new(200, json);

    private static (FakeGateway Gateway, PipelineExecutor Executor) Arrange(TestGraphs.ResultPathsFixture drawn, Func<string, JsonElement, NodeCallResult> onCall)
    {
        var gateway = new FakeGateway(drawn.Fixture.Build()) { OnCall = onCall };
        return (gateway, new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock()));
    }

    private static JsonElement Readings(object readings) => JsonSerializer.SerializeToElement(new { readings });

    private static IEnumerable<JsonElement> InputsSentTo(FakeGateway gateway, string subdomain) =>
        gateway.Envelopes.Where(sent => sent.Subdomain == subdomain).Select(sent => sent.Envelope.GetProperty("inputs").GetProperty("in"));

    private static string StatusOf(PipelineRunResult run, GraphThing node) => run.Nodes.Single(n => n.NodeId == node.Id).Status;

    [Fact]
    public async Task A_null_a_block_answers_reaches_the_next_node_as_null()
    {
        var drawn = TestGraphs.ResultPathsPipeline();
        var (gateway, executor) = Arrange(drawn, (_, _) => Answer("""{"result":"success","outputs":{"out":null}}"""));

        var run = await executor.RunAsync(drawn.PipelineId, Readings("north"), CancellationToken.None);

        run.Success.Should().BeTrue(run.Error);
        InputsSentTo(gateway, "record").Single().ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_port_the_answer_leaves_out_reaches_the_next_node_as_null()
    {
        var drawn = TestGraphs.ResultPathsPipeline();
        var (gateway, executor) = Arrange(drawn, (_, _) => Answer("""{"result":"success","outputs":{}}"""));

        var run = await executor.RunAsync(drawn.PipelineId, Readings("north"), CancellationToken.None);

        run.Success.Should().BeTrue(run.Error);
        InputsSentTo(gateway, "record").Single().ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_fail_travels_its_fail_wire_with_the_error_and_leaves_the_success_path_not_taken()
    {
        var drawn = TestGraphs.ResultPathsPipeline();
        var (gateway, executor) = Arrange(drawn, (subdomain, _) => subdomain == "check"
            ? Answer("""{"result":"fail","error":"The reading names no reservoir.","outputs":{"out":"north"}}""")
            : Answer("""{"result":"success","outputs":{"out":"done"}}"""));

        var run = await executor.RunAsync(drawn.PipelineId, Readings("north"), CancellationToken.None);

        run.Success.Should().BeTrue("the fail was handled and no fail wire reached the end");
        InputsSentTo(gateway, "tell").Single().GetString().Should().Be("The reading names no reservoir.");
        gateway.Called.Should().NotContain("record");
        StatusOf(run, drawn.Record).Should().Be(RunStatus.NotTaken);
        StatusOf(run, drawn.End).Should().Be(RunStatus.Succeeded);
    }

    [Fact]
    public async Task A_fail_wired_straight_to_the_end_makes_the_run_report_fail()
    {
        var drawn = TestGraphs.ResultPathsPipeline(failAlsoReachesTheEnd: true);
        var (_, executor) = Arrange(drawn, (subdomain, _) => subdomain == "check"
            ? Answer("""{"result":"fail","error":"broken"}""")
            : Answer("""{"result":"success","outputs":{"out":"done"}}"""));

        var run = await executor.RunAsync(drawn.PipelineId, Readings("north"), CancellationToken.None);

        run.Success.Should().BeFalse();
        run.Error.Should().Be($"A fail path reached '{drawn.End.Name}'.");
    }

    [Fact]
    public async Task A_fail_with_no_fail_wire_fails_the_run_and_nothing_after_it_runs()
    {
        var drawn = TestGraphs.ResultPathsPipeline();
        var (gateway, executor) = Arrange(drawn, (subdomain, _) => subdomain == "record"
            ? Answer("""{"result":"fail","error":"the level could not be written"}""")
            : Answer("""{"result":"success","outputs":{"out":"done"}}"""));

        var run = await executor.RunAsync(drawn.PipelineId, Readings("north"), CancellationToken.None);

        run.Success.Should().BeFalse();
        run.Error.Should().Be("'Record level' failed, and no fail wire leaves it.");
        StatusOf(run, drawn.End).Should().Be(RunStatus.Skipped);
    }

    [Fact]
    public async Task An_answer_with_no_result_fails_the_block()
    {
        var drawn = TestGraphs.ResultPathsPipeline();
        var (gateway, executor) = Arrange(drawn, (subdomain, _) => subdomain == "check"
            ? Answer("""{"success":true,"outputs":{"out":"north"}}""")
            : Answer("""{"result":"success","outputs":{"out":"done"}}"""));

        var run = await executor.RunAsync(drawn.PipelineId, Readings("north"), CancellationToken.None);

        run.Nodes.Single(n => n.NodeId == drawn.Check.Id).Error.Should().Be(
            "'Check reading' answered no result: an answer says \"result\": \"success\" or \"fail\".");
        InputsSentTo(gateway, "tell").Should().ContainSingle("a block answering no result fails, and its fail wire carries");
    }

    [Theory]
    [InlineData("continue", new[] { "north", "south" }, new[] { "east is dry" }, RunStatus.Partial)]
    [InlineData("fail", new string[0], new[] { "east is dry" }, RunStatus.Failed)]
    public async Task Each_item_of_a_node_that_runs_once_per_item_takes_the_path_its_own_answer_chose(
        string onItemError, string[] recorded, string[] told, string checkStatus)
    {
        var drawn = TestGraphs.ResultPathsPipeline(perItem: true, onItemError: onItemError);
        var (gateway, executor) = Arrange(drawn, (subdomain, envelope) =>
        {
            if (subdomain != "check") return Answer("""{"result":"success","outputs":{"out":"done"}}""");
            var reading = envelope.GetProperty("inputs").GetProperty("in").GetString();
            return reading == "east"
                ? Answer("""{"result":"fail","error":"east is dry"}""")
                : Answer(JsonSerializer.Serialize(new { result = "success", outputs = new { @out = reading } }));
        });

        var run = await executor.RunAsync(drawn.PipelineId, Readings(new[] { "north", "east", "south" }), CancellationToken.None);

        StatusOf(run, drawn.Check).Should().Be(checkStatus);
        InputsSentTo(gateway, "record").SelectMany(sent => sent.EnumerateArray()).Select(value => value.GetString()).Should().Equal(recorded);
        InputsSentTo(gateway, "tell").SelectMany(sent => sent.EnumerateArray()).Select(value => value.GetString()).Should().Equal(told);
    }

    [Fact]
    public void A_wire_taken_on_nothing_is_refused_by_name()
    {
        var drawn = TestGraphs.ResultPathsPipeline();
        var fx = drawn.Fixture;
        fx.TakeOn(fx.WireInto(drawn.Record));

        var dag = PipelineDagBuilder.Build(fx.Build(), drawn.PipelineId);

        DagValidator.Validate(dag).Should().Contain(
            "The wire from 'Check reading' to 'Record level' is taken on nothing: it relates along takenOn to no result or decision, "
            + "so no answer from 'Check reading' could carry along it.");
    }

    [Fact]
    public void A_pipeline_with_no_Output_node_is_refused()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        var end = fx.Get("Demo end");
        fx.Unrelate(fx.Get("Demo"), fx.Get("has"), end);

        DagValidator.Validate(PipelineDagBuilder.Build(fx.Build(), pipelineId)).Should().Contain(
            "'Demo' has 0 Output nodes; every path ends at one, which reports how the run ended.");
    }

    [Fact]
    public void A_pipeline_with_two_Output_nodes_is_refused()
    {
        var drawn = TestGraphs.ResultPathsPipeline();
        var fx = drawn.Fixture;
        fx.EndAt(fx.Get("Reservoir check"), (drawn.Record, "out"));

        DagValidator.Validate(PipelineDagBuilder.Build(fx.Build(), drawn.PipelineId)).Should().Contain(
            "'Reservoir check' has 2 Output nodes; every path ends at one, which reports how the run ended.");
    }

    [Fact]
    public void A_node_whose_paths_cannot_reach_the_Output_node_is_refused_by_name()
    {
        var drawn = TestGraphs.ResultPathsPipeline();
        var fx = drawn.Fixture;
        fx.Unrelate(drawn.Tell, fx.Get("has"), fx.WireFrom(drawn.Tell, drawn.End));

        DagValidator.Validate(PipelineDagBuilder.Build(fx.Build(), drawn.PipelineId)).Should().Contain(
            "'Tell operator' cannot reach 'Reservoir check end', so a run taking its path would end without reporting how.");
    }
}
