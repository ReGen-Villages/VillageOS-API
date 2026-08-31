using System.Text.Json;
using FluentAssertions;
using vos.Service.RainwaterHarvest.Services;
using vos.Service.Shared.Subscriptions;
using Xunit;

namespace vos.Service.RainwaterHarvest.Tests;

// What the model says about the demands a harvest serves, read out of one snapshot. Every name here comes
// from the model, so a component this cannot read is a harvest measured against the wrong thing or written
// nowhere — which is why each unreadable shape is refused by name rather than skipped.
public class WaterDemandComponentReaderTests
{
    private static readonly Guid Archetype = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Domestic = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Irrigation = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static SnapshotProperty Value(object value) =>
        new(JsonDocument.Parse(JsonSerializer.Serialize(value)).RootElement, null, null);

    private static Dictionary<string, SnapshotProperty> Component(
        long servingOrder, string demand, string coverage, string shortfall) =>
        new()
        {
            ["servingOrder"] = Value(servingOrder),
            ["demandProperty"] = Value(demand),
            ["coverageProperty"] = Value(coverage),
            ["shortfallProperty"] = Value(shortfall),
        };

    private static Dictionary<string, SnapshotProperty> Domestic1 => Component(
        1, "domesticConsumptionM3PerYear", "pctOfDomesticDemand", "domesticShortfallM3PerYear");

    private static Dictionary<string, SnapshotProperty> Irrigation2 => Component(
        2, "irrigationConsumptionM3PerYear", "pctOfIrrigationDemand", "irrigationShortfallM3PerYear");

    /// <summary>A member whose values are its own, as a template writes them before normalization.</summary>
    private static SnapshotThing Stating(Guid id, string name, Dictionary<string, SnapshotProperty> stated) =>
        new(id, name, false, stated, new Dictionary<string, InheritedPropertySet>(), [], []);

    /// <summary>The same member as a loaded seed holds it: normalization relocates a value stated over the
    /// archetype's declaration into an override, so nothing is left among its own properties.</summary>
    private static SnapshotThing Overriding(Guid id, string name, Dictionary<string, SnapshotProperty> stated) =>
        new(id, name, false, new Dictionary<string, SnapshotProperty>(),
            new Dictionary<string, InheritedPropertySet>
            {
                ["WaterDemandComponent"] = new("WaterDemandComponent", stated, null),
            }, [], []);

    private static SnapshotThing TheArchetype => new(
        Archetype, "WaterDemandComponent", true,
        new Dictionary<string, SnapshotProperty>
        {
            ["__IsWaterDemandComponentArchetype"] = Value(true),
            ["servingOrder"] = Value(0L),
            ["demandProperty"] = Value(string.Empty),
            ["coverageProperty"] = Value(string.Empty),
            ["shortfallProperty"] = Value(string.Empty),
        },
        new Dictionary<string, InheritedPropertySet>(), [], []);

    private static SnapshotDocument Snapshot(params SnapshotThing[] things) => new(0, [.. things], []);

    [Fact]
    public void The_vocabulary_is_selected_by_the_mark_rather_than_by_the_archetypes_name()
    {
        var selector = WaterDemandComponentReader.Selector;

        selector.MarkedTypes.Should().Equal(WaterDemandComponentReader.ComponentArchetypeFlag);
        selector.Names.Should().BeNull();
        selector.Types.Should().BeNull();
    }

    // A member states its values over the archetype's empty declarations, and seed normalization moves
    // them out of its own properties into an override. Read as own properties alone, every component comes
    // back blank — and a blank name is a demand read off nothing and written nowhere.
    [Fact]
    public void A_value_a_component_states_over_its_archetype_is_read_from_where_normalization_put_it()
    {
        var components = WaterDemandComponentReader.Read(Snapshot(
            TheArchetype,
            Overriding(Domestic, "domestic-demand", Domestic1),
            Overriding(Irrigation, "irrigation-demand", Irrigation2)));

        components.Should().HaveCount(2);
        components[0].DemandProperty.Should().Be("domesticConsumptionM3PerYear");
        components[0].CoverageProperty.Should().Be("pctOfDomesticDemand");
        components[1].ShortfallProperty.Should().Be("irrigationShortfallM3PerYear");
    }

    [Fact]
    public void A_component_is_read_the_same_whether_it_states_a_value_as_its_own_or_as_an_override()
    {
        var asOwn = WaterDemandComponentReader.Read(Snapshot(
            TheArchetype, Stating(Domestic, "domestic-demand", Domestic1)));
        var asOverride = WaterDemandComponentReader.Read(Snapshot(
            TheArchetype, Overriding(Domestic, "domestic-demand", Domestic1)));

        asOwn.Should().BeEquivalentTo(asOverride);
    }

    // The mark selects the archetype as well as its members, and the archetype carries the empty
    // declarations. Read as a component it would put a demand of nothing at the front of the queue.
    [Fact]
    public void The_archetype_the_components_hang_under_is_not_itself_a_demand()
    {
        var components = WaterDemandComponentReader.Read(Snapshot(
            TheArchetype, Overriding(Domestic, "domestic-demand", Domestic1)));

        components.Should().ContainSingle().Which.Name.Should().Be("domestic-demand");
    }

    // The order is the model's, and the snapshot's order is not it: a snapshot lists Things in whatever
    // order the selection resolved them.
    [Fact]
    public void The_demands_come_back_in_the_order_the_model_serves_them()
    {
        var components = WaterDemandComponentReader.Read(Snapshot(
            Overriding(Irrigation, "irrigation-demand", Irrigation2),
            TheArchetype,
            Overriding(Domestic, "domestic-demand", Domestic1)));

        components.Select(component => component.Name).Should()
            .Equal("domestic-demand", "irrigation-demand");
    }

    // A model holding no component is not a harvest that covers everything: it is a harvest with nothing
    // to be measured against, and reporting full coverage of nothing would read as a site in good shape.
    [Fact]
    public void A_model_declaring_no_demand_is_refused_and_names_the_mark_it_looked_for()
    {
        var refusal = Assert.Throws<InvalidOperationException>(
            () => WaterDemandComponentReader.Read(Snapshot(TheArchetype)));

        refusal.Message.Should().Contain(WaterDemandComponentReader.ComponentArchetypeFlag);
    }

    [Theory]
    [InlineData("demandProperty")]
    [InlineData("coverageProperty")]
    [InlineData("shortfallProperty")]
    public void A_component_naming_no_property_is_refused_by_its_name_and_the_field_it_left_empty(string field)
    {
        var blank = Domestic1;
        blank[field] = Value(string.Empty);

        var refusal = Assert.Throws<InvalidOperationException>(() => WaterDemandComponentReader.Read(
            Snapshot(TheArchetype, Overriding(Domestic, "domestic-demand", blank))));

        refusal.Message.Should().Contain("domestic-demand").And.Contain(field);
    }

    // A field the component never states at all, rather than one it states as nothing. The archetype
    // declares each of them empty, so a member that says nothing inherits an empty name — and a snapshot
    // carrying neither has to refuse for the same reason rather than read past it.
    [Theory]
    [InlineData("demandProperty")]
    [InlineData("shortfallProperty")]
    public void A_component_that_never_states_a_property_at_all_is_refused_the_same_way(string field)
    {
        var absent = Domestic1;
        absent.Remove(field);

        var refusal = Assert.Throws<InvalidOperationException>(() => WaterDemandComponentReader.Read(
            Snapshot(TheArchetype, Overriding(Domestic, "domestic-demand", absent))));

        refusal.Message.Should().Contain("domestic-demand").And.Contain(field);
    }

    // A name of nothing but spaces is a name of nothing: the study has no such property, so the demand
    // would be sized by nothing or written nowhere exactly as an empty one would.
    [Fact]
    public void A_property_name_of_nothing_but_spaces_is_refused_like_an_empty_one()
    {
        var blank = Domestic1;
        blank["demandProperty"] = Value("   ");

        var refusal = Assert.Throws<InvalidOperationException>(() => WaterDemandComponentReader.Read(
            Snapshot(TheArchetype, Overriding(Domestic, "domestic-demand", blank))));

        refusal.Message.Should().Contain("demandProperty");
    }

    // A Thing carries no name of its own in the platform, and a refusal that named nothing would leave a
    // reader with a model to search by hand. It falls back to the identifier, which is enough to find it.
    [Fact]
    public void A_demand_with_no_name_is_still_identified_in_what_it_is_refused_for()
    {
        var blank = Domestic1;
        blank["demandProperty"] = Value(string.Empty);

        var refusal = Assert.Throws<InvalidOperationException>(() => WaterDemandComponentReader.Read(
            Snapshot(TheArchetype, new SnapshotThing(
                Domestic, null, false, blank, new Dictionary<string, InheritedPropertySet>(), [], []))));

        refusal.Message.Should().Contain(Domestic.ToString());
    }

    // A value of the wrong kind is refused rather than read as absent. Reading the two the same way is
    // how a split silently short by one describes a different parcel (Bug #6576 in land allocation); here
    // it would drop a demand out of the queue and hand its water to the one behind it.
    [Fact]
    public void A_place_in_the_queue_that_is_not_a_number_is_refused_rather_than_read_as_absent()
    {
        var misspelled = Domestic1;
        misspelled["servingOrder"] = Value("first");

        var refusal = Assert.Throws<InvalidOperationException>(() => WaterDemandComponentReader.Read(
            Snapshot(TheArchetype, Overriding(Domestic, "domestic-demand", misspelled))));

        refusal.Message.Should().Contain("domestic-demand").And.Contain("servingOrder");
    }

    [Fact]
    public void A_property_name_that_is_not_text_is_refused_rather_than_read_as_absent()
    {
        var misspelled = Domestic1;
        misspelled["coverageProperty"] = Value(3);

        var refusal = Assert.Throws<InvalidOperationException>(() => WaterDemandComponentReader.Read(
            Snapshot(TheArchetype, Overriding(Domestic, "domestic-demand", misspelled))));

        refusal.Message.Should().Contain("domestic-demand").And.Contain("coverageProperty");
    }

    // A component whose values reach it through more than one type it `is`. Stopping at the first source
    // that does not carry the name would read the rest of the component as blank.
    [Fact]
    public void A_value_is_found_whichever_type_the_component_states_it_over()
    {
        var carried = Domestic1;
        var elsewhere = new Dictionary<string, SnapshotProperty> { ["servingOrder"] = carried["servingOrder"] };
        carried.Remove("servingOrder");

        var components = WaterDemandComponentReader.Read(Snapshot(TheArchetype, new SnapshotThing(
            Domestic, "domestic-demand", false, new Dictionary<string, SnapshotProperty>(),
            new Dictionary<string, InheritedPropertySet>
            {
                ["Somewhere"] = new("Somewhere", elsewhere, null),
                ["WaterDemandComponent"] = new("WaterDemandComponent", carried, null),
            }, [], [])));

        components.Should().ContainSingle().Which.DemandProperty.Should().Be("domesticConsumptionM3PerYear");
    }

    [Fact]
    public void A_component_that_states_no_place_in_the_queue_is_refused_rather_than_served_first()
    {
        var unordered = Domestic1;
        unordered.Remove("servingOrder");

        var refusal = Assert.Throws<InvalidOperationException>(() => WaterDemandComponentReader.Read(
            Snapshot(TheArchetype, Overriding(Domestic, "domestic-demand", unordered))));

        refusal.Message.Should().Contain("domestic-demand").And.Contain("servingOrder");
    }
}
