using System.Text.Json;
using FluentAssertions;
using vos.Service.LandAllocation.Services;
using vos.Service.Shared.Subscriptions;
using Xunit;

namespace vos.Service.LandAllocation.Tests;

// Turning a model subgraph into a split: study → site → its parcel and its allocations, and from each
// allocation the category Thing it names. The category is followed by the flag the model marks its
// predicate with, so nothing here depends on what that predicate is called.
public class ProgrammeSplitReaderTests
{
    private sealed class ModelBuilder
    {
        private readonly Dictionary<string, Guid> _ids = new(StringComparer.Ordinal);
        private readonly List<SnapshotThing> _things = new();
        private readonly List<SnapshotRelationship> _edges = new();

        public Guid Id(string name)
        {
            if (_ids.TryGetValue(name, out var existing)) return existing;
            var id = Guid.NewGuid();
            _ids[name] = id;
            _things.Add(Thing(id, name, new Dictionary<string, SnapshotProperty>()));
            return id;
        }

        public ModelBuilder With(string name, params (string Key, object Value)[] properties)
        {
            var id = Id(name);
            _things.RemoveAll(thing => thing.Id == id);
            _things.Add(Thing(id, name, properties.ToDictionary(
                property => property.Key,
                property => new SnapshotProperty(
                    JsonDocument.Parse(JsonSerializer.Serialize(property.Value)).RootElement, null, null))));
            return this;
        }

        public ModelBuilder Relate(string subject, string predicate, string target)
        {
            _edges.Add(new SnapshotRelationship(Guid.NewGuid(), null, Id(subject), Id(predicate), Id(target),
                new Dictionary<string, SnapshotProperty>(), new Dictionary<string, InheritedPropertySet>(), []));
            return this;
        }

        public SnapshotDocument Build() => new(0, _things, _edges);

        private static SnapshotThing Thing(Guid id, string name, Dictionary<string, SnapshotProperty> properties) =>
            new(id, name, false, properties, new Dictionary<string, InheritedPropertySet>(), [], []);
    }

    // A site with a parcel and two allocations, each naming a marked category.
    private static ModelBuilder WillowBend()
    {
        var model = new ModelBuilder()
            .With("categorizedAs", (ProgrammeSplitReader.CategoryFlag, true))
            .With("residential", (ProgrammeSplitReader.BuiltFootprintFlag, true))
            .With("food-and-agriculture", (ProgrammeSplitReader.ProductiveFootprintFlag, true))
            .With("parcel", (ProgrammeSplitReader.ParcelAreaProperty, 24.0))
            .With("housing", (ProgrammeSplitReader.SharePctProperty, 40.0))
            .With("growing", (ProgrammeSplitReader.SharePctProperty, 60.0));

        return model
            .Relate("study", "studies", "WillowBend")
            .Relate("WillowBend", "has", "parcel")
            .Relate("WillowBend", "has", "housing")
            .Relate("WillowBend", "has", "growing")
            .Relate("housing", "categorizedAs", "residential")
            .Relate("growing", "categorizedAs", "food-and-agriculture");
    }

    [Fact]
    public void The_split_reaches_the_parcel_and_every_allocation_through_the_site()
    {
        var model = WillowBend();

        var split = ProgrammeSplitReader.Read(model.Build(), model.Id("study"));

        split.ParcelAreaHectares.Should().Be(24.0);
        split.Categories.Select(category => category.Name).Should().BeEquivalentTo("residential", "food-and-agriculture");
        split.Categories.Single(c => c.Name == "residential").IsBuilt.Should().BeTrue();
        split.Categories.Single(c => c.Name == "food-and-agriculture").IsProductive.Should().BeTrue();
    }

    [Fact]
    public void The_category_is_followed_by_its_mark_whatever_the_predicate_is_called()
    {
        var model = WillowBend();
        // The same model with the predicate renamed. Nothing about the answer may change.
        var renamed = new ModelBuilder()
            .With("belongsTo", (ProgrammeSplitReader.CategoryFlag, true))
            .With("residential", (ProgrammeSplitReader.BuiltFootprintFlag, true))
            .With("parcel", (ProgrammeSplitReader.ParcelAreaProperty, 10.0))
            .With("housing", (ProgrammeSplitReader.SharePctProperty, 100.0))
            .Relate("study", "studies", "WillowBend")
            .Relate("WillowBend", "has", "parcel")
            .Relate("WillowBend", "has", "housing")
            .Relate("housing", "belongsTo", "residential");

        var split = ProgrammeSplitReader.Read(renamed.Build(), renamed.Id("study"));

        split.Categories.Single().Name.Should().Be("residential");
        split.Categories.Single().IsBuilt.Should().BeTrue();
    }

    [Fact]
    public void An_unmarked_edge_from_the_allocation_is_not_mistaken_for_its_category()
    {
        // An allocation can point at more than one Thing, and the unmarked edge can come first. Following
        // whatever it points at would read a note as the category and mark the parcel by it.
        var model = new ModelBuilder()
            .With("categorizedAs", (ProgrammeSplitReader.CategoryFlag, true))
            .With("residential", (ProgrammeSplitReader.BuiltFootprintFlag, true))
            .With("parcel", (ProgrammeSplitReader.ParcelAreaProperty, 10.0))
            .With("housing", (ProgrammeSplitReader.SharePctProperty, 100.0))
            .With("someNote")
            .Relate("study", "studies", "WillowBend")
            .Relate("WillowBend", "has", "parcel")
            .Relate("WillowBend", "has", "housing")
            .Relate("housing", "recordedBy", "someNote")
            .Relate("housing", "categorizedAs", "residential");

        var split = ProgrammeSplitReader.Read(model.Build(), model.Id("study"));

        split.Categories.Single().Name.Should().Be("residential");
        split.Categories.Single().IsBuilt.Should().BeTrue();
        split.Uncategorised.Should().BeEmpty();
    }

    [Fact]
    public void A_footprint_flag_set_to_false_is_a_category_declining_the_role()
    {
        // A flag is a property, so it can hold false. That says "not this role", which is different from
        // silence and must not read as marked.
        var model = WillowBend().With("residential", (ProgrammeSplitReader.BuiltFootprintFlag, false));

        var split = ProgrammeSplitReader.Read(model.Build(), model.Id("study"));

        split.Categories.Single(category => category.Name == "residential").IsBuilt.Should().BeFalse();
    }

    [Fact]
    public void An_allocation_naming_no_category_is_carried_as_a_gap_not_dropped()
    {
        // Allocating the rest would silently describe a different parcel than the one submitted.
        var model = WillowBend().With("orphan", (ProgrammeSplitReader.SharePctProperty, 15.0));
        model.Relate("WillowBend", "has", "orphan");

        var split = ProgrammeSplitReader.Read(model.Build(), model.Id("study"));

        split.Uncategorised.Should().ContainSingle().Which.Should().Be("orphan");
        split.Categories.Should().HaveCount(2);
    }

    [Fact]
    public void A_category_marked_for_neither_footprint_is_still_read()
    {
        var model = WillowBend()
            .With("green-water-and-restoration")
            .With("greenspace", (ProgrammeSplitReader.SharePctProperty, 10.0));
        model.Relate("WillowBend", "has", "greenspace")
             .Relate("greenspace", "categorizedAs", "green-water-and-restoration");

        var split = ProgrammeSplitReader.Read(model.Build(), model.Id("study"));

        var green = split.Categories.Single(category => category.Name == "green-water-and-restoration");
        green.IsBuilt.Should().BeFalse();
        green.IsProductive.Should().BeFalse();
        split.Uncategorised.Should().BeEmpty();
    }

    [Fact]
    public void The_Things_it_read_from_are_the_allocations_and_the_parcel_not_the_study()
    {
        // What the follower is asked to watch. Naming the study here would watch what this service
        // writes rather than what it reads, and a share moving would change nothing.
        var model = WillowBend();

        var split = ProgrammeSplitReader.Read(model.Build(), model.Id("study"));

        split.ReadsFrom.Should().BeEquivalentTo(new[]
        {
            model.Id("parcel"), model.Id("housing"), model.Id("growing"),
        });
    }

    [Fact]
    public void A_study_that_reaches_no_site_reads_as_an_empty_split()
    {
        var model = new ModelBuilder().With("study");

        var split = ProgrammeSplitReader.Read(model.Build(), model.Id("study"));

        split.Categories.Should().BeEmpty();
        split.ParcelAreaHectares.Should().Be(0);
        split.ReadsFrom.Should().BeEmpty();
    }

    [Fact]
    public void A_share_written_as_text_is_read_the_same_whatever_the_regional_format()
    {
        var model = WillowBend().With("housing", (ProgrammeSplitReader.SharePctProperty, "40.5"));

        var split = ProgrammeSplitReader.Read(model.Build(), model.Id("study"));

        split.Categories.Single(category => category.Name == "residential").SharePct.Should().Be(40.5);
    }

    [Fact]
    public void The_selector_asks_for_the_category_by_mark_and_the_structural_predicates_by_name()
    {
        var selector = ProgrammeSplitReader.SelectorFor(Guid.NewGuid());

        selector.Traverse!.Should().Contain(rule => rule.PredicateFlag == ProgrammeSplitReader.CategoryFlag);
        selector.Names.Should().Contain(new[] { ProgrammeSplitReader.StudiesPredicate, ProgrammeSplitReader.HasPredicate });
    }
}
