using FluentAssertions;
using vos.Service.Forage.Helpers;
using Xunit;

namespace vos.Service.Forage.Tests.Helpers;

// Task #6789 — what a run reads about coverage already recorded. A site used to be discovered exactly
// once ever, because the state a run was dispatched by counted `observed` edges and nothing removes
// one. The platform now declares a `SourceCoverage` per subject and source (#6779); this is the half
// that reads them, so a run asks only the calls whose answer is not already in.
//
// A coverage is about the **subject of a call**, not the site. A source that resolves onto an archetype
// is called once per Thing the site has of it, so a portal answering for five of a site's assessments
// and not the sixth has to leave five resolved and one outstanding. Recorded per source, the sixth
// would either re-call all six or be lost.
//
// Every failure here reports itself as an absence. A coverage read as unresolved when it resolved calls
// a provider that already answered; read as resolved when it did not, the source is never asked again
// and its value is missing with nothing saying why.
public class RecordedCoverageTests
{
    private const string ResolvedAt = "resolvedAt";
    private const string Attempts = "attempts";

    private static ModelBuilder CoverageOf(string coverage, string subject, string source)
        => new ModelBuilder()
            .Relate(coverage, CoveringSourceResolver.AppliesToPredicate, subject)
            .Relate(coverage, CoveringSourceResolver.SourcedFromPredicate, source);

    [Fact]
    public void AModelRecordingNoCoverage_ReadsNone()
    {
        var model = new ModelBuilder().Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal");

        CoveringSourceResolver.RecordedCoverageIn(model.Build()).Should().BeEmpty();
    }

    [Fact]
    public void ACoverageReachesTheSubjectItIsAboutAndTheSourceItReads()
    {
        var model = CoverageOf("WillowBend/climate", "WillowBend", "Mapresso");

        var only = CoveringSourceResolver.RecordedCoverageIn(model.Build()).Should().ContainSingle().Subject;

        only.CoverageId.Should().Be(model.Id("WillowBend/climate"));
        only.SubjectId.Should().Be(model.Id("WillowBend"));
        only.SourceId.Should().Be(model.Id("Mapresso"));
    }

    // The whole of "a call that already answered is not made again": the answer landing takes one
    // coverage out of the outstanding set, one call at a time.
    [Fact]
    public void ACoverageCarryingTheInstantItsSourceAnsweredIsResolved()
    {
        var model = CoverageOf("WillowBend/climate", "WillowBend", "Mapresso")
            .Carrying("WillowBend/climate", ResolvedAt, "2026-08-27T09:00:00Z");

        CoveringSourceResolver.RecordedCoverageIn(model.Build())
            .Should().ContainSingle().Which.Resolved.Should().BeTrue();
    }

    [Fact]
    public void ACoverageWhoseSourceHasNotAnsweredIsOutstanding()
    {
        var model = CoverageOf("WillowBend/climate", "WillowBend", "Mapresso");

        CoveringSourceResolver.RecordedCoverageIn(model.Build())
            .Should().ContainSingle().Which.Resolved.Should().BeFalse();
    }

    // The count is carried so a run adds to what earlier runs recorded rather than overwriting it. A
    // coverage nothing has tried yet reads nought, which is what the first attempt makes one.
    [Fact]
    public void ACoverageNothingHasTriedYetHasNoAttemptsAgainstIt()
    {
        var model = CoverageOf("WillowBend/climate", "WillowBend", "Mapresso");

        CoveringSourceResolver.RecordedCoverageIn(model.Build())
            .Should().ContainSingle().Which.Attempts.Should().Be(0);
    }

    // A source that resolves onto an archetype is called once per Thing the site has of it, so the
    // portal's coverage of one assessment is a different Thing from its coverage of another. Reading
    // them as one would re-call every assessment because one of them failed.
    [Fact]
    public void OneSourceAnsweringForSeveralAssessmentsIsRecordedAgainstEachOfThem()
    {
        var model = CoverageOf("WillowBend/flood", "WillowBendFlood", "ThinkHazard")
            .Relate("WillowBend/landslide", CoveringSourceResolver.AppliesToPredicate, "WillowBendLandslide")
            .Relate("WillowBend/landslide", CoveringSourceResolver.SourcedFromPredicate, "ThinkHazard")
            .Carrying("WillowBend/flood", ResolvedAt, "2026-08-27T09:00:00Z");

        var recorded = CoveringSourceResolver.RecordedCoverageIn(model.Build());

        recorded.Should().HaveCount(2);
        recorded.Should().OnlyContain(coverage => coverage.SourceId == model.Id("ThinkHazard"));
        recorded.Single(coverage => coverage.SubjectId == model.Id("WillowBendFlood")).Resolved.Should().BeTrue();
        recorded.Single(coverage => coverage.SubjectId == model.Id("WillowBendLandslide")).Resolved.Should().BeFalse();
    }

    // A coverage reaching no source cannot be matched to any call a run makes, so it is left out rather
    // than counted — the same rule a source with no registration is left out under.
    [Fact]
    public void ACoverageReachingNoSourceIsLeftOut()
    {
        var model = new ModelBuilder()
            .Relate("WillowBend/climate", CoveringSourceResolver.AppliesToPredicate, "WillowBend");

        CoveringSourceResolver.RecordedCoverageIn(model.Build()).Should().BeEmpty();
    }

    private static ModelBuilder WholeVocabulary()
    {
        var model = new ModelBuilder();
        model.Id(CoveringSourceResolver.IsPredicateName);
        model.Id(CoveringSourceResolver.AppliesToPredicate);
        model.Id(CoveringSourceResolver.SourcedFromPredicate);
        return model.Archetype("SourceCoverage", CoveringSourceResolver.SourceCoverageArchetypeFlag);
    }

    // The archetype a run mints against is found by the mark the platform declares it with, never by its
    // name: a name would be renamed there and mint Things related to nothing here, and the only sign of
    // it would be every subject's coverage reading as empty.
    [Fact]
    public void TheArchetypeAMintGoesAgainstIsFoundByItsMark()
    {
        var model = WholeVocabulary();

        CoveringSourceResolver.CoverageVocabularyIn(model.Build())!.Archetype
            .Should().Be(model.Id("SourceCoverage"));
    }

    [Fact]
    public void TheVocabularyCarriesEveryEdgeAMintWrites()
    {
        var model = WholeVocabulary();

        var vocabulary = CoveringSourceResolver.CoverageVocabularyIn(model.Build())!;

        vocabulary.Is.Should().Be(model.Id(CoveringSourceResolver.IsPredicateName));
        vocabulary.AppliesTo.Should().Be(model.Id(CoveringSourceResolver.AppliesToPredicate));
        vocabulary.SourcedFrom.Should().Be(model.Id(CoveringSourceResolver.SourcedFromPredicate));
    }

    [Fact]
    public void AModelDeclaringNoCoverageArchetypeAnswersNothingRatherThanGuessing()
    {
        var model = new ModelBuilder().Archetype("SourceCoverage");

        CoveringSourceResolver.CoverageVocabularyIn(model.Build()).Should().BeNull();
    }

    // Refused whole rather than in part: a Thing minted and then left unrelated reaches neither its
    // subject nor its source, and no later run can tell it from one that was never minted at all.
    [Theory]
    [InlineData(CoveringSourceResolver.IsPredicateName)]
    [InlineData(CoveringSourceResolver.AppliesToPredicate)]
    [InlineData(CoveringSourceResolver.SourcedFromPredicate)]
    public void AModelMissingAnyEdgeAMintWritesIsRefused(string missing)
    {
        var model = new ModelBuilder();
        foreach (var predicate in new[]
                 {
                     CoveringSourceResolver.IsPredicateName,
                     CoveringSourceResolver.AppliesToPredicate,
                     CoveringSourceResolver.SourcedFromPredicate,
                 })
            if (predicate != missing) model.Id(predicate);
        model.Archetype("SourceCoverage", CoveringSourceResolver.SourceCoverageArchetypeFlag);

        CoveringSourceResolver.CoverageVocabularyIn(model.Build()).Should().BeNull();
    }

    // The coverages hanging off an assessment are the ones a run would otherwise never see, so the rule
    // reaching them has to run after the one that brings the site's own Things into the set.
    [Fact]
    public void TheReadReachesACoverageOnlyAfterTheSubjectsItCouldHangFrom()
    {
        var traverse = CoveringSourceResolver.SelectorFor(Guid.NewGuid()).Traverse;
        var names = traverse.Select(rule => rule.Predicate).ToList();

        names.IndexOf(CoveringSourceResolver.AppliesToPredicate)
            .Should().BeGreaterThan(names.IndexOf(CoveringSourceResolver.HasPredicate));
        names.IndexOf(CoveringSourceResolver.SourcedFromPredicate)
            .Should().BeGreaterThan(names.IndexOf(CoveringSourceResolver.AppliesToPredicate));
    }
}
