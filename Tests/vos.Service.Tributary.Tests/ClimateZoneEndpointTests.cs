using vos.Service.Tributary.Services;
using FluentAssertions;
using JsonataTransform = vos.Service.Shared.JsonataTransform;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace vos.Service.Tributary.Tests;

// Task #6734 — the climate source a discovery run resolves a site against. A site's climate zone takes
// observations only, so nobody can type it in and only a fetch can write it; until this registration
// existed the property had never been written at all.
//
// The expression is authored in the platform repository, as the `climate-classification` registration in
// open-data-sources.template.json (User Story #6750). The two agree by this literal and by nothing else,
// which is why what it has to get right is spelled out here rather than left to the seed.
public class ClimateZoneEndpointTests
{
    // The provider's answer carries a code from every classification scheme it holds, so the entry is
    // selected by the marker the provider gives Köppen-Geiger. The reading names no Thing: the discovery
    // run says which site a call is about, and a name here would fit only one site.
    private const string ClimateZoneReshape =
        "{\"properties\": {\"climateZone\": data[short='KG'].code}}";

    // One point query at a site's coordinates, trimmed to the schemes that matter to the assertions. The
    // Köppen-Geiger entry is deliberately not first: reading the first entry is the mistake this guards.
    private const string ProviderAnswer = """
    {
      "results": { "lat": 39.4, "lon": -8.2, "version": "0.90-pyzonae" },
      "status": "OK",
      "data": [
        { "type": "Cannon", "code": "3cw", "short": "C" },
        { "type": "Trewartha", "code": "BS", "short": "T", "text": "Steppe or Semiarid" },
        { "type": "Köppen-Geiger", "code": "Csa", "short": "KG", "text": "Hot-summer Mediterranean climate" },
        { "type": "Whittaker (1970)", "code": "Woodland/shrubland", "short": "W" }
      ]
    }
    """;

    private static ObservationIngestService Ingest() => new(
        Substitute.For<IEndpointMyceliumClient>(),
        Substitute.For<ILogger<ObservationIngestService>>());

    [Fact]
    public void Reshape_maps_the_climate_classification_to_a_site_reading()
    {
        var ok = Ingest().TryTransform(
            ProviderAnswer, new JsonataTransform(ClimateZoneReshape), out var transformed, out var error);

        ok.Should().BeTrue(error);
        transformed.Should().Contain("\"climateZone\":\"Csa\"");
    }

    [Fact]
    public void Reshape_takes_the_scheme_the_codes_belong_to_and_not_whichever_answer_leads()
    {
        var ok = Ingest().TryTransform(
            ProviderAnswer, new JsonataTransform(ClimateZoneReshape), out var transformed, out var error);

        ok.Should().BeTrue(error);
        // Every one of these is a real code for this coordinate under a different scheme. A zone read
        // against the wrong scheme is a plausible value nothing can tell apart from the right one.
        transformed.Should().NotContain("3cw");
        transformed.Should().NotContain("Woodland/shrubland");
        transformed.Should().NotContain("\"climateZone\":\"BS\"");
    }

    [Fact]
    public void Reshape_writes_nothing_where_the_provider_holds_no_climate_classification()
    {
        const string noClimateScheme = """
        { "status": "OK", "data": [ { "type": "Cannon", "code": "3cw", "short": "C" } ] }
        """;

        var ok = Ingest().TryTransform(
            noClimateScheme, new JsonataTransform(ClimateZoneReshape), out var transformed, out var error);

        // A site the provider cannot classify keeps no zone, rather than taking the nearest scheme's code
        // as though it were one. Nothing is written, which is what leaves the property honestly unknown.
        ok.Should().BeTrue(error);
        transformed.Should().NotContain("climateZone");
    }
}
