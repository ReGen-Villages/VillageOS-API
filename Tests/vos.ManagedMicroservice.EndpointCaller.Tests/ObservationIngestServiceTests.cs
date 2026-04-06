using vos.ManagedMicroservice.EndpointCaller.Services;
using FluentAssertions;
using Jsonata.Net.Native;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace vos.ManagedMicroservice.EndpointCaller.Tests;

public class ObservationIngestServiceTests
{
    [Fact]
    public void TryTransform_WithValidJsonAndExpression_ReturnsNormalizedJson()
    {
        var sut = CreateService();
        var query = new JsonataQuery("{\"value\":hourly.temperature_2m[0]}");
        var upstream = """
        {
          "hourly": {
            "temperature_2m": [5.8, 6.1]
          }
        }
        """;

        var ok = sut.TryTransform(upstream, query, out var transformed, out var error);

        ok.Should().BeTrue();
        error.Should().BeEmpty();
        transformed.Should().Be("{\"value\":5.8}");
    }

    [Fact]
    public void TryTransform_WithNonJsonBody_ReturnsError()
    {
        var sut = CreateService();
        var query = new JsonataQuery("{\"x\":1}");

        var ok = sut.TryTransform("not-json", query, out _, out var error);

        ok.Should().BeFalse();
        error.Should().Contain("not valid JSON");
    }

    [Fact]
    public async Task CreateObservationsAsync_WithValidTransform_CreatesAndRelatesObservations()
    {
        var endpointThingId = Guid.NewGuid();
        var observedPredicateId = Guid.NewGuid();
        var createdObservationId = Guid.NewGuid();
        var brokerClient = Substitute.For<IEndpointBrokerClient>();
        brokerClient.FindThingByNameAsync("observed")
            .Returns(new BrokerClient.BrokerThing(observedPredicateId, "observed"));
        brokerClient.CreateThingAsync(
                "Observation - temperature_2m",
                Arg.Is<Dictionary<string, object?>>(d => d.ContainsKey("time") && d.ContainsKey("value")))
            .Returns(new BrokerClient.BrokerThing(createdObservationId, "Observation - temperature_2m"));
        brokerClient.CreateRelationshipAsync(endpointThingId, observedPredicateId, createdObservationId)
            .Returns(true);

        var logger = Substitute.For<ILogger<ObservationIngestService>>();
        var sut = new ObservationIngestService(brokerClient, logger);

        var query = new JsonataQuery(
            "{\"name\":\"Observation - temperature_2m\",\"properties\":{\"time\":hourly.time[0],\"value\":hourly.temperature_2m[0]}}");
        var upstream = """
        {
          "hourly": {
            "time": ["2026-03-03T00:00"],
            "temperature_2m": [5.8]
          }
        }
        """;

        var result = await sut.CreateObservationsAsync(endpointThingId, query, upstream);

        result.Success.Should().BeTrue($"{result.Error} {result.Detail}");
        result.ObservedCount.Should().Be(1);
        await brokerClient.Received(1).CreateThingAsync(
            "Observation - temperature_2m",
            Arg.Any<Dictionary<string, object?>>());
        await brokerClient.Received(1).CreateRelationshipAsync(endpointThingId, observedPredicateId, createdObservationId);
    }

    private static ObservationIngestService CreateService()
    {
        var brokerClient = Substitute.For<IEndpointBrokerClient>();
        var logger = Substitute.For<ILogger<ObservationIngestService>>();
        return new ObservationIngestService(brokerClient, logger);
    }
}
