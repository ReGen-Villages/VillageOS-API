using FluentAssertions;
using vos.Service.Phloem.Model;
using Xunit;

namespace vos.Service.Phloem.Tests;

// Several published pipelines may claim one start. The one whose start node stands for the most of what the start
// offers is the one started, whatever order the pipelines are read in.
public class MostSpecificStartTests
{
    private readonly GraphFixture _fixture = new();
    private readonly PipelineVocabulary _vocabulary;
    private readonly GraphThing _standsFor;
    private readonly GraphThing _arrives;
    private readonly GraphThing _meterReading;
    private readonly GraphThing _surveyReport;
    private readonly GraphThing _waterBoard;
    private readonly GraphThing _otherSystem;
    private readonly GraphThing _ofKind;
    private readonly GraphThing _from;

    public MostSpecificStartTests()
    {
        _vocabulary = _fixture.DeclareVocabulary();
        _standsFor = _fixture.Playing("standsFor", PipelineRoles.StandsFor);
        _arrives = _fixture.Thing("arrives");
        _fixture.Rel(_arrives, _vocabulary.Is, _vocabulary.Connection);
        var kind = _fixture.Playing("MessageKind", PipelineRoles.MessageKind);
        var system = _fixture.Playing("ExternalSystem", PipelineRoles.ExternalSystem);
        _meterReading = Member("meter reading", kind);
        _surveyReport = Member("survey report", kind);
        _waterBoard = Member("water board system", system);
        _otherSystem = Member("another system", system);
        _ofKind = _fixture.Thing("ofKind");
        _from = _fixture.Thing("from");
    }

    private GraphThing Member(string name, GraphThing archetype)
    {
        var member = _fixture.Thing(name);
        _fixture.Rel(member, _vocabulary.Is, archetype);
        return member;
    }

    private GraphThing Pipeline(string name, bool published, params GraphThing[] standsFor)
    {
        var pipeline = _fixture.Thing(name);
        _fixture.Rel(pipeline, _vocabulary.Is, _vocabulary.Pipeline);
        if (published) _fixture.Publish(pipeline);
        var start = _fixture.Thing($"{name} · start");
        _fixture.Rel(start, _vocabulary.Is, _vocabulary.PipelineInput);
        _fixture.Rel(pipeline, _vocabulary.Has, start);
        foreach (var stood in standsFor) _fixture.Rel(start, _standsFor, stood);
        return pipeline;
    }

    private GraphThing Published(string name, params GraphThing[] standsFor) => Pipeline(name, published: true, standsFor);

    private GraphThing Arriving(GraphThing? kind, GraphThing? system)
    {
        var document = _fixture.Thing("an arriving document");
        if (kind is not null) _fixture.Rel(document, _ofKind, kind);
        if (system is not null) _fixture.Rel(document, _from, system);
        return document;
    }

    private StartResolution Start(GraphThing document)
    {
        var graph = _fixture.Build();
        return PipelineStart.Resolve(graph, _arrives.Id, graph.TargetIdsFrom(document).ToList());
    }

    [Fact]
    public void A_message_from_the_named_system_starts_the_pipeline_naming_its_kind_and_system()
    {
        Published("standard", _arrives);
        Published("every meter reading", _arrives, _meterReading);
        var waterBoardReadings = Published("the water board's meter readings", _arrives, _meterReading, _waterBoard);

        Start(Arriving(_meterReading, _waterBoard)).PipelineId.Should().Be(waterBoardReadings.Id);
    }

    [Fact]
    public void A_message_from_any_other_system_starts_the_pipeline_naming_its_kind_alone()
    {
        Published("standard", _arrives);
        var everyMeterReading = Published("every meter reading", _arrives, _meterReading);
        Published("the water board's meter readings", _arrives, _meterReading, _waterBoard);

        Start(Arriving(_meterReading, _otherSystem)).PipelineId.Should().Be(everyMeterReading.Id);
    }

    [Fact]
    public void Naming_the_kind_and_the_system_beats_naming_the_system_alone()
    {
        Published("everything from the water board", _arrives, _waterBoard);
        var waterBoardReadings = Published("the water board's meter readings", _arrives, _meterReading, _waterBoard);

        Start(Arriving(_meterReading, _waterBoard)).PipelineId.Should().Be(waterBoardReadings.Id);
    }

    [Fact]
    public void Naming_the_kind_or_the_system_beats_naming_neither()
    {
        Published("standard", _arrives);
        var fromTheWaterBoard = Published("everything from the water board", _arrives, _waterBoard);

        Start(Arriving(_surveyReport, _waterBoard)).PipelineId.Should().Be(fromTheWaterBoard.Id);
    }

    [Fact]
    public void A_message_naming_neither_kind_nor_system_starts_the_pipeline_naming_neither()
    {
        Published("the water board's meter readings", _arrives, _meterReading, _waterBoard);
        var standard = Published("standard", _arrives);

        Start(Arriving(null, null)).PipelineId.Should().Be(standard.Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_choice_does_not_depend_on_the_order_the_pipelines_are_read_in(bool specificFirst)
    {
        GraphThing waterBoardReadings;
        if (specificFirst)
        {
            waterBoardReadings = Published("the water board's meter readings", _arrives, _meterReading, _waterBoard);
            Published("standard", _arrives);
        }
        else
        {
            Published("standard", _arrives);
            waterBoardReadings = Published("the water board's meter readings", _arrives, _meterReading, _waterBoard);
        }

        Start(Arriving(_meterReading, _waterBoard)).PipelineId.Should().Be(waterBoardReadings.Id);
    }

    [Fact]
    public void A_draft_claims_nothing_even_where_it_would_be_the_more_specific()
    {
        Pipeline("the water board's meter readings, being revised", published: false, _arrives, _meterReading, _waterBoard);
        var standard = Published("standard", _arrives);

        Start(Arriving(_meterReading, _waterBoard)).PipelineId.Should().Be(standard.Id);
    }

    [Fact]
    public void A_start_node_standing_for_something_the_start_does_not_offer_does_not_fit()
    {
        Published("the water board's meter readings", _arrives, _meterReading, _waterBoard);
        var standard = Published("standard", _arrives);

        Start(Arriving(_meterReading, _otherSystem)).PipelineId.Should().Be(standard.Id);
    }

    [Fact]
    public void Of_what_the_arriving_Thing_relates_to_only_its_kind_and_its_system_are_offered()
    {
        var standard = Published("standard", _arrives);
        var aPlace = _fixture.Thing("a place the document names");
        Published("documents naming the place", _arrives, aPlace);
        var document = Arriving(_meterReading, null);
        _fixture.Rel(document, _fixture.Thing("names"), aPlace);

        Start(document).PipelineId.Should().Be(standard.Id);
    }

    [Fact]
    public void A_start_node_standing_for_a_Thing_the_read_left_out_does_not_fit()
    {
        var standard = Published("standard", _arrives);
        var elsewhere = _fixture.Thing("a Thing the start read did not carry");
        Published("the water board's meter readings, and more", _arrives, _meterReading, _waterBoard, elsewhere);
        _fixture.Forget(elsewhere);

        Start(Arriving(_meterReading, _waterBoard)).PipelineId.Should().Be(standard.Id);
    }

    [Fact]
    public void Two_published_pipelines_standing_for_exactly_the_same_Things_refuse_the_start_naming_both()
    {
        Published("standard", _arrives);
        Published("the water board's meter readings", _arrives, _meterReading, _waterBoard);
        Published("the water board's meter readings, copied", _arrives, _meterReading, _waterBoard);

        var start = Start(Arriving(_meterReading, _waterBoard));

        start.Started.Should().BeFalse();
        start.Refusal.Should().Contain("'the water board's meter readings'").And.Contain("'the water board's meter readings, copied'")
            .And.Contain("exactly the same");
    }

    [Fact]
    public void Two_different_claims_naming_as_many_Things_that_both_fit_refuse_the_start_naming_both()
    {
        Published("every meter reading", _arrives, _meterReading);
        Published("everything from the water board", _arrives, _waterBoard);

        var start = Start(Arriving(_meterReading, _waterBoard));

        start.Started.Should().BeFalse();
        start.Refusal.Should().Contain("'every meter reading'").And.Contain("'everything from the water board'")
            .And.Contain("neither is more specific");
    }

    [Fact]
    public void Three_different_claims_naming_as_many_Things_that_all_fit_refuse_the_start_naming_all_three()
    {
        Published("every meter reading", _arrives, _meterReading);
        Published("everything from the water board", _arrives, _waterBoard);
        Published("the water board's meter readings, however they arrive", _meterReading, _waterBoard);

        var start = Start(Arriving(_meterReading, _waterBoard));

        start.Started.Should().BeFalse();
        start.Refusal.Should().Contain("'every meter reading'").And.Contain("'everything from the water board'")
            .And.Contain("'the water board's meter readings, however they arrive'").And.Contain("none is more specific");
    }

    [Fact]
    public void When_nothing_fits_the_refusal_lists_what_the_start_offered()
    {
        Published("the water board's meter readings", _arrives, _meterReading, _waterBoard);

        var start = Start(Arriving(_meterReading, _otherSystem));

        start.Started.Should().BeFalse();
        start.Refusal.Should().Contain("'arrives'").And.Contain("'meter reading'").And.Contain("'another system'");
    }

    [Fact]
    public void A_start_node_standing_for_the_state_and_its_connection_beats_one_standing_for_the_connection_alone()
    {
        var watches = _fixture.Playing("watches", PipelineRoles.StateWatch);
        var dry = _fixture.Thing("dry");
        _fixture.Rel(_arrives, watches, dry);
        Published("this connection, any state", _arrives);
        var throughThisConnection = Published("dry, through this connection", dry, _arrives);

        PipelineStart.Resolve(_fixture.Build(), _arrives.Id).PipelineId.Should().Be(throughThisConnection.Id);
    }
}
