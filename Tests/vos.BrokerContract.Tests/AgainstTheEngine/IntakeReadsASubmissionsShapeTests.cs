using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Intake.Models;
using vos.Service.Intake.Services;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// Intake reads the archetypes and predicates a submission is built on through their roles, on every save. Its unit
// tests answer any selector with the whole model, so only the engine can say what this read carries.
public class IntakeReadsASubmissionsShapeTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public IntakeReadsASubmissionsShapeTests(TheEngine engine) => _engine = engine;

    [FactAgainstTheEngine]
    public async Task The_shape_read_carries_the_bindings_and_none_of_the_things_built_on_the_archetypes()
    {
        var isPredicate = await _engine.DeclareOnceAsync("is");
        var bound = new Dictionary<Role, Guid>();
        foreach (var role in new[]
                 {
                     LandIntakeRoles.Site, LandIntakeRoles.Parcel, LandIntakeRoles.Project, LandIntakeRoles.PersonalDetail,
                     LandIntakeRoles.ProgrammeAllocation, LandIntakeRoles.HazardAssessment,
                     LandIntakeRoles.SubmittedSource, LandIntakeRoles.Submission,
                 })
        {
            bound[role] = await _engine.DeclareAsync($"{role.Word}Kind", isArchetype: true);
            await _engine.BindAsync(role, bound[role]);
        }
        foreach (var role in new[] { LandIntakeRoles.StudiedSite, LandIntakeRoles.ProposedSite, LandIntakeRoles.ServiceOrder })
        {
            bound[role] = await _engine.DeclareAsync($"{role.Word}Along");
            await _engine.BindAsync(role, bound[role]);
        }
        foreach (var site in new[] { "Willow Bend", "Alder Ford", "Birch Hollow" })
            await _engine.RelateAsync(await _engine.DeclareAsync(site), isPredicate, bound[LandIntakeRoles.Site]);

        var subscriptions = new SubscriptionClient(
            _engine.ClientFactory, NullLogger<SubscriptionClient>.Instance, TheEngine.Url, _engine.AdminToken);
        var subscribed = await subscriptions.SubscribeAsync(SubmissionShapeReader.Selector());
        await subscriptions.UnsubscribeAsync(subscribed.SubscriptionId);

        var shape = SubmissionShapeReader.Read(subscribed.Snapshot);

        shape.Archetypes.Site.Should().Be(new DeclaredTerm("siteKind", bound[LandIntakeRoles.Site]));
        shape.Studies.Id.Should().Be(bound[LandIntakeRoles.StudiedSite]);
        subscribed.Snapshot.Relationships.Should().NotContain(edge => edge.PredicateId == isPredicate,
            "a Thing built on an archetype is not part of what the archetype is");
    }
}
