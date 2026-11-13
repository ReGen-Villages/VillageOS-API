using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Intake.Services;
using Xunit;
using vos.Service.Shared;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// The findings read is rooted at one submitter's site and reaches the archetypes through `is`. Its unit tests
// answer a selector with a walk they reproduce themselves, so only the engine can say which relationships the
// archetypes bring with them: without rules, every other submitter's `is` edge onto the shared archetype.
public class IntakeReadsOneSubmittersFindingsTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public IntakeReadsOneSubmittersFindingsTests(TheEngine engine) => _engine = engine;

    private const string PageSpecification = """
        {"title": "Site submission",
         "sections": [{"title": "The land", "widgets": [
           {"type": "kpi", "title": "Stated area",
            "value": {"kind": "property", "thing": "$scope", "property": "statedAreaHectares"},
            "origin": {"kind": "origin", "property": "statedAreaHectares", "via": [{"predicate": "has", "archetype": "Parcel"}]}},
           {"type": "verdict", "title": "What the analysis says",
            "value": {"kind": "verdict", "via": [{"predicate": "studies", "direction": "in"}],
                      "states": [{"state": "EnergyNetPositive", "reads": "the site makes what it uses"}]}}]}]}
        """;

    private sealed record Submission(Guid Site, Guid Parcel, Guid Study);

    [FactAgainstTheEngine]
    public async Task One_submitters_read_carries_no_relationship_of_another_submitter()
    {
        var isPredicate = await _engine.DeclareOnceAsync("is");
        var hasPredicate = await _engine.DeclareOnceAsync("has");
        var studiesPredicate = await _engine.DeclareOnceAsync("studies");
        var siteArchetype = await _engine.DeclareAsync("Site", isArchetype: true);
        var parcelArchetype = await _engine.DeclareAsync("Parcel", isArchetype: true);
        var studyArchetype = await _engine.DeclareAsync("SiteStudy", isArchetype: true);

        async Task<Submission> SubmittedAsync(string siteName)
        {
            var site = await _engine.DeclareAsync(siteName);
            var parcel = await _engine.DeclareAsync($"{siteName} Parcel-01");
            var study = await _engine.DeclareAsync($"{siteName} Site Study");
            await _engine.RelateAsync(site, isPredicate, siteArchetype);
            await _engine.RelateAsync(parcel, isPredicate, parcelArchetype);
            await _engine.RelateAsync(study, isPredicate, studyArchetype);
            await _engine.RelateAsync(site, hasPredicate, parcel);
            await _engine.RelateAsync(study, studiesPredicate, site);
            return new Submission(site, parcel, study);
        }

        var willowBend = await SubmittedAsync($"Willow Bend {Guid.NewGuid():N}");
        var alderRise = await SubmittedAsync($"Alder Rise {Guid.NewGuid():N}");

        var mycelium = new IntakeMyceliumClient(
            _engine.ClientFactory, NullLogger<IntakeMyceliumClient>.Instance, TheEngine.Url, new LaunchToken(_engine.AdminToken));
        using var reading = await mycelium.ReadAsync(
            FindingsReader.FindingsSelector(willowBend.Site, PageSpecification), CancellationToken.None);

        var relationships = reading.RootElement.GetProperty("relationships").EnumerateArray()
            .Select(edge => (Subject: edge.GetProperty("SubjectId").GetGuid(), Target: edge.GetProperty("TargetId").GetGuid()))
            .ToList();
        var things = reading.RootElement.GetProperty("things").EnumerateArray()
            .Select(thing => thing.GetProperty("Id").GetGuid()).ToList();

        things.Should().Contain([willowBend.Site, willowBend.Parcel, willowBend.Study, studyArchetype]);
        things.Should().NotContain([alderRise.Site, alderRise.Parcel, alderRise.Study]);
        relationships.Should().Contain((willowBend.Study, studyArchetype), "the reader tells what a reached Thing is by its own `is` edge");
        relationships.Should().Contain((willowBend.Site, willowBend.Parcel));
        relationships.Should().Contain((willowBend.Study, willowBend.Site));
        relationships.Should().NotContain(edge =>
            edge.Subject == alderRise.Study || edge.Subject == alderRise.Site || edge.Subject == alderRise.Parcel
            || edge.Target == alderRise.Study || edge.Target == alderRise.Site || edge.Target == alderRise.Parcel);
    }
}
