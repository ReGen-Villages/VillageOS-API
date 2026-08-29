using System.Text.Json;
using vos.Service.Tributary.Services;
using FluentAssertions;
using JsonataTransform = vos.Service.Shared.JsonataTransform;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace vos.Service.Tributary.Tests;

// The climate averages a discovery run resolves a site against. Both figures take observations only, so
// nobody can type either in and only a fetch can write them — and until this registration existed nothing
// wrote either, so the rainwater harvest and everything judged over it read unassessed on every site.
//
// The expression is authored in the platform repository, as the `climate-averages` registration in
// open-data-sources.template.json (Bug #6825). The two agree by this literal and by nothing else, which is
// why what it has to get right is spelled out here rather than left to the seed.
public class ClimateAveragesEndpointTests
{
    // The provider states both figures as a daily average over twenty years, so each is multiplied by the
    // days in an average year — the twenty it averages hold five leap days, which is the quarter day. The
    // reading names no Thing: the discovery run says which site a call is about.
    private const string ClimateAveragesReshape = """
        ($rainfallMillimetresPerDay := properties.parameter.PRECTOTCORR.ANN; $sunlightKwhPerM2PerDay := properties.parameter.ALLSKY_SFC_SW_DWN.ANN; {"properties": $merge([$rainfallMillimetresPerDay > 0 ? {"rainfallMillimetresPerYear": $rainfallMillimetresPerDay * 365.25} : {}, $sunlightKwhPerM2PerDay > 0 ? {"solarResourceKwhPerM2PerYear": $sunlightKwhPerM2PerDay * 365.25} : {}])})
        """;

    // One point query at a site's coordinates, verbatim from 39.5012, -8.4137 and trimmed to what the
    // assertions read. The provider's own body carries a `properties` object, which the reading built from
    // it also names — an expression reading the wrong one answers with the provider's shape unchanged.
    private const string ProviderAnswer = """
    {
      "type": "Feature",
      "geometry": { "type": "Point", "coordinates": [-8.414, 39.501, 231.4] },
      "properties": {
        "parameter": {
          "ALLSKY_SFC_SW_DWN": { "JAN": 2.1221, "JUL": 7.4393, "DEC": 1.9289, "ANN": 4.6428 },
          "PRECTOTCORR": { "JAN": 2.66, "JUL": 0.23, "DEC": 2.5, "ANN": 1.88 }
        }
      },
      "header": {
        "fill_value": -999.0,
        "range": "20-year Meteorological and Solar Monthly & Annual Climatologies (January 2001 - December 2020)"
      },
      "parameters": {
        "ALLSKY_SFC_SW_DWN": { "units": "kW-hr/m^2/day", "longname": "All Sky Surface Shortwave Downward Irradiance" },
        "PRECTOTCORR": { "units": "mm/day", "longname": "Precipitation Corrected" }
      }
    }
    """;

    // What the provider answers where it holds nothing for a figure at a coordinate: the negative fill
    // value its own header declares, in place of a measurement.
    private const string ProviderHoldsNoData = """
    {
      "properties": {
        "parameter": {
          "ALLSKY_SFC_SW_DWN": { "ANN": -999.0 },
          "PRECTOTCORR": { "ANN": -999.0 }
        }
      },
      "header": { "fill_value": -999.0 }
    }
    """;

    private static ObservationIngestService Ingest() => new(
        Substitute.For<IEndpointMyceliumClient>(),
        Substitute.For<ILogger<ObservationIngestService>>());

    private static JsonElement Reading(string providerAnswer)
    {
        var ok = Ingest().TryTransform(
            providerAnswer, new JsonataTransform(ClimateAveragesReshape), out var transformed, out var error);

        ok.Should().BeTrue(error);
        return JsonDocument.Parse(transformed!).RootElement.GetProperty("properties");
    }

    // 1.88 mm a day over an average year is 686.67 mm; 4.6428 kWh a square metre a day is 1,695.78 kWh over
    // the same year. Read to two places, which is far tighter than a wrong conversion factor could survive.
    [Theory]
    [InlineData("rainfallMillimetresPerYear", 686.67)]
    [InlineData("solarResourceKwhPerM2PerYear", 1695.78)]
    public void Reshape_turns_a_daily_average_into_the_figure_a_year_holds(string figure, double expected)
    {
        Reading(ProviderAnswer).GetProperty(figure).GetDouble().Should().BeApproximately(expected, 0.01);
    }

    // The provider's own body has a `properties` object of its own, holding the parameter block the
    // expression reads. A reading that carried it through would write the provider's shape onto the site.
    [Fact]
    public void Reshape_writes_the_two_figures_and_nothing_else_the_provider_carried()
    {
        Reading(ProviderAnswer).EnumerateObject().Select(figure => figure.Name)
            .Should().BeEquivalentTo("rainfallMillimetresPerYear", "solarResourceKwhPerM2PerYear");
    }

    // The fill value is a number, and a number written onto the site is a measurement as far as every
    // guard over it is concerned: a harvest would be worked out from minus 365,000 mm of rain and judged.
    // Nothing is written instead, which is what leaves both figures honestly unknown.
    [Fact]
    public void Reshape_writes_nothing_where_the_provider_answers_with_its_fill_value()
    {
        Reading(ProviderHoldsNoData).EnumerateObject().Should().BeEmpty();
    }

    // One figure held and the other not is the ordinary case at a coordinate near a coastline, and the two
    // are independent: withholding the pair because one was missing would lose a figure the provider gave.
    [Fact]
    public void Reshape_writes_the_figure_the_provider_holds_when_the_other_is_missing()
    {
        const string rainOnly = """
        {
          "properties": { "parameter": {
            "ALLSKY_SFC_SW_DWN": { "ANN": -999.0 },
            "PRECTOTCORR": { "ANN": 1.88 } } },
          "header": { "fill_value": -999.0 }
        }
        """;

        var reading = Reading(rainOnly);

        reading.GetProperty("rainfallMillimetresPerYear").GetDouble().Should().BeApproximately(686.67, 0.01);
        reading.TryGetProperty("solarResourceKwhPerM2PerYear", out _).Should().BeFalse();
    }

    // A parameter the provider left out entirely is not the same answer as one it filled — a request the
    // provider partly refused comes back without the block at all — and it has to leave the figure unknown
    // just the same rather than reaching the arithmetic.
    [Fact]
    public void Reshape_writes_nothing_where_the_provider_returned_no_figures_at_all()
    {
        const string noParameters = """{ "properties": { "parameter": {} }, "header": { "fill_value": -999.0 } }""";

        Reading(noParameters).EnumerateObject().Should().BeEmpty();
    }
}
