using vos.ManagedMicroservice.Tributary.Services;
using FluentAssertions;
using JsonataTransform = vos.ManagedMicroservice.Shared.JsonataTransform;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace vos.ManagedMicroservice.Tributary.Tests;

// Plane A of the Energy slice (#5806): a solar-resource endpoint reshapes an Open-Meteo response into a
// reading Tributary ingests as a shortwave-radiation observation on the Site. That resource (annualized to
// GTI) and the PV area — rolled up reactively over the classified SolarArray is-edges (#5796/#5797,
// AggregateBounds Sum) — are the two inputs the EnergyBalance node multiplies into solar generation.
public class SolarResourceEndpointTests
{
    // Open-Meteo hourly shortwave radiation -> a Site reading { name, properties:{shortwaveRadiation}, observedAt }.
    private const string SolarResourceReshape =
        "{\"name\": \"JosudanSite\", \"properties\": {\"shortwaveRadiation\": hourly.shortwave_radiation[0]}, \"observedAt\": hourly.time[0]}";

    [Fact]
    public void Reshape_maps_open_meteo_solar_radiation_to_a_site_reading()
    {
        var sut = new ObservationIngestService(
            Substitute.For<IEndpointMyceliumClient>(),
            Substitute.For<ILogger<ObservationIngestService>>());
        var query = new JsonataTransform(SolarResourceReshape);
        var upstream = """
        {
          "latitude": -25.75,
          "longitude": 28.19,
          "hourly": {
            "time": ["2026-07-09T11:00", "2026-07-09T12:00"],
            "shortwave_radiation": [612.0, 690.5]
          }
        }
        """;

        var ok = sut.TryTransform(upstream, query, out var transformed, out var error);

        ok.Should().BeTrue(error);
        transformed.Should().Contain("\"name\":\"JosudanSite\"");
        transformed.Should().Contain("\"shortwaveRadiation\":612");
        transformed.Should().Contain("\"observedAt\":\"2026-07-09T11:00\"");
    }
}
