using System.Text.Json;
using FluentAssertions;
using vos.Service.Forage.Helpers;
using vos.Service.Shared.Subscriptions;
using Xunit;

namespace vos.Service.Forage.Tests.Helpers;

// The whole resolution is read from the model's declaration (#6809; the declaration is platform User
// Story 6773): the marked archetype names the property its word arrives under, and the shape edge
// names the subject and the predicate. Nothing in the resolver names a vocabulary, so every scenario
// here declares its own and the resolver has never heard of it.
public class DiscoveredVocabularyResolverTests
{
    private readonly Guid _is = Guid.NewGuid();
    private readonly Guid _flowsAs = Guid.NewGuid();
    private readonly Guid _spring = Guid.NewGuid();
    private readonly Guid _flowRegime = Guid.NewGuid();
    private readonly Guid _steady = Guid.NewGuid();
    private readonly Guid _flashy = Guid.NewGuid();
    private readonly Guid _willowBend = Guid.NewGuid();

    private const string Source = "flow-survey";

    private static SnapshotProperty Value<T>(T value) =>
        new(JsonSerializer.SerializeToElement(value), null, null);

    private static SnapshotThing Thing(
        Guid id, string name, bool isArchetype = false,
        Dictionary<string, SnapshotProperty>? properties = null) =>
        new(id, name, isArchetype, properties ?? new(), new(), [], []);

    private static SnapshotRelationship Edge(Guid subject, Guid predicate, Guid target, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), null, subject, predicate, target, new(), new(), []);

    // A spring flows as one regime of a declared set — the same shape Site classifiedAs ClimateZone
    // ships, with nothing of that model's names in it.
    private SnapshotDocument Declared(params SnapshotRelationship[] extraEdges)
    {
        List<SnapshotThing> things =
        [
            Thing(_is, "is"),
            Thing(_flowsAs, "flowsAs"),
            Thing(_spring, "Spring", isArchetype: true),
            Thing(_flowRegime, "FlowRegime", isArchetype: true, new()
            {
                [DiscoveredVocabularyResolver.ArchetypeFlag] = Value(true),
                [DiscoveredVocabularyResolver.ResolvedFromProperty] = Value("flowRegime"),
            }),
            Thing(_steady, "steady"),
            Thing(_flashy, "flashy"),
            Thing(_willowBend, "Willow Bend spring"),
        ];
        List<SnapshotRelationship> edges =
        [
            Edge(_spring, _flowsAs, _flowRegime),
            Edge(_steady, _is, _flowRegime),
            Edge(_flashy, _is, _flowRegime),
            Edge(_willowBend, _is, _spring),
            .. extraEdges,
        ];
        return new SnapshotDocument(0, things, edges);
    }

    private static FetchedWords Fetched(Guid subjectId, string word) =>
        new(subjectId, Source, new Dictionary<string, string> { ["flowRegime"] = word });

    [Fact]
    public void A_fetched_word_becomes_a_planned_edge_to_the_member_it_names()
    {
        var resolution = DiscoveredVocabularyResolver.Resolve(
            Declared(), [Fetched(_willowBend, "steady")]);

        resolution.Unresolved.Should().BeEmpty();
        var edge = resolution.Edges.Should().ContainSingle().Subject;
        edge.SubjectId.Should().Be(_willowBend);
        edge.PredicateId.Should().Be(_flowsAs);
        edge.MemberId.Should().Be(_steady);
        edge.Replaces.Should().BeEmpty();
    }

    [Fact]
    public void A_word_the_vocabulary_does_not_hold_is_reported_with_every_name_a_reader_needs()
    {
        var resolution = DiscoveredVocabularyResolver.Resolve(
            Declared(), [Fetched(_willowBend, "torrential")]);

        resolution.Edges.Should().BeEmpty();
        var unresolved = resolution.Unresolved.Should().ContainSingle().Subject;
        unresolved.Should().Be(new UnresolvedWord(
            Source, "Willow Bend spring", "flowRegime", "torrential", "FlowRegime"));
    }

    // The member names are the scheme's own — `As` and `AS` are different claims — so nothing is
    // resolved by looking almost like a member.
    [Fact]
    public void Matching_is_exact_so_a_case_shifted_word_does_not_resolve()
    {
        var resolution = DiscoveredVocabularyResolver.Resolve(
            Declared(), [Fetched(_willowBend, "Steady")]);

        resolution.Edges.Should().BeEmpty();
        resolution.Unresolved.Should().ContainSingle();
    }

    [Fact]
    public void An_edge_already_pointing_at_the_named_member_plans_nothing()
    {
        var resolution = DiscoveredVocabularyResolver.Resolve(
            Declared(Edge(_willowBend, _flowsAs, _steady)), [Fetched(_willowBend, "steady")]);

        resolution.Edges.Should().BeEmpty();
        resolution.Unresolved.Should().BeEmpty();
    }

    [Fact]
    public void A_changed_word_plans_the_replacement_of_the_stale_edge()
    {
        var stale = Guid.NewGuid();
        var resolution = DiscoveredVocabularyResolver.Resolve(
            Declared(Edge(_willowBend, _flowsAs, _flashy, stale)), [Fetched(_willowBend, "steady")]);

        var edge = resolution.Edges.Should().ContainSingle().Subject;
        edge.MemberId.Should().Be(_steady);
        edge.Replaces.Should().Equal(stale);
    }

    [Fact]
    public void A_subject_reaching_the_shape_archetype_through_an_intermediate_type_still_resolves()
    {
        var mountainSpring = Guid.NewGuid();
        var alpine = Guid.NewGuid();
        var model = Declared(Edge(mountainSpring, _is, _spring), Edge(alpine, _is, mountainSpring));
        model.Things.Add(Thing(mountainSpring, "MountainSpring", isArchetype: true));
        model.Things.Add(Thing(alpine, "Alpine spring"));

        var resolution = DiscoveredVocabularyResolver.Resolve(model, [Fetched(alpine, "flashy")]);

        var edge = resolution.Edges.Should().ContainSingle().Subject;
        edge.SubjectId.Should().Be(alpine);
        edge.MemberId.Should().Be(_flashy);
    }

    // A Thing outside the declared shape carrying the same property name is not a discovered word —
    // the declaration says whose words resolve, so nothing is written and nothing is reported.
    [Fact]
    public void A_subject_the_shape_does_not_name_is_left_alone()
    {
        var reservoir = Guid.NewGuid();
        var model = Declared();
        model.Things.Add(Thing(reservoir, "Willow Bend reservoir"));

        var resolution = DiscoveredVocabularyResolver.Resolve(model, [Fetched(reservoir, "steady")]);

        resolution.Edges.Should().BeEmpty();
        resolution.Unresolved.Should().BeEmpty();
    }

    // Two members answering to one name would resolve one word two ways on two runs, so the word is
    // reported instead of either being picked.
    [Fact]
    public void A_member_name_two_things_carry_resolves_nothing_and_the_word_is_reported()
    {
        var secondSteady = Guid.NewGuid();
        var model = Declared(Edge(secondSteady, _is, _flowRegime));
        model.Things.Add(Thing(secondSteady, "steady"));

        var resolution = DiscoveredVocabularyResolver.Resolve(model, [Fetched(_willowBend, "steady")]);

        resolution.Edges.Should().BeEmpty();
        resolution.Unresolved.Should().ContainSingle();
    }

    // Seed validation refuses the half-declared shape before any model loads; meeting one anyway must
    // resolve nothing rather than guess, and it is the validator's job to have said why.
    [Fact]
    public void A_mark_without_the_word_property_resolves_nothing()
    {
        var model = Declared();
        var stripped = model.Things.Single(thing => thing.Id == _flowRegime);
        stripped.Properties.Remove(DiscoveredVocabularyResolver.ResolvedFromProperty);

        var resolution = DiscoveredVocabularyResolver.Resolve(model, [Fetched(_willowBend, "steady")]);

        resolution.Edges.Should().BeEmpty();
        resolution.Unresolved.Should().BeEmpty();
    }
}
