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
            _things.Add(Thing(id, name, Values(properties)));
            return this;
        }

        /// <summary>The same Thing as a submission leaves it. A value written for a name the Thing's
        /// archetype declares — which every figure a submission sends is — is stored as an override under
        /// that archetype rather than among the Thing's own properties, so this is the shape the reader
        /// actually meets on a live model and <see cref="With"/> is the shape a seeded template mints.</summary>
        public ModelBuilder Stating(string name, params (string Key, object Value)[] properties)
        {
            var id = Id(name);
            _things.RemoveAll(thing => thing.Id == id);
            _things.Add(new SnapshotThing(id, name, false, new Dictionary<string, SnapshotProperty>(),
                new Dictionary<string, InheritedPropertySet>
                {
                    [$"{name}-archetype"] = new($"{name}-archetype", Values(properties), null),
                },
                [], []));
            return this;
        }

        private static Dictionary<string, SnapshotProperty> Values((string Key, object Value)[] properties) =>
            properties.ToDictionary(
                property => property.Key,
                property => new SnapshotProperty(
                    JsonDocument.Parse(JsonSerializer.Serialize(property.Value)).RootElement, null, null));

        public ModelBuilder Relate(string subject, string predicate, string target)
        {
            _edges.Add(new SnapshotRelationship(Guid.NewGuid(), null, Id(subject), Id(predicate), Id(target),
                new Dictionary<string, SnapshotProperty>(), null, []));
            return this;
        }

        /// <summary>Strip a Thing's name, keeping the id it is already related through. The snapshot
        /// carries a name the platform always sets, so a null one reaches the reader only from a payload
        /// that omitted it — which the reader still has to answer for.</summary>
        public ModelBuilder Nameless(string name)
        {
            var id = Id(name);
            var existing = _things.Single(thing => thing.Id == id);
            _things.Remove(existing);
            _things.Add(existing with { Name = null });
            return this;
        }

        public SnapshotDocument Build() => new(0, _things, _edges);

        private static SnapshotThing Thing(Guid id, string name, Dictionary<string, SnapshotProperty> properties) =>
            new(id, name, false, properties, null, [], []);
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

    /// <summary>The same site as a submission leaves it (#6805). Every figure a submission sends is written
    /// for a name its archetype declares, so the model stores it as an override and the Thing's own
    /// properties are empty. Read from own properties alone, the parcel is not a parcel and an allocation
    /// is not an allocation: the split comes back empty, the study is reported as reaching no parcel, and
    /// nothing is left in what the split was read from for a later change to re-drive.</summary>
    [Fact]
    public void The_split_reads_what_a_submitted_site_states_over_its_archetypes_declarations()
    {
        var model = new ModelBuilder()
            .With("categorizedAs", (ProgrammeSplitReader.CategoryFlag, true))
            .With("residential", (ProgrammeSplitReader.BuiltFootprintFlag, true))
            .With("food-and-agriculture", (ProgrammeSplitReader.ProductiveFootprintFlag, true))
            .Stating("parcel", (ProgrammeSplitReader.ParcelAreaProperty, 28.39))
            .Stating("housing", (ProgrammeSplitReader.SharePctProperty, 40.0))
            .Stating("growing", (ProgrammeSplitReader.SharePctProperty, 60.0))
            .Relate("study", "studies", "WillowBend")
            .Relate("WillowBend", "has", "parcel")
            .Relate("WillowBend", "has", "housing")
            .Relate("WillowBend", "has", "growing")
            .Relate("housing", "categorizedAs", "residential")
            .Relate("growing", "categorizedAs", "food-and-agriculture");

        var split = ProgrammeSplitReader.Read(model.Build(), model.Id("study"));

        split.ParcelAreaHectares.Should().Be(28.39);
        split.Categories.Select(category => category.Name)
            .Should().BeEquivalentTo("residential", "food-and-agriculture");
        split.ReadsFrom.Should().BeEquivalentTo(
            [model.Id("parcel"), model.Id("housing"), model.Id("growing")]);
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
        split.ParcelAreaHectares.Should().BeNull();
        split.ReadsFrom.Should().BeEmpty();
    }

    // Bug 6767: no parcel and a parcel of nought hectares are different answers, and reading both as
    // nought let a site nobody had described report a definite food shortfall. The reader says which it
    // found; what to do about it is the handler's.
    [Fact]
    public void A_site_holding_no_parcel_reads_as_no_area_rather_than_nought_hectares()
    {
        var model = new ModelBuilder().With("study").With("site")
            .Relate("study", "studies", "site");

        var split = ProgrammeSplitReader.Read(model.Build(), model.Id("study"));

        split.ParcelAreaHectares.Should().BeNull();
    }

    [Fact]
    public void A_parcel_that_states_nought_hectares_reads_as_nought()
    {
        var model = WillowBend().With("parcel", (ProgrammeSplitReader.ParcelAreaProperty, 0.0));

        var split = ProgrammeSplitReader.Read(model.Build(), model.Id("study"));

        split.ParcelAreaHectares.Should().Be(0);
    }

    [Fact]
    public void A_share_written_as_text_is_read_the_same_whatever_the_regional_format()
    {
        var model = WillowBend().With("housing", (ProgrammeSplitReader.SharePctProperty, "40.5"));

        var split = ProgrammeSplitReader.Read(model.Build(), model.Id("study"));

        split.Categories.Single(category => category.Name == "residential").SharePct.Should().Be(40.5);
    }

    // A share that is present and unreadable is not a Thing that turns out not to be an allocation. Reading
    // the two the same way divides the parcel between whatever is left and reports it as a whole split
    // (Bug #6576), where the balance beside this one refuses an input it cannot read and names it.
    [Fact]
    public void A_share_that_cannot_be_read_is_refused_by_name_rather_than_dropped()
    {
        var model = WillowBend().With("housing", (ProgrammeSplitReader.SharePctProperty, "two fifths"));

        var refusal = Assert.Throws<InvalidOperationException>(
            () => ProgrammeSplitReader.Read(model.Build(), model.Id("study")));

        refusal.Message.Should().Contain(ProgrammeSplitReader.SharePctProperty).And.Contain("housing");
    }

    // Text that does not parse is one way to be unreadable; a value of another shape altogether is the
    // other, and both are the property being present and saying nothing the split can use.
    [Fact]
    public void A_share_that_is_not_a_figure_at_all_is_refused_the_same_way()
    {
        var model = WillowBend().With("housing", (ProgrammeSplitReader.SharePctProperty, true));

        var refusal = Assert.Throws<InvalidOperationException>(
            () => ProgrammeSplitReader.Read(model.Build(), model.Id("study")));

        refusal.Message.Should().Contain(ProgrammeSplitReader.SharePctProperty);
    }

    [Fact]
    public void A_parcel_area_that_cannot_be_read_is_refused_rather_than_read_as_no_area()
    {
        var model = WillowBend().With("parcel", (ProgrammeSplitReader.ParcelAreaProperty, "twenty-four"));

        var refusal = Assert.Throws<InvalidOperationException>(
            () => ProgrammeSplitReader.Read(model.Build(), model.Id("study")));

        refusal.Message.Should().Contain(ProgrammeSplitReader.ParcelAreaProperty).And.Contain("parcel");
    }

    // A Thing beside the allocations that simply has no share is what the reader is walking past, and it
    // stays silent for that.
    [Fact]
    public void A_Thing_the_site_holds_that_is_no_allocation_is_walked_past()
    {
        var model = WillowBend().With("hazard-assessment", ("hazardLevel", "low"));
        model.Relate("WillowBend", "has", "hazard-assessment");

        var split = ProgrammeSplitReader.Read(model.Build(), model.Id("study"));

        split.Categories.Should().HaveCount(2);
        split.Uncategorised.Should().BeEmpty();
    }

    // A name is what a refusal has to give an operator to find the allocation with, and the platform always
    // sets one. If a payload arrives without it the id has to stand in, because a refusal naming nothing
    // leaves the split unfixable.
    [Fact]
    public void An_allocation_with_no_name_is_reported_as_the_gap_it_is_by_its_id()
    {
        var model = WillowBend().With("orphan", (ProgrammeSplitReader.SharePctProperty, 15.0)).Nameless("orphan");
        model.Relate("WillowBend", "has", "orphan");

        var split = ProgrammeSplitReader.Read(model.Build(), model.Id("study"));

        split.Uncategorised.Should().ContainSingle().Which.Should().Be(model.Id("orphan").ToString());
    }

    // Two nameless categories would otherwise share one key, and the second would take the first's area.
    [Fact]
    public void A_category_with_no_name_is_told_from_another_by_its_id()
    {
        var model = WillowBend()
            .With("first-unnamed").Nameless("first-unnamed")
            .With("second-unnamed").Nameless("second-unnamed")
            .With("north", (ProgrammeSplitReader.SharePctProperty, 10.0))
            .With("south", (ProgrammeSplitReader.SharePctProperty, 30.0));
        model.Relate("WillowBend", "has", "north").Relate("north", "categorizedAs", "first-unnamed")
             .Relate("WillowBend", "has", "south").Relate("south", "categorizedAs", "second-unnamed");

        var split = ProgrammeSplitReader.Read(model.Build(), model.Id("study"));

        split.Categories.Select(category => category.Name).Should().Contain(new[]
        {
            model.Id("first-unnamed").ToString(), model.Id("second-unnamed").ToString(),
        });
    }

    // The predicate names the reader has to match are the two structural ones. A predicate Thing the
    // snapshot does not carry cannot be either of them, and an unnamed one matches nothing.
    [Fact]
    public void An_edge_through_an_unnamed_predicate_reaches_no_site()
    {
        var model = WillowBend().Nameless("studies");

        var split = ProgrammeSplitReader.Read(model.Build(), model.Id("study"));

        split.Categories.Should().BeEmpty();
        split.ReadsFrom.Should().BeEmpty();
    }

    // A scoped snapshot holds the Things the traversal reached, and an edge can name a predicate that is
    // not among them. It cannot be one of the two the reader matches by name, so it reaches nothing.
    [Fact]
    public void An_edge_naming_a_predicate_the_snapshot_does_not_carry_reaches_nothing()
    {
        var model = WillowBend();
        var absent = model.Build() with
        {
            Relationships = [.. model.Build().Relationships, new SnapshotRelationship(
                Guid.NewGuid(), null, model.Id("WillowBend"), Guid.NewGuid(), model.Id("parcel"),
                new Dictionary<string, SnapshotProperty>(), new Dictionary<string, InheritedPropertySet>(), [])],
        };

        var split = ProgrammeSplitReader.Read(absent, model.Id("study"));

        split.ReadsFrom.Should().BeEquivalentTo(new[]
        {
            model.Id("parcel"), model.Id("housing"), model.Id("growing"),
        });
    }

    // The site holds more than its parcel and its allocations, and only what hangs off it by `has` is the
    // split. An edge from the site through any other predicate is not walked.
    [Fact]
    public void An_edge_from_the_site_through_another_predicate_is_not_part_of_the_split()
    {
        var model = WillowBend().With("shadow", (ProgrammeSplitReader.SharePctProperty, 90.0));
        model.Relate("WillowBend", "wasSurveyedBy", "shadow");

        var split = ProgrammeSplitReader.Read(model.Build(), model.Id("study"));

        split.ReadsFrom.Should().NotContain(model.Id("shadow"));
        split.Categories.Should().HaveCount(2);
    }

    [Fact]
    public void The_selector_asks_for_the_category_by_mark_and_the_structural_predicates_by_name()
    {
        var selector = ProgrammeSplitReader.SelectorFor(Guid.NewGuid());

        selector.Traverse!.Should().Contain(rule => rule.PredicateFlag == ProgrammeSplitReader.CategoryFlag);
        selector.Names.Should().Contain(new[] { ProgrammeSplitReader.StudiesPredicate, ProgrammeSplitReader.HasPredicate });
    }
}
