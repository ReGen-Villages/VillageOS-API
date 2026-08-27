using vos.Service.Tributary.Services;
using FluentAssertions;
using JsonataTransform = vos.Service.Shared.JsonataTransform;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace vos.Service.Tributary.Tests;

// Task #6735 — the hazard portal a discovery run resolves a site's assessments against. An
// assessment's level takes observations only, so nobody can type it in and only a fetch can write
// it; the run calls the portal once per assessment, with that assessment as the call's subject.
//
// The expression is authored in the platform repository, as the `hazard-grading` registration in
// open-data-sources.template.json (platform Task #6804). The two agree by this literal and by
// nothing else, which is why what it has to get right is spelled out here rather than left to the
// seed.
public class HazardGradingEndpointTests
{
    // The portal's grade titles are spaced and cased for a reader; the model's hazard levels are the
    // kebab-case Things the grade will one day relate to. A hazard the portal holds no data for is
    // answered with 404 and never reaches this expression — the guard below is for a body that
    // carries the grade anyway, because "no data" must leave an assessment unassessed, not graded.
    private const string HazardGradingReshape =
        "($level := hazard_category[hazard_level and $lowercase(hazard_level) != 'no data'].hazard_level;"
        + " $level ? {\"properties\": {\"hazardLevel\": $replace($lowercase($level), \" \", \"-\"),"
        + " \"assessedOn\": $now()}} : {\"properties\": {}})";

    // One per-hazard report, trimmed to the shape that matters: the grade sits beside recommendation
    // prose many times its size, and beside the portal's own source list.
    private const string ProviderAnswer = """
    {
      "hazard_category": {
        "hazard_type": "River flood",
        "hazard_level": "High",
        "general_recommendation": "In the area you have selected river flood hazard is classified as **high**...",
        "technical_recommendations": [
          { "text": "LOCATION ASSESSMENT: consider a study of the surrounding landscape.", "detail": "..." }
        ]
      },
      "sources": [ { "id": "FL-GLOBAL-FATHOM", "owner_organization": "GFDRR" } ]
    }
    """;

    private static ObservationIngestService Ingest() => new(
        Substitute.For<IEndpointMyceliumClient>(),
        Substitute.For<ILogger<ObservationIngestService>>());

    [Fact]
    public void Reshape_maps_the_portals_grade_to_an_assessment_reading()
    {
        var ok = Ingest().TryTransform(
            ProviderAnswer, new JsonataTransform(HazardGradingReshape), out var transformed, out var error);

        ok.Should().BeTrue(error);
        transformed.Should().Contain("\"hazardLevel\":\"high\"");
        transformed.Should().Contain("assessedOn");
    }

    [Fact]
    public void Reshape_writes_the_vocabulary_term_and_not_the_portals_title()
    {
        // "Very low" is the portal's rendering; very-low is the Thing the level names. A title written
        // as it came would resolve against no vocabulary Thing when the word becomes an edge.
        const string veryLow = """
        { "hazard_category": { "hazard_type": "Earthquake", "hazard_level": "Very low" } }
        """;

        var ok = Ingest().TryTransform(
            veryLow, new JsonataTransform(HazardGradingReshape), out var transformed, out var error);

        ok.Should().BeTrue(error);
        transformed.Should().Contain("\"hazardLevel\":\"very-low\"");
        transformed.Should().NotContain("Very low");
    }

    [Fact]
    public void Reshape_writes_nothing_for_an_answer_naming_no_grade()
    {
        // No grade means no reading — and no date either, because a dated absence would read as an
        // assessment that happened.
        const string ungraded = """
        { "hazard_category": { "hazard_type": "Cyclone" } }
        """;

        var ok = Ingest().TryTransform(
            ungraded, new JsonataTransform(HazardGradingReshape), out var transformed, out var error);

        ok.Should().BeTrue(error);
        transformed.Should().NotContain("hazardLevel");
        transformed.Should().NotContain("assessedOn");
    }

    [Fact]
    public void Reshape_writes_nothing_for_a_no_data_grade()
    {
        // "No data" is the portal saying it holds nothing for this place, which is not the same as no
        // hazard. Writing it would grade the hazard; the assessment has to stay unassessed instead.
        const string noData = """
        { "hazard_category": { "hazard_type": "Tsunami", "hazard_level": "No Data" } }
        """;

        var ok = Ingest().TryTransform(
            noData, new JsonataTransform(HazardGradingReshape), out var transformed, out var error);

        ok.Should().BeTrue(error);
        transformed.Should().NotContain("hazardLevel");
        transformed.Should().NotContain("assessedOn");
    }

    [Fact]
    public void Reshape_carries_none_of_the_recommendation_prose()
    {
        // The report is mostly guidance text for a human reader. A reading carries the grade alone;
        // prose written onto an assessment would be a document masquerading as a value.
        var ok = Ingest().TryTransform(
            ProviderAnswer, new JsonataTransform(HazardGradingReshape), out var transformed, out var error);

        ok.Should().BeTrue(error);
        transformed.Should().NotContain("recommendation");
        transformed.Should().NotContain("LOCATION ASSESSMENT");
        transformed.Should().NotContain("FL-GLOBAL-FATHOM");
    }
}
