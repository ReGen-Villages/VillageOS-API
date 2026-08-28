using FluentAssertions;
using vos.Service.Forage.Helpers;
using Xunit;

namespace vos.Service.Forage.Tests.Helpers;

// Task #6812 — what a dispatch names. A run used to be about a site and nothing else; now a source
// added to the catalogue is dispatched too, and the two runs are opposites: one fetches and records, the
// other mints and fetches nothing. The model says which the subject is, by the mark the platform puts on
// the archetype every site `is` (platform Task #6811) — never by the name of the connection that
// dispatched it, which the two repositories would then agree about only by spelling.
//
// Getting this wrong is quiet in both directions. A site taken for a source is never fetched and is
// stamped as worked out; a source taken for a site is fetched about nothing and stamped the same way.
public class SubjectKindTests
{
    private static ModelBuilder MarkedSiteArchetype(ModelBuilder model) =>
        model.Archetype("Site", CoveringSourceResolver.SiteArchetypeFlag);

    [Fact]
    public void AThingThatIsTheMarkedArchetype_IsASite()
    {
        var model = MarkedSiteArchetype(
            new ModelBuilder().Relate("WillowBend", CoveringSourceResolver.IsPredicateName, "Site"));

        CoveringSourceResolver.KindOf(model.Build(), model.Id("WillowBend")).Should().Be(SubjectKind.Site);
    }

    [Fact]
    public void AThingReachingTheMarkThroughAnIntermediateType_IsASite()
    {
        var model = MarkedSiteArchetype(new ModelBuilder()
            .Relate("WillowBend", CoveringSourceResolver.IsPredicateName, "CoastalSite")
            .Relate("CoastalSite", CoveringSourceResolver.IsPredicateName, "Site")
            .Archetype("CoastalSite"));

        CoveringSourceResolver.KindOf(model.Build(), model.Id("WillowBend")).Should().Be(SubjectKind.Site);
    }

    [Fact]
    public void AThingThatCoversAPlace_IsASource()
    {
        var model = MarkedSiteArchetype(new ModelBuilder()
            .Relate("NationalFloodPortal", CoveringSourceResolver.CoversPredicate, "Portugal"));

        CoveringSourceResolver.KindOf(model.Build(), model.Id("NationalFloodPortal"))
            .Should().Be(SubjectKind.Source);
    }

    // A registration is what makes a source callable, so one that covers nothing yet is still a source
    // — offered to no site, and stamped as such rather than dispatched again on every load.
    [Fact]
    public void AThingResolvedByARegistration_IsASource()
    {
        var model = MarkedSiteArchetype(new ModelBuilder()
            .Relate("OpenMeteo", CoveringSourceResolver.ResolvedByPredicate, "OpenMeteoEndpoint"));

        CoveringSourceResolver.KindOf(model.Build(), model.Id("OpenMeteo")).Should().Be(SubjectKind.Source);
    }

    // Anything not a site would otherwise be offered to the sites it covers — none — and stamped as
    // worked out. A Place, a study, a connection: stamped, and nothing would say the dispatch was wrong.
    [Fact]
    public void AThingThatNeitherIsASiteNorCoversAnything_IsNeither()
    {
        var model = MarkedSiteArchetype(new ModelBuilder()
            .Relate("WillowBend", CoveringSourceResolver.IsPredicateName, "Site")
            .Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal"));

        CoveringSourceResolver.KindOf(model.Build(), model.Id("Portugal"))
            .Should().Be(SubjectKind.NeitherSiteNorSource);
    }

    // An archetype does not play its own role. Taken for a source, it would be stamped as worked out —
    // and a value on the archetype is inherited by every site, which would take all of them out of the
    // state a run is dispatched by.
    [Fact]
    public void TheMarkedArchetypeItself_IsNotASite()
    {
        var model = MarkedSiteArchetype(
            new ModelBuilder().Relate("WillowBend", CoveringSourceResolver.IsPredicateName, "Site"));

        CoveringSourceResolver.KindOf(model.Build(), model.Id("Site"))
            .Should().Be(SubjectKind.NeitherSiteNorSource);
    }

    // A model that never read the template carrying the mark cannot tell the two apart, and it also
    // cannot have declared the connection that dispatches a source — so the honest answer is the one
    // every dispatch got before there were two kinds.
    [Fact]
    public void AModelMarkingNoSiteArchetype_CannotTell()
    {
        var model = new ModelBuilder()
            .Relate("WillowBend", CoveringSourceResolver.IsPredicateName, "Site")
            .Relate("NationalFloodPortal", CoveringSourceResolver.CoversPredicate, "Portugal")
            .Archetype("Site");

        CoveringSourceResolver.KindOf(model.Build(), model.Id("NationalFloodPortal"))
            .Should().Be(SubjectKind.ModelMarksNoSite);
    }

    [Fact]
    public void KindSelectorFor_AsksForTheSubjectTheSiteArchetypeAloneAndThePredicatesItCompares()
    {
        // The archetype alone: asked for with its members, every site in the model would arrive to
        // answer a question about one Thing. The subject's own `is` chain comes with the ancestors the
        // broker closes over, so the archetype is asked for only so that a model without the mark reads
        // as one rather than as a subject that is not a site.
        var subject = Guid.NewGuid();

        var selector = CoveringSourceResolver.KindSelectorFor(subject);

        selector.Ids.Should().Equal(subject);
        selector.MarkedArchetypes.Should().Equal(CoveringSourceResolver.SiteArchetypeFlag);
        selector.MarkedTypes.Should().BeNullOrEmpty();
        selector.Names.Should().Contain(new[]
        {
            CoveringSourceResolver.IsPredicateName,
            CoveringSourceResolver.CoversPredicate,
            CoveringSourceResolver.ResolvedByPredicate,
        });
        selector.IncludeIsAncestors.Should().BeTrue();
        selector.IncludeRelationships.Should().BeTrue();
    }
}
