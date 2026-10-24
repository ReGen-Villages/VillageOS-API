using FluentAssertions;
using vos.Service.Phloem.Model;
using Xunit;

namespace vos.Service.Phloem.Tests;

// Which pipeline the target of a relationship the broker sends starts. The broker posts one body for a `runs`
// write and for a state entry; only what the target is tells them apart, and that is read from the model.
public class PipelineStartTests
{
    [Fact]
    public void A_target_that_is_a_pipeline_starts_itself()
    {
        var fixture = TestGraphs.StatePipeline();

        var resolution = PipelineStart.Resolve(fixture.Fixture.Build(), fixture.DrawnPipelineId);

        resolution.Started.Should().BeTrue();
        resolution.PipelineId.Should().Be(fixture.DrawnPipelineId);
    }

    [Fact]
    public void A_connection_starts_the_pipeline_whose_start_node_stands_for_it()
    {
        var fixture = TestGraphs.StatePipeline();

        var resolution = PipelineStart.Resolve(fixture.Fixture.Build(), fixture.WatchingConnectionId);

        resolution.Started.Should().BeTrue();
        resolution.PipelineId.Should().Be(fixture.DrawnPipelineId);
    }

    // The page draws a state row standing for the state, not for any one connection watching it, because
    // several may. The broker calls the connection, so the orchestrator walks to the state it watches.
    [Fact]
    public void A_connection_starts_the_pipeline_whose_start_node_stands_for_the_state_it_watches()
    {
        var fixture = TestGraphs.StatePipeline();
        var fx = fixture.Fixture;
        var watches = fx.Playing("watches", PipelineRoles.StateWatch);
        var state = fx.Thing("SubmissionHandled");
        fx.Rel(fx.Get("looseConn"), watches, state);
        var start = fx.Thing("Decided");
        fx.Rel(start, fx.Get("is"), fx.Get("PipelineInput"));
        fx.Rel(start, fx.Get("standsFor"), state);
        var drawnFromTheState = fx.Thing("Tell the submitter");
        fx.Rel(drawnFromTheState, fx.Get("is"), fx.Get("Pipeline"));
        fx.Publish(drawnFromTheState);
        fx.Rel(drawnFromTheState, fx.Get("has"), start);

        var resolution = PipelineStart.Resolve(fx.Build(), fixture.LooseConnectionId);

        resolution.PipelineId.Should().Be(drawnFromTheState.Id);
    }

    [Fact]
    public void A_draft_drawn_from_the_same_connection_is_never_the_one_started()
    {
        var fixture = TestGraphs.StatePipeline();
        var fx = fixture.Fixture;
        var draftStart = fx.Thing("Arrival, redrawn");
        fx.Rel(draftStart, fx.Get("is"), fx.Get("PipelineInput"));
        fx.Rel(draftStart, fx.Get("standsFor"), fx.Get("watchingConn"));
        var draft = fx.Thing("Drawn from the state, draft");
        fx.Rel(draft, fx.Get("is"), fx.Get("Pipeline"));
        fx.Rel(draft, fx.Get("has"), draftStart);

        var withBoth = PipelineStart.Resolve(fx.Build(), fixture.WatchingConnectionId);
        fx.State(fx.Get("Drawn from the state"), ModelNames.Published, false);
        var withTheDraftAlone = PipelineStart.Resolve(fx.Build(), fixture.WatchingConnectionId);

        withBoth.PipelineId.Should().Be(fixture.DrawnPipelineId);
        withTheDraftAlone.Started.Should().BeFalse();
        withTheDraftAlone.Refusal.Should().Contain("No published pipeline is drawn from 'watchingConn'");
    }

    [Fact]
    public void A_target_that_is_a_draft_is_refused_saying_so()
    {
        var fixture = TestGraphs.StatePipeline();
        fixture.Fixture.State(fixture.Fixture.Get("Drawn from the state"), ModelNames.Published, false);

        var resolution = PipelineStart.Resolve(fixture.Fixture.Build(), fixture.DrawnPipelineId);

        resolution.Started.Should().BeFalse();
        resolution.Refusal.Should().Contain("'Drawn from the state' is a draft");
    }

    [Fact]
    public void A_connection_starts_the_pipeline_it_reaches_along_the_start_mark()
    {
        var fixture = TestGraphs.StatePipeline();

        var resolution = PipelineStart.Resolve(fixture.Fixture.Build(), fixture.ReachingConnectionId);

        resolution.Started.Should().BeTrue();
        resolution.PipelineId.Should().Be(fixture.ReachedPipelineId);
    }

    [Fact]
    public void A_connection_reaching_only_a_draft_along_the_start_mark_starts_nothing()
    {
        var fixture = TestGraphs.StatePipeline();
        fixture.Fixture.State(fixture.Fixture.Get("Reached along the start mark"), ModelNames.Published, false);

        PipelineStart.Resolve(fixture.Fixture.Build(), fixture.ReachingConnectionId).Started.Should().BeFalse();
    }

    // The drawing is the binding the page writes; the start mark is a model's own direct binding. Where a
    // connection carries both, the drawing wins, as it does on the page's rail.
    [Fact]
    public void A_start_node_standing_for_the_connection_wins_over_the_start_mark()
    {
        var fixture = TestGraphs.StatePipeline();
        var fx = fixture.Fixture;
        fx.Rel(fx.Get("watchingConn"), fx.Get("starts"), fx.Get("Reached along the start mark"));

        var resolution = PipelineStart.Resolve(fx.Build(), fixture.WatchingConnectionId);

        resolution.PipelineId.Should().Be(fixture.DrawnPipelineId);
    }

    [Fact]
    public void A_connection_nothing_is_drawn_from_is_refused_by_name()
    {
        var fixture = TestGraphs.StatePipeline();

        var resolution = PipelineStart.Resolve(fixture.Fixture.Build(), fixture.LooseConnectionId);

        resolution.Started.Should().BeFalse();
        resolution.PipelineId.Should().BeNull();
        resolution.Refusal.Should().Contain("looseConn").And.Contain("No published pipeline is drawn from");
    }

    [Fact]
    public void A_target_the_model_does_not_hold_is_refused()
    {
        var fixture = TestGraphs.StatePipeline();
        var unknown = Guid.NewGuid();

        var resolution = PipelineStart.Resolve(fixture.Fixture.Build(), unknown);

        resolution.Started.Should().BeFalse();
        resolution.Refusal.Should().Contain(unknown.ToString());
    }

    // A node standing for the connection that is not a start node — an end node left standing for a
    // state by mistake — starts nothing; only a start node says a run comes from the connection.
    [Fact]
    public void Only_a_start_node_standing_for_the_connection_counts()
    {
        var fixture = TestGraphs.StatePipeline();
        var fx = fixture.Fixture;
        fx.Rel(fx.Get("Out"), fx.Get("standsFor"), fx.Get("looseConn"));

        var resolution = PipelineStart.Resolve(fx.Build(), fixture.LooseConnectionId);

        resolution.Started.Should().BeFalse();
    }

    // A start node no pipeline holds is a fragment of a drawing, and a fragment starts nothing.
    [Fact]
    public void A_start_node_held_by_no_pipeline_starts_nothing()
    {
        var fixture = TestGraphs.StatePipeline();
        var fx = fixture.Fixture;
        var stray = fx.Thing("Stray start");
        fx.Rel(stray, fx.Get("is"), fx.Get("PipelineInput"));
        fx.Rel(stray, fx.Get("standsFor"), fx.Get("looseConn"));

        var resolution = PipelineStart.Resolve(fx.Build(), fixture.LooseConnectionId);

        resolution.Started.Should().BeFalse();
    }

    // The mark is on the predicate itself. A predicate that merely `is` a marked one does not play the role,
    // which is how the platform reads the predicates it calls services on.
    [Fact]
    public void A_predicate_carrying_no_mark_of_its_own_does_not_stand_for_anything()
    {
        var fixture = TestGraphs.StatePipeline();
        var fx = fixture.Fixture;
        var unmarked = fx.Thing("standsForToo");
        fx.Rel(unmarked, fx.Get("is"), fx.Get("standsFor"));
        var start = fx.Thing("Another start");
        fx.Rel(start, fx.Get("is"), fx.Get("PipelineInput"));
        fx.Rel(start, unmarked, fx.Get("looseConn"));
        fx.Rel(fx.Get("Drawn from the state"), fx.Get("has"), start);

        var resolution = PipelineStart.Resolve(fx.Build(), fixture.LooseConnectionId);

        resolution.Started.Should().BeFalse();
    }
}
