using FluentAssertions;
using vos.Service.Intake.Services;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;
using Xunit;

namespace vos.Service.Intake.Tests;

public class SubmissionShapeReaderTests
{
    [Fact]
    public void The_read_asks_for_every_role_a_submission_is_composed_against_and_follows_its_binding()
    {
        var selector = SubmissionShapeReader.Selector();

        selector.Ids.Should().BeEquivalentTo(RoleBindings.Identifiers(
            LandIntakeRoles.Site, LandIntakeRoles.Parcel, LandIntakeRoles.Project, LandIntakeRoles.PersonalDetail,
            LandIntakeRoles.ProgrammeAllocation, LandIntakeRoles.HazardAssessment, LandIntakeRoles.SubmittedSource,
            LandIntakeRoles.Submission, LandIntakeRoles.StudiedSite, LandIntakeRoles.ProposedSite,
            LandIntakeRoles.ServiceOrder));
        selector.Names.Should().Contain(RoleBindings.BindsPredicateName);
        selector.Traverse.Should().ContainEquivalentOf(RoleBindings.FollowBindings);
    }

    [Fact]
    public void Each_archetype_and_predicate_is_the_Thing_its_role_binds()
    {
        var model = DeclaredModel.Seeded();

        var shape = SubmissionShapeReader.Read(model.Build());

        shape.Archetypes.Site.Should().Be(model.Id("Site"));
        shape.Archetypes.Parcel.Should().Be(model.Id("Parcel"));
        shape.Archetypes.Project.Should().Be(model.Id("Project"));
        shape.Archetypes.Contact.Should().Be(model.Id("Contact"));
        shape.Archetypes.ProgrammeAllocation.Should().Be(model.Id("ProgrammeAllocation"));
        shape.Archetypes.HazardAssessment.Should().Be(model.Id("HazardAssessment"));
        shape.Archetypes.SubmittedSource.Should().Be(model.Id("SubmittedSource"));
        shape.Archetypes.Submission.Should().Be(model.Id("Submission"));
        shape.Studies.Id.Should().Be(model.Id("studies"));
        shape.Proposes.Id.Should().Be(model.Id("proposes"));
        shape.ServedAfter.Id.Should().Be(model.Id("servedAfter"));
        shape.Studies.Minted.Should().BeFalse("a predicate a role binds is the model's own");
    }
}
