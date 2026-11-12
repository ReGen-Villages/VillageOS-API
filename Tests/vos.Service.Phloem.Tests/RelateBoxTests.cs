using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Shared;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Phloem.Tests;

// A relate box starts a service the way the model does: it relates the Thing reaching it along the connection it stands
// for, so the platform calls the service, and waits for the state the connection is proven by.
public class RelateBoxTests
{
    private static readonly RunSubject Study = new(Guid.NewGuid(), "Willow Bend study");

    private static (TestGraphs.RelateBoxFixture Box, FakeGateway Gateway, PipelineExecutor Executor) Arrange(TestGraphs.RelateBoxFixture? fixture = null)
    {
        var box = fixture ?? TestGraphs.RelateBoxPipeline();
        var gateway = new FakeGateway(box.Fixture.Build())
        {
            OnCall = (_, envelope) => new NodeCallResult(200, JsonSerializer.Serialize(
                new { result = "success", outputs = new { @out = envelope.GetProperty("inputs").GetProperty("in") } })),
        };
        return (box, gateway, new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock()));
    }

    private static Task<PipelineRunResult> RunAsync(TestGraphs.RelateBoxFixture box, PipelineExecutor executor, bool rehearsal = false) =>
        executor.RunAsync(box.PipelineId, default, CancellationToken.None, subject: Study, rehearsal: rehearsal);

    [Fact]
    public async Task A_run_reaching_a_relate_box_relates_its_Thing_to_the_connections_service_and_waits_for_the_connections_state()
    {
        var (box, gateway, executor) = Arrange();

        var result = await RunAsync(box, executor);

        gateway.RelationshipsWritten.Should().Equal((Study.Id, box.Connection.Id, box.Service.Id));
        gateway.Called.Should().BeEmpty("the platform calls the service, not the orchestrator");
        result.Error.Should().Be($"The run waits for 'Willow Bend study' to enter 'EnergyWorkedOut' at '{TestGraphs.RelateBox}'.");
        gateway.Watches[(result.RunId, box.Relate.Id)].Should().Be(new Watch(Study.Id, box.WorkedOut.Id));
        gateway.Records[(result.RunId, box.Relate.Id)].Outputs[ModelNames.Written].GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task A_relate_box_whose_Thing_is_already_related_writes_nothing_and_still_waits()
    {
        var (box, gateway, executor) = Arrange();
        gateway.ReachedAlong[(Study.Id, box.Connection.Id)] = [new RunSubject(box.Service.Id, box.Service.Name)];

        var result = await RunAsync(box, executor);

        gateway.RelationshipsWritten.Should().BeEmpty();
        gateway.Records[(result.RunId, box.Relate.Id)].Status.Should().Be(RunStatus.Waiting);
        gateway.Records[(result.RunId, box.Relate.Id)].Outputs[ModelNames.Written].GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task A_resumed_relate_box_hands_on_its_Thing()
    {
        var (box, gateway, executor) = Arrange();
        var waited = await RunAsync(box, executor);

        await executor.ResumeAsync(waited.RunId, box.Relate.Id, CancellationToken.None);

        gateway.Envelopes.Single(sent => sent.Subdomain == "publish").Envelope.GetProperty("inputs").GetProperty("in")
            .GetProperty("id").GetGuid().Should().Be(Study.Id);
        gateway.RunStatusOf(waited.RunId).Should().Be(RunStatus.Succeeded);
    }

    [Fact]
    public async Task A_relationship_the_platform_refuses_fails_the_box_with_its_reason_and_places_no_watch()
    {
        var (box, gateway, executor) = Arrange();
        gateway.RefuseRelationship = (_, _, _) => "403 the caller may not write along balancesEnergy";

        var result = await RunAsync(box, executor);

        var relate = result.Nodes.Single(node => node.NodeId == box.Relate.Id);
        relate.Status.Should().Be(RunStatus.Failed);
        relate.Error.Should().Be($"'{TestGraphs.RelateBox}' could not relate 'Willow Bend study' along 'balancesEnergy': "
            + "The platform refused a relationship: 403 the caller may not write along balancesEnergy");
        gateway.Watches.Should().BeEmpty();
    }

    // Two runs for one Thing can both find it unrelated; the second's write is refused because the first's landed.
    [Fact]
    public async Task A_refused_relationship_that_another_write_has_since_made_counts_as_already_related()
    {
        var (box, gateway, executor) = Arrange();
        gateway.RefuseRelationship = (subject, predicate, target) =>
        {
            gateway.ReachedAlong[(subject, predicate)] = [new RunSubject(target, string.Empty)];
            return "400 the relationship exists";
        };

        var result = await RunAsync(box, executor);

        gateway.Records[(result.RunId, box.Relate.Id)].Status.Should().Be(RunStatus.Waiting);
        gateway.Records[(result.RunId, box.Relate.Id)].Outputs[ModelNames.Written].GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task A_relate_box_that_cannot_read_whether_its_Thing_is_related_fails_and_writes_nothing()
    {
        var (box, gateway, executor) = Arrange();
        gateway.ReachedAlongFailure = new HttpRequestException("503 from the broker");

        var result = await RunAsync(box, executor);

        result.Nodes.Single(node => node.NodeId == box.Relate.Id).Error.Should().Be(
            $"'{TestGraphs.RelateBox}' could not read whether 'Willow Bend study' is related along 'balancesEnergy': 503 from the broker");
        gateway.RelationshipsWritten.Should().BeEmpty();
    }

    [Fact]
    public async Task A_relate_box_reached_by_something_that_names_no_Thing_fails_naming_its_port()
    {
        var (box, gateway, executor) = Arrange();

        var result = await executor.RunAsync(box.PipelineId, JsonSerializer.SerializeToElement(new { subject = "not a Thing" }), CancellationToken.None);

        result.Nodes.Single(node => node.NodeId == box.Relate.Id).Error.Should().Be(
            $"'{TestGraphs.RelateBox}' relates the Thing reaching its port 'thing', but what reached it names no Thing.");
        gateway.RelationshipsWritten.Should().BeEmpty();
    }

    [Fact]
    public async Task A_refused_relationship_whose_second_read_also_fails_fails_the_box_with_the_refusal()
    {
        var (box, gateway, executor) = Arrange();
        gateway.RefuseRelationship = (_, _, _) =>
        {
            gateway.ReachedAlongFailure = new HttpRequestException("503 from the broker");
            return "500 the write was lost";
        };

        var result = await RunAsync(box, executor);

        result.Nodes.Single(node => node.NodeId == box.Relate.Id).Error.Should().Be(
            $"'{TestGraphs.RelateBox}' could not relate 'Willow Bend study' along 'balancesEnergy': The platform refused a relationship: 500 the write was lost");
        gateway.Watches.Should().BeEmpty();
    }

    [Fact]
    public async Task A_relate_box_whose_inputs_cannot_be_put_together_fails_saying_so_and_writes_nothing()
    {
        var (box, gateway, executor) = Arrange();
        box.Fixture.State(box.SubjectWire, ModelNames.ListTransform, "$error(\"no shape for the study\")");
        gateway.Graph = box.Fixture.Build();

        var result = await RunAsync(box, executor);

        result.Nodes.Single(node => node.NodeId == box.Relate.Id).Error.Should().Contain("could not be put together").And.Contain("no shape for the study");
        gateway.RelationshipsWritten.Should().BeEmpty();
    }

    [Fact]
    public async Task A_relate_box_standing_for_a_connection_that_names_two_states_refuses_the_run()
    {
        var box = TestGraphs.RelateBoxPipeline();
        box.Fixture.Rel(box.Connection, box.Fixture.Get("provenBy"), box.Fixture.Thing("EnergyWorkedOutAgain"));
        var (_, gateway, executor) = Arrange(box);

        var result = await RunAsync(box, executor);

        result.Error.Should().Contain($"Node '{TestGraphs.RelateBox}' stands for 'balancesEnergy', which names two states its work is proven by.");
        gateway.RunStatusOf(result.RunId).Should().Be(RunStatus.Refused);
    }

    [Fact]
    public async Task A_relate_box_standing_for_a_connection_that_binds_no_service_refuses_the_run()
    {
        var box = TestGraphs.RelateBoxPipeline();
        box.Fixture.Unrelate(box.Connection, box.Fixture.Get("has"), box.Service);
        var (_, gateway, executor) = Arrange(box);

        var result = await RunAsync(box, executor);

        result.Error.Should().Contain($"Node '{TestGraphs.RelateBox}' stands for 'balancesEnergy', which binds no service, so nothing would be called.");
        gateway.RunStatusOf(result.RunId).Should().Be(RunStatus.Refused);
    }

    [Fact]
    public async Task A_rehearsal_records_what_a_relate_box_would_have_related_and_writes_nothing()
    {
        var (box, gateway, executor) = Arrange();

        var result = await RunAsync(box, executor, rehearsal: true);

        gateway.Records[(result.RunId, box.Relate.Id)].Status.Should().Be(RunStatus.Rehearsed);
        gateway.RelationshipsWritten.Should().BeEmpty();
        gateway.Watches.Should().BeEmpty();
    }

    [Fact]
    public async Task A_relate_box_standing_for_a_connection_that_names_no_state_refuses_the_run()
    {
        var (box, gateway, executor) = Arrange(TestGraphs.RelateBoxPipeline(provesByAState: false));

        var result = await RunAsync(box, executor);

        result.Error.Should().Contain($"Node '{TestGraphs.RelateBox}' stands for 'balancesEnergy', which names no state its work is proven by, "
            + "so the run could not tell when the work is done.");
        gateway.RunStatusOf(result.RunId).Should().Be(RunStatus.Refused);
        gateway.RelationshipsWritten.Should().BeEmpty();
    }

    [Fact]
    public async Task A_relate_box_standing_for_no_connection_refuses_the_run()
    {
        var (box, gateway, executor) = Arrange(TestGraphs.RelateBoxPipeline(standsForAConnection: false));

        var result = await RunAsync(box, executor);

        result.Error.Should().Contain($"Node '{TestGraphs.RelateBox}' is a relate node but stands for no connection.");
        gateway.RunStatusOf(result.RunId).Should().Be(RunStatus.Refused);
    }

    [Fact]
    public async Task A_relate_box_with_nothing_wired_into_its_port_refuses_the_run()
    {
        var box = TestGraphs.RelateBoxPipeline();
        box.Fixture.Forget(box.SubjectWire);
        var (_, gateway, executor) = Arrange(box);

        var result = await RunAsync(box, executor);

        result.Error.Should().Contain($"'{TestGraphs.RelateBox}' waits for the Thing reaching its port 'thing', but nothing fills that port.");
        gateway.RunStatusOf(result.RunId).Should().Be(RunStatus.Refused);
    }

    // Writing again is safe, because the box finds a relationship it already wrote and writes nothing.
    [Fact]
    public async Task A_restarted_orchestrator_carries_on_a_relate_box_caught_writing_without_writing_twice()
    {
        var (box, gateway, executor) = Arrange();
        var runId = Guid.NewGuid();
        var start = box.Fixture.Get("Discovery finished");
        gateway.Runs[runId] = (box.PipelineId, JsonSerializer.SerializeToElement(new { subject = new { id = Study.Id, name = Study.Name } }));
        gateway.Records[(runId, start.Id)] = new NodeRecord(RunStatus.Succeeded,
            new Dictionary<string, JsonElement> { ["subject"] = JsonSerializer.SerializeToElement(new { id = Study.Id, name = Study.Name }) });
        gateway.Records[(runId, box.Relate.Id)] = new NodeRecord(RunStatus.Running, new Dictionary<string, JsonElement>());
        gateway.ReachedAlong[(Study.Id, box.Connection.Id)] = [new RunSubject(box.Service.Id, box.Service.Name)];

        var carriedOn = await executor.RecoverUnfinishedAsync(CancellationToken.None);

        carriedOn.Should().Be(1);
        await Settle.UntilAsync(() => gateway.Watches.ContainsKey((runId, box.Relate.Id)), "the box waits again");
        gateway.RelationshipsWritten.Should().BeEmpty();
    }
}
