using FluentAssertions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using Xunit;

namespace vos.Service.Phloem.Tests;

// The starter reads the target from the model and answers what it starts; the resolution rule itself is
// PipelineStartTests' subject.
public class PipelineStarterTests
{
    [Fact]
    public async Task ResolveAsync_ReadsTheTargetFromTheModelAndAnswersThePipelineDrawnFromIt()
    {
        var fixture = TestGraphs.StatePipeline();
        var gateway = new FakeGateway(fixture.Fixture.Build());
        var starter = new PipelineStarter(gateway);

        var resolution = await starter.ResolveAsync(fixture.WatchingConnectionId, Guid.NewGuid(), null, CancellationToken.None);

        resolution.PipelineId.Should().Be(fixture.DrawnPipelineId);
        gateway.StartTargetsAsked.Should().Equal(fixture.WatchingConnectionId);
        gateway.RelationshipsRead.Should().BeEmpty("a state's call record names its connection as the target");
    }

    private static (GraphThing Arriving, GraphThing System) ArrivingFromTheWaterBoard(GraphFixture fx)
    {
        var system = fx.Thing("water board system");
        fx.Rel(system, fx.Get("is"), fx.Playing("ExternalSystem", PipelineRoles.ExternalSystem));
        var arriving = fx.Thing("an arriving document");
        fx.Rel(arriving, fx.Thing("from"), system);
        return (arriving, system);
    }

    private static GraphThing PublishedStandingFor(GraphFixture fx, string name, params GraphThing[] standsFor)
    {
        var start = fx.Thing($"{name}, start");
        fx.Rel(start, fx.Get("is"), fx.Get("PipelineInput"));
        foreach (var thing in standsFor) fx.Rel(start, fx.Get("standsFor"), thing);
        var pipeline = fx.Thing(name);
        fx.Rel(pipeline, fx.Get("is"), fx.Get("Pipeline"));
        fx.Publish(pipeline);
        fx.Rel(pipeline, fx.Get("has"), start);
        return pipeline;
    }

    [Fact]
    public async Task ResolveAsync_ReadsTheArrivingThingAndStartsThePipelineNamingItsSystem()
    {
        var fixture = TestGraphs.StatePipeline();
        var fx = fixture.Fixture;
        var (arriving, system) = ArrivingFromTheWaterBoard(fx);
        var fromTheWaterBoard = PublishedStandingFor(fx, "From the water board", fx.Get("watchingConn"), system);
        var gateway = new FakeGateway(fx.Build());

        var resolution = await new PipelineStarter(gateway).ResolveAsync(fixture.WatchingConnectionId, Guid.NewGuid(), arriving.Id, CancellationToken.None);

        resolution.PipelineId.Should().Be(fromTheWaterBoard.Id);
        gateway.RelatedFromAsked.Should().Equal(arriving.Id);
        gateway.StartReadsWidenedBy.Single().Should().Equal(system.Id);
    }

    [Fact]
    public async Task ResolveAsync_ARelationshipWrittenAlongAConnection_StartsThePipelineNamingTheArrivingThingsSystem()
    {
        var fixture = TestGraphs.StatePipeline();
        var fx = fixture.Fixture;
        var (arriving, system) = ArrivingFromTheWaterBoard(fx);
        var fromTheWaterBoard = PublishedStandingFor(fx, "From the water board", fx.Get("watchingConn"), system);
        var joined = fx.Thing("the Thing the document was joined to").Id;
        var gateway = new FakeGateway(fx.Build());
        var written = Guid.NewGuid();
        gateway.PredicatesByRelationship[written] = fixture.WatchingConnectionId;

        var resolution = await new PipelineStarter(gateway).ResolveAsync(joined, written, arriving.Id, CancellationToken.None);

        resolution.PipelineId.Should().Be(fromTheWaterBoard.Id);
    }

    [Fact]
    public async Task ResolveAsync_ARelationshipAlongAPredicateThatIsNoConnection_StartsThePipelineNamingItsTargetAndTheArrivingThingsSystem()
    {
        var fixture = TestGraphs.StatePipeline();
        var fx = fixture.Fixture;
        var (arriving, system) = ArrivingFromTheWaterBoard(fx);
        var joined = fx.Thing("the Thing the document was joined to");
        PublishedStandingFor(fx, "For anyone", joined);
        var fromTheWaterBoard = PublishedStandingFor(fx, "From the water board", joined, system);
        var gateway = new FakeGateway(fx.Build());
        var written = Guid.NewGuid();
        gateway.PredicatesByRelationship[written] = fx.Thing("sits beside").Id;

        var resolution = await new PipelineStarter(gateway).ResolveAsync(joined.Id, written, arriving.Id, CancellationToken.None);

        resolution.PipelineId.Should().Be(fromTheWaterBoard.Id);
    }

    [Fact]
    public async Task ResolveAsync_RefusesATargetNothingIsDrawnFrom()
    {
        var fixture = TestGraphs.StatePipeline();
        var starter = new PipelineStarter(new FakeGateway(fixture.Fixture.Build()));

        var resolution = await starter.ResolveAsync(fixture.LooseConnectionId, Guid.NewGuid(), null, CancellationToken.None);

        resolution.Started.Should().BeFalse();
        resolution.Refusal.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ResolveAsync_ARelationshipWrittenAlongAConnection_StartsThePipelineDrawnFromThatConnection()
    {
        var fixture = TestGraphs.StatePipeline();
        var joined = fixture.Fixture.Thing("the Thing the subject was joined to").Id;
        var gateway = new FakeGateway(fixture.Fixture.Build());
        var written = Guid.NewGuid();
        gateway.PredicatesByRelationship[written] = fixture.WatchingConnectionId;

        var resolution = await new PipelineStarter(gateway).ResolveAsync(joined, written, null, CancellationToken.None);

        resolution.PipelineId.Should().Be(fixture.DrawnPipelineId);
        gateway.StartTargetsAsked.Should().Equal(joined, fixture.WatchingConnectionId);
    }

    [Fact]
    public async Task ResolveAsync_ARunsRelationshipNamingThePipeline_IsNotReadThroughItsPredicate()
    {
        var fixture = TestGraphs.StatePipeline();
        var gateway = new FakeGateway(fixture.Fixture.Build());

        var resolution = await new PipelineStarter(gateway).ResolveAsync(fixture.DrawnPipelineId, Guid.NewGuid(), null, CancellationToken.None);

        resolution.PipelineId.Should().Be(fixture.DrawnPipelineId);
        gateway.RelationshipsRead.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_ARelationshipTheModelDoesNotHold_IsRefusedForItsTarget()
    {
        var fixture = TestGraphs.StatePipeline();
        var joined = fixture.Fixture.Thing("the Thing the subject was joined to").Id;
        var gateway = new FakeGateway(fixture.Fixture.Build());

        var resolution = await new PipelineStarter(gateway).ResolveAsync(joined, Guid.NewGuid(), null, CancellationToken.None);

        resolution.Started.Should().BeFalse();
        resolution.Refusal.Should().Contain("the Thing the subject was joined to");
    }

    [Fact]
    public async Task ResolveAsync_ARelationshipAlongAPredicateThatIsNoConnection_IsRefusedForItsTarget()
    {
        var fixture = TestGraphs.StatePipeline();
        var joined = fixture.Fixture.Thing("the Thing the subject was joined to").Id;
        var plainPredicate = fixture.Fixture.Thing("sits beside").Id;
        var gateway = new FakeGateway(fixture.Fixture.Build());
        var written = Guid.NewGuid();
        gateway.PredicatesByRelationship[written] = plainPredicate;

        var resolution = await new PipelineStarter(gateway).ResolveAsync(joined, written, null, CancellationToken.None);

        resolution.Started.Should().BeFalse();
        resolution.Refusal.Should().Contain("the Thing the subject was joined to");
    }
}
