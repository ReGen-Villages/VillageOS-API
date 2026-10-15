using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Shared;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Phloem.Tests;

// A drawing ends by starting another: the run that reached the end leaves a second run of the pipeline the end
// stands for, about the same subject, carrying what reached the end as its parameters.
public class StartingRunTests
{
    private static readonly RunSubject Plot = new(Guid.NewGuid(), "Plot 7");

    private static (TestGraphs.StartingFixture Starting, FakeGateway Gateway, PipelineExecutor Executor) Arrange()
    {
        var starting = TestGraphs.StartingPipeline();
        var gateway = new FakeGateway(starting.Fixture.Build())
        {
            OnDispatch = (subdomain, envelope) => subdomain == "ech"
                ? new NodeDispatchResult(200, JsonSerializer.Serialize(new { success = true, outputs = new { echo = envelope.GetProperty("inputs").GetProperty("message").GetString() } }))
                : new NodeDispatchResult(200, JsonSerializer.Serialize(new { success = false, error = $"unexpected {subdomain}" })),
        };
        return (starting, gateway, new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock()));
    }

    [Fact]
    public async Task A_run_ending_at_a_pipeline_leaves_a_second_run_of_it_about_the_same_subject_carrying_the_ends_inputs_and_naming_the_first()
    {
        var (starting, gateway, executor) = Arrange();

        var first = await executor.RunAsync(starting.PipelineId, JsonSerializer.SerializeToElement(new { seed = "world" }), CancellationToken.None, subject: Plot);

        first.Success.Should().BeTrue();
        await Settle.UntilAsync(() => { lock (gateway.RunsCreated) return gateway.RunsCreated.Count == 2; }, "the second run is recorded");
        var second = gateway.RunsCreated.Last();
        second.PipelineId.Should().Be(starting.NextPipelineId);
        second.StartedBy.Should().Be(first.RunId);
        second.Subject.Should().Be(Plot);
        var parameters = gateway.Runs[second.RunId].Parameters;
        parameters.EnumerateObject().Select(parameter => parameter.Name).Should().Equal("subject", "result");
        parameters.GetProperty("result").GetString().Should().Be("world");
        first.Nodes.Single(node => node.Name == "Out").Outputs[ModelNames.StartedRun].GetString().Should().Be(second.RunId.ToString());
    }

    [Fact]
    public async Task A_run_nobody_started_from_an_end_names_no_starting_run()
    {
        var (starting, gateway, executor) = Arrange();

        await executor.RunAsync(starting.PipelineId, JsonSerializer.SerializeToElement(new { seed = "world" }), CancellationToken.None);

        gateway.RunsCreated.First().StartedBy.Should().BeNull();
    }
}
