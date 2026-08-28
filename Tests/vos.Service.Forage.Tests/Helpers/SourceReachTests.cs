using FluentAssertions;
using vos.Service.Forage.Helpers;
using Xunit;

namespace vos.Service.Forage.Tests.Helpers;

// Task #6812 — the walk in the other direction: from a source, through the Places it covers and down
// their nesting, to every site in them. A source added to the catalogue reached no existing site while
// the only walk ran from a site outward, and a site already discovered was never dispatched again.
//
// The sites are told from the Places around them by the mark the platform puts on the archetype every
// site `is` (platform Task #6811). Nothing here names an archetype: named, a model calling its sites
// something else would offer every source to nothing, and the only sign would be a catalogue nobody
// fetched.
public class SourceReachTests
{
    private static ModelBuilder Model() =>
        new ModelBuilder().Archetype("Site", CoveringSourceResolver.SiteArchetypeFlag);

    private static ModelBuilder SiteIn(ModelBuilder model, string site, string place) =>
        model.Relate(site, CoveringSourceResolver.IsPredicateName, "Site")
             .Relate(site, CoveringSourceResolver.IsInPredicate, place);

    // A source is callable only through its registration, so every scenario below wires both.
    private static ModelBuilder SourceCovering(ModelBuilder model, string source, string place) =>
        model.Relate(source, CoveringSourceResolver.CoversPredicate, place)
             .Relate(source, CoveringSourceResolver.ResolvedByPredicate, source + "Endpoint");

    private static IReadOnlyList<CoveringSource> Reach(ModelBuilder model, string source) =>
        CoveringSourceResolver.Reach(model.Build(), model.Id(source));

    private static IEnumerable<string> SubjectsOf(IReadOnlyList<CoveringSource> reach) =>
        reach.Single().Calls.Select(call => call.SubjectName);

    [Fact]
    public void Reach_ASourceCoveringAPlace_IsCalledAboutEverySiteInIt()
    {
        var model = SourceCovering(
            SiteIn(SiteIn(Model(), "WillowBend", "Portugal"), "Oakhollow", "Portugal"),
            "NationalFloodPortal", "Portugal");

        var reach = Reach(model, "NationalFloodPortal");

        reach.Single().SourceId.Should().Be(model.Id("NationalFloodPortal"));
        reach.Single().EndpointName.Should().Be("NationalFloodPortalEndpoint");
        SubjectsOf(reach).Should().Equal("Oakhollow", "WillowBend");
        reach.Single().Calls.Select(call => call.SubjectId)
            .Should().Equal(model.Id("Oakhollow"), model.Id("WillowBend"));
    }

    [Fact]
    public void Reach_ASourceCoveringAContainingPlace_ReachesTheSitesNestedBelowIt()
    {
        var model = SourceCovering(
            SiteIn(Model(), "WillowBend", "Portugal").Relate("Portugal", CoveringSourceResolver.IsInPredicate, "Europe"),
            "OpenMeteo", "Europe");

        SubjectsOf(Reach(model, "OpenMeteo")).Should().Equal("WillowBend");
    }

    // The half of "none for a site another source covers": a site in a Place the source does not
    // cover is not offered it, however many sources cover that other Place.
    [Fact]
    public void Reach_DoesNotReachASiteInAPlaceItDoesNotCover()
    {
        var model = SourceCovering(
            SiteIn(SiteIn(Model(), "WillowBend", "Portugal"), "Oakhollow", "Spain"),
            "NationalFloodPortal", "Portugal");
        SourceCovering(model, "SpanishFloodPortal", "Spain");

        SubjectsOf(Reach(model, "NationalFloodPortal")).Should().Equal("WillowBend");
    }

    // The step the mark exists for: a Place nested inside a covered Place is reached by the same walk
    // that reaches a site, and only the mark tells the two apart.
    [Fact]
    public void Reach_ANestedPlaceIsNotASite()
    {
        var model = SourceCovering(
            Model().Relate("Portugal", CoveringSourceResolver.IsInPredicate, "Europe")
                   .Relate("Lisbon", CoveringSourceResolver.IsInPredicate, "Portugal"),
            "OpenMeteo", "Europe");

        Reach(model, "OpenMeteo").Single().Calls.Should().BeEmpty();
    }

    [Fact]
    public void Reach_ASiteThroughAnIntermediateType_IsStillASite()
    {
        var model = SourceCovering(
            Model().Relate("WillowBend", CoveringSourceResolver.IsPredicateName, "CoastalSite")
                   .Relate("CoastalSite", CoveringSourceResolver.IsPredicateName, "Site")
                   .Archetype("CoastalSite")
                   .Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal"),
            "NationalFloodPortal", "Portugal");

        SubjectsOf(Reach(model, "NationalFloodPortal")).Should().Equal("WillowBend");
    }

    // Left out for the reason the site's walk leaves it out: a source nothing can call is not one the
    // sites are waiting on, and a coverage minted for it would hold every site outstanding for ever.
    [Fact]
    public void Reach_ASourceWithNoRegistration_ReachesNothing()
    {
        var model = SiteIn(Model(), "WillowBend", "Portugal")
            .Relate("NationalFloodPortal", CoveringSourceResolver.CoversPredicate, "Portugal");

        Reach(model, "NationalFloodPortal").Should().BeEmpty();
    }

    // The same rule the site's walk applies: a source that declares what it resolves onto is called
    // about each Thing a site has of it, so the coverages minted are the ones that run will look for.
    [Fact]
    public void Reach_ASourceResolvingOntoAnArchetype_IsCalledAboutWhatEachSiteHasOfIt()
    {
        var model = SourceCovering(
            SiteIn(SiteIn(Model(), "WillowBend", "Portugal"), "Oakhollow", "Portugal"),
            "HazardPortal", "Portugal")
            .Relate("HazardPortal", CoveringSourceResolver.ResolvesOntoPredicate, "HazardAssessment")
            .Archetype("HazardAssessment")
            .Relate("WillowBend", CoveringSourceResolver.HasPredicate, "flood assessment")
            .Relate("flood assessment", CoveringSourceResolver.IsPredicateName, "HazardAssessment")
            .Relate("Oakhollow", CoveringSourceResolver.HasPredicate, "wildfire assessment")
            .Relate("wildfire assessment", CoveringSourceResolver.IsPredicateName, "HazardAssessment")
            .Relate("Oakhollow", CoveringSourceResolver.HasPredicate, "a parcel");

        SubjectsOf(Reach(model, "HazardPortal")).Should().Equal("wildfire assessment", "flood assessment");
    }

    [Fact]
    public void Reach_ASourceResolvingOntoWhatNoSiteHas_KeepsTheSourceWithNoCalls()
    {
        var model = SourceCovering(SiteIn(Model(), "WillowBend", "Portugal"), "HazardPortal", "Portugal")
            .Relate("HazardPortal", CoveringSourceResolver.ResolvesOntoPredicate, "HazardAssessment")
            .Archetype("HazardAssessment");

        Reach(model, "HazardPortal").Single().Calls.Should().BeEmpty();
    }

    [Fact]
    public void Reach_PlaceNestingWithACycle_Terminates()
    {
        var model = SourceCovering(
            SiteIn(Model(), "WillowBend", "Portugal")
                .Relate("Portugal", CoveringSourceResolver.IsInPredicate, "Europe")
                .Relate("Europe", CoveringSourceResolver.IsInPredicate, "Portugal"),
            "OpenMeteo", "Europe");

        SubjectsOf(Reach(model, "OpenMeteo")).Should().Equal("WillowBend");
    }

    [Fact]
    public void Reach_ASourceCoveringSeveralPlacesASiteIsIn_IsCalledAboutItOnce()
    {
        var model = SourceCovering(
            SourceCovering(
                SiteIn(Model(), "WillowBend", "Portugal").Relate("Portugal", CoveringSourceResolver.IsInPredicate, "Europe"),
                "OpenMeteo", "Europe"),
            "OpenMeteo", "Portugal");

        SubjectsOf(Reach(model, "OpenMeteo")).Should().Equal("WillowBend");
    }

    [Fact]
    public void Reach_AModelMarkingNoSiteArchetype_ReachesNoSite()
    {
        var model = SourceCovering(
            new ModelBuilder().Archetype("Site")
                .Relate("WillowBend", CoveringSourceResolver.IsPredicateName, "Site")
                .Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal"),
            "NationalFloodPortal", "Portugal");

        Reach(model, "NationalFloodPortal").Single().Calls.Should().BeEmpty();
    }

    [Fact]
    public void SelectorForSource_WalksCoverageBeforeNestingAndNestingBeforeWhatASiteHolds()
    {
        // Traverse rules compose over the set built so far: the Places have to be in it before their
        // nesting is walked downwards, the sites before what each has, and the subjects before the
        // coverages that apply to them. Only the orderings are pinned, as the site's selector pins its own.
        var selector = CoveringSourceResolver.SelectorForSource(Guid.NewGuid());
        var predicates = selector.Traverse!.Select(rule => rule.Predicate).ToList();

        predicates.IndexOf(CoveringSourceResolver.CoversPredicate)
            .Should().BeLessThan(predicates.IndexOf(CoveringSourceResolver.IsInPredicate));
        predicates.IndexOf(CoveringSourceResolver.IsInPredicate)
            .Should().BeLessThan(predicates.IndexOf(CoveringSourceResolver.HasPredicate));
        predicates.IndexOf(CoveringSourceResolver.HasPredicate)
            .Should().BeLessThan(predicates.IndexOf(CoveringSourceResolver.AppliesToPredicate));

        var nesting = selector.Traverse!.Single(rule => rule.Predicate == CoveringSourceResolver.IsInPredicate);
        nesting.Direction.Should().Be("incoming", "the edge runs site -> place, and the set holds the places");
        nesting.Depth.Should().BeGreaterThan(1);
        selector.Traverse!.Single(rule => rule.Predicate == CoveringSourceResolver.AppliesToPredicate)
            .Direction.Should().Be("incoming");
        selector.IncludeRelationships.Should().BeTrue();
    }

    [Fact]
    public void SelectorForSource_AsksForTheCoverageArchetypeAloneAndEveryPredicateItCompares()
    {
        // Alone, because a run mints against it and needs only its identity; with its members, every
        // coverage in the model would arrive with it. The site archetype needs no asking: every site the
        // nesting reaches brings its own `is` chain with the ancestors the broker closes over.
        var selector = CoveringSourceResolver.SelectorForSource(Guid.NewGuid());

        selector.MarkedArchetypes.Should().Equal(CoveringSourceResolver.SourceCoverageArchetypeFlag);
        selector.MarkedTypes.Should().BeNullOrEmpty();
        selector.Names.Should().Contain(new[]
        {
            CoveringSourceResolver.IsInPredicate,
            CoveringSourceResolver.CoversPredicate,
            CoveringSourceResolver.ResolvedByPredicate,
            CoveringSourceResolver.ResolvesOntoPredicate,
            CoveringSourceResolver.HasPredicate,
            CoveringSourceResolver.IsPredicateName,
            CoveringSourceResolver.AppliesToPredicate,
            CoveringSourceResolver.SourcedFromPredicate,
        });
    }
}
