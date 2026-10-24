using FluentAssertions;
using vos.Service.Forage.Helpers;
using vos.Service.Shared.Subscriptions;
using Xunit;

namespace vos.Service.Forage.Tests.Helpers;

// What a service call names: a site, or a source added to the catalogue, and the two runs are opposites: one fetches and records, the
// other mints and fetches nothing. The model says which the subject is, through the role the platform binds
// to the archetype every site `is` — never by the name of the connection that
// started it, which the two repositories would then agree about only by spelling.
//
// Getting this wrong is quiet in both directions. A site taken for a source is never fetched and is
// stamped as worked out; a source taken for a site is fetched about nothing and stamped the same way.
public class SubjectKindTests
{
    private static ModelBuilder BoundSiteArchetype(ModelBuilder model) =>
        model.Archetype("Site").Bound(CoveringSourceResolver.SiteRole, "Site");

    [Fact]
    public void AThingThatIsTheBoundArchetype_IsASite()
    {
        var model = BoundSiteArchetype(
            new ModelBuilder().Relate("WillowBend", CoveringSourceResolver.IsPredicateName, "Site"));

        CoveringSourceResolver.KindOf(model.Build(), model.Id("WillowBend")).Should().Be(SubjectKind.Site);
    }

    [Fact]
    public void AThingReachingTheBoundArchetypeThroughAnIntermediateType_IsASite()
    {
        var model = BoundSiteArchetype(new ModelBuilder()
            .Relate("WillowBend", CoveringSourceResolver.IsPredicateName, "CoastalSite")
            .Relate("CoastalSite", CoveringSourceResolver.IsPredicateName, "Site")
            .Archetype("CoastalSite"));

        CoveringSourceResolver.KindOf(model.Build(), model.Id("WillowBend")).Should().Be(SubjectKind.Site);
    }

    [Fact]
    public void AThingThatCoversAPlace_IsASource()
    {
        var model = BoundSiteArchetype(new ModelBuilder()
            .Relate("NationalFloodPortal", CoveringSourceResolver.CoversPredicate, "Portugal"));

        CoveringSourceResolver.KindOf(model.Build(), model.Id("NationalFloodPortal"))
            .Should().Be(SubjectKind.Source);
    }

    // A registration is what makes a source callable, so one that covers nothing yet is still a source
    // — offered to no site, and stamped as such rather than called again on every load.
    [Fact]
    public void AThingResolvedByARegistration_IsASource()
    {
        var model = BoundSiteArchetype(new ModelBuilder()
            .Relate("OpenMeteo", CoveringSourceResolver.ResolvedByPredicate, "OpenMeteoEndpoint"));

        CoveringSourceResolver.KindOf(model.Build(), model.Id("OpenMeteo")).Should().Be(SubjectKind.Source);
    }

    // Anything not a site would otherwise be offered to the sites it covers — none — and stamped as
    // worked out. A Place, a study, a connection: stamped, and nothing would say the service call was wrong.
    [Fact]
    public void AThingThatNeitherIsASiteNorCoversAnything_IsNeither()
    {
        var model = BoundSiteArchetype(new ModelBuilder()
            .Relate("WillowBend", CoveringSourceResolver.IsPredicateName, "Site")
            .Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal"));

        CoveringSourceResolver.KindOf(model.Build(), model.Id("Portugal"))
            .Should().Be(SubjectKind.NeitherSiteNorSource);
    }

    // An archetype does not play its own role. Taken for a source, it would be stamped as worked out —
    // and a value on the archetype is inherited by every site, which would take all of them out of the
    // state that starts a run.
    [Fact]
    public void TheBoundArchetypeItself_IsNotASite()
    {
        var model = BoundSiteArchetype(
            new ModelBuilder().Relate("WillowBend", CoveringSourceResolver.IsPredicateName, "Site"));

        CoveringSourceResolver.KindOf(model.Build(), model.Id("Site"))
            .Should().Be(SubjectKind.NeitherSiteNorSource);
    }

    // A model that never read the template declaring the role cannot tell the two apart, and it also
    // cannot have declared the connection that service calls a source — so the honest answer is the one
    // every service call got before there were two kinds. An archetype named Site binds nothing on its own.
    [Fact]
    public void AModelBindingNoSiteArchetype_CannotTell()
    {
        var model = new ModelBuilder()
            .Relate("WillowBend", CoveringSourceResolver.IsPredicateName, "Site")
            .Relate("NationalFloodPortal", CoveringSourceResolver.CoversPredicate, "Portugal")
            .Archetype("Site");

        CoveringSourceResolver.KindOf(model.Build(), model.Id("NationalFloodPortal"))
            .Should().Be(SubjectKind.ModelBindsNoSite);
    }

    // The site role is the whole of how a site is told from a Place, so a model calling the archetype
    // something else keeps answering.
    [Fact]
    public void AnArchetypeOfAnotherNameTheRoleBinds_MakesItsMembersSites()
    {
        var model = new ModelBuilder()
            .Archetype("Holding").Bound(CoveringSourceResolver.SiteRole, "Holding")
            .Relate("WillowBend", CoveringSourceResolver.IsPredicateName, "Holding");

        CoveringSourceResolver.KindOf(model.Build(), model.Id("WillowBend")).Should().Be(SubjectKind.Site);
    }

    [Fact]
    public void KindSelectorFor_AsksForTheSubjectTheSiteRoleWithItsBindingAndThePredicatesItCompares()
    {
        // The role Thing and what it binds, never the archetype's members: asked for with them, every site
        // in the model would arrive to answer a question about one Thing. The subject's own `is` chain comes
        // with the ancestors the broker closes over, so the binding is asked for only so that a model binding
        // no site reads as one rather than as a subject that is not a site.
        var subject = Guid.NewGuid();

        var selector = CoveringSourceResolver.KindSelectorFor(subject);

        selector.Ids.Should().Equal(subject, CoveringSourceResolver.SiteRole.Id);
        selector.Traverse.Should().Equal(RoleBindings.FollowBindings);
        selector.Names.Should().Contain(RoleBindings.BindsPredicateName);
        selector.BoundTypes.Should().BeNullOrEmpty();
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
