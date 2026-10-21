using FluentAssertions;
using vos.Service.Phloem.Execution;
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

        var resolution = await starter.ResolveAsync(fixture.WatchingConnectionId, Guid.NewGuid(), CancellationToken.None);

        resolution.PipelineId.Should().Be(fixture.DrawnPipelineId);
        gateway.StartTargetsAsked.Should().Equal(fixture.WatchingConnectionId);
        gateway.RelationshipsRead.Should().BeEmpty("a state's dispatch record names its connection as the target");
    }

    [Fact]
    public async Task ResolveAsync_RefusesATargetNothingIsDrawnFrom()
    {
        var fixture = TestGraphs.StatePipeline();
        var starter = new PipelineStarter(new FakeGateway(fixture.Fixture.Build()));

        var resolution = await starter.ResolveAsync(fixture.LooseConnectionId, Guid.NewGuid(), CancellationToken.None);

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

        var resolution = await new PipelineStarter(gateway).ResolveAsync(joined, written, CancellationToken.None);

        resolution.PipelineId.Should().Be(fixture.DrawnPipelineId);
        gateway.StartTargetsAsked.Should().Equal(joined, fixture.WatchingConnectionId);
    }

    [Fact]
    public async Task ResolveAsync_ARunsRelationshipNamingThePipeline_IsNotReadThroughItsPredicate()
    {
        var fixture = TestGraphs.StatePipeline();
        var gateway = new FakeGateway(fixture.Fixture.Build());

        var resolution = await new PipelineStarter(gateway).ResolveAsync(fixture.DrawnPipelineId, Guid.NewGuid(), CancellationToken.None);

        resolution.PipelineId.Should().Be(fixture.DrawnPipelineId);
        gateway.RelationshipsRead.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_ARelationshipTheModelDoesNotHold_IsRefusedForItsTarget()
    {
        var fixture = TestGraphs.StatePipeline();
        var joined = fixture.Fixture.Thing("the Thing the subject was joined to").Id;
        var gateway = new FakeGateway(fixture.Fixture.Build());

        var resolution = await new PipelineStarter(gateway).ResolveAsync(joined, Guid.NewGuid(), CancellationToken.None);

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

        var resolution = await new PipelineStarter(gateway).ResolveAsync(joined, written, CancellationToken.None);

        resolution.Started.Should().BeFalse();
        resolution.Refusal.Should().Contain("the Thing the subject was joined to");
    }
}
