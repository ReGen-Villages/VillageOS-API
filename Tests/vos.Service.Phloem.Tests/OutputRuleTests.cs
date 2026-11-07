using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Shared;
using Xunit;

namespace vos.Service.Phloem.Tests;

public class OutputRuleTests
{
    private static NodeCallResult Answer(string json) => new(200, json);

    private static readonly Func<string, JsonElement, NodeCallResult> CheckFails = (subdomain, _) => subdomain == "check"
        ? Answer("""{"result":"fail","error":"broken"}""")
        : Answer("""{"result":"success","outputs":{"out":"done"}}""");

    private static readonly Func<string, JsonElement, NodeCallResult> EverythingSucceeds =
        (_, _) => Answer("""{"result":"success","outputs":{"out":"done"}}""");

    private static async Task<PipelineRunResult> RunAsync(
        string? rule, Func<string, JsonElement, NodeCallResult> onCall, bool failAlsoReachesTheEnd = true)
    {
        var drawn = TestGraphs.ResultPathsPipeline(failAlsoReachesTheEnd: failAlsoReachesTheEnd);
        if (rule is not null) drawn.Fixture.Hold(drawn.End, ModelNames.When, rule);
        var gateway = new FakeGateway(drawn.Fixture.Build()) { OnCall = onCall };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());
        return await executor.RunAsync(drawn.PipelineId, JsonSerializer.SerializeToElement(new { readings = "north" }), CancellationToken.None);
    }

    [Fact]
    public async Task A_rule_that_holds_passes_a_run_a_fail_wire_reached()
    {
        var run = await RunAsync("inputs.`Tell operator` = 'done'", CheckFails);

        run.Success.Should().BeTrue(run.Error);
    }

    [Fact]
    public async Task The_rule_every_new_rule_starts_as_fails_a_run_a_fail_wire_reached()
    {
        var run = await RunAsync("$count(failures) = 0", CheckFails);

        run.Success.Should().BeFalse();
        run.Error.Should().Be("'Reservoir check end' did not pass the run: its rule does not hold.");
    }

    [Fact]
    public async Task The_rule_every_new_rule_starts_as_passes_a_run_only_success_wires_reached()
    {
        var run = await RunAsync("$count(failures) = 0", EverythingSucceeds);

        run.Success.Should().BeTrue(run.Error);
    }

    [Fact]
    public async Task A_rule_that_does_not_hold_fails_a_run_only_success_wires_reached()
    {
        var run = await RunAsync("inputs.`Record level` = 'something else'", EverythingSucceeds);

        run.Success.Should().BeFalse();
        run.Error.Should().Be("'Reservoir check end' did not pass the run: its rule does not hold.");
    }

    [Fact]
    public async Task A_rule_that_answers_nothing_does_not_hold()
    {
        var run = await RunAsync("inputs.`No such port`", EverythingSucceeds);

        run.Success.Should().BeFalse();
        run.Error.Should().Be("'Reservoir check end' did not pass the run: its rule does not hold.");
    }

    [Fact]
    public async Task Each_failure_names_the_node_it_came_from_the_port_it_reached_and_that_nodes_error()
    {
        var run = await RunAsync(
            "$count(failures) = 1 and failures[0].node = 'Check reading' and failures[0].port = 'failed' and failures[0].error = 'broken'",
            CheckFails);

        run.Success.Should().BeTrue(run.Error);
    }

    [Fact]
    public async Task A_rule_answering_neither_true_nor_false_fails_the_run_naming_what_it_answered()
    {
        var run = await RunAsync("'yes'", EverythingSucceeds);

        run.Success.Should().BeFalse();
        run.Error.Should().Be("'Reservoir check end' cannot judge the run: its rule answered \"yes\", and a rule answers true or false.");
    }

    [Fact]
    public async Task A_rule_that_fails_as_it_runs_fails_the_run_naming_the_rule()
    {
        var run = await RunAsync("$number('not a number') > 1", EverythingSucceeds);

        run.Success.Should().BeFalse();
        run.Error.Should().StartWith("'Reservoir check end' cannot judge the run: ");
    }

    [Fact]
    public void A_rule_that_does_not_compile_is_refused_naming_the_output_node()
    {
        var drawn = TestGraphs.ResultPathsPipeline();
        drawn.Fixture.Hold(drawn.End, ModelNames.When, "inputs.x = (");

        DagValidator.Validate(PipelineDagBuilder.Build(drawn.Fixture.Build(), drawn.PipelineId)).Should().ContainSingle()
            .Which.Should().StartWith("'Reservoir check end' is the Output node, and its rule does not compile: ");
    }
}
