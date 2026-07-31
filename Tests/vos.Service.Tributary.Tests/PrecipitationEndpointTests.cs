using vos.Service.Tributary.Services;
using FluentAssertions;
using JsonataTransform = vos.Service.Shared.JsonataTransform;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace vos.Service.Tributary.Tests;

// Plane A of the Water slice (#5805): a precipitation endpoint reshapes an Open-Meteo response into a
// reading Tributary ingests as an observation on the Site — precipitation (mm) at an observed time. The
// generic fetch + ingest is Tributary's; this pins the source-specific reshape that lands real rainfall
// onto the Site, feeding the reserve/storage the WaterReserve node consumes.
public class PrecipitationEndpointTests
{
    // Open-Meteo hourly precipitation -> a Site reading { name, properties:{precipitation}, observedAt }.
    private const string PrecipitationReshape =
        "{\"name\": \"ExampleSite\", \"properties\": {\"precipitation\": hourly.precipitation[0]}, \"observedAt\": hourly.time[0]}";

    [Fact]
    public void Reshape_maps_open_meteo_precipitation_to_a_site_reading()
    {
        var sut = new ObservationIngestService(
            Substitute.For<IEndpointMyceliumClient>(),
            Substitute.For<ILogger<ObservationIngestService>>());
        var query = new JsonataTransform(PrecipitationReshape);
        var upstream = """
        {
          "latitude": -25.75,
          "longitude": 28.19,
          "hourly": {
            "time": ["2026-07-09T00:00", "2026-07-09T01:00"],
            "precipitation": [3.4, 0.0]
          }
        }
        """;

        var ok = sut.TryTransform(upstream, query, out var transformed, out var error);

        ok.Should().BeTrue(error);
        transformed.Should().Contain("\"name\":\"ExampleSite\"");
        transformed.Should().Contain("\"precipitation\":3.4");
        transformed.Should().Contain("\"observedAt\":\"2026-07-09T00:00\"");
    }
}
