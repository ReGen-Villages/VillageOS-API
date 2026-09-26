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

        var resolution = await starter.ResolveAsync(fixture.WatchingConnectionId, CancellationToken.None);

        resolution.PipelineId.Should().Be(fixture.DrawnPipelineId);
        gateway.StartTargetsAsked.Should().Equal(fixture.WatchingConnectionId);
    }

    [Fact]
    public async Task ResolveAsync_RefusesATargetNothingIsDrawnFrom()
    {
        var fixture = TestGraphs.StatePipeline();
        var starter = new PipelineStarter(new FakeGateway(fixture.Fixture.Build()));

        var resolution = await starter.ResolveAsync(fixture.LooseConnectionId, CancellationToken.None);

        resolution.Started.Should().BeFalse();
        resolution.Refusal.Should().NotBeNullOrEmpty();
    }
}
