using vos.ManagedMicroservice.EndpointCaller.Services;
using FluentAssertions;
using Jsonata.Net.Native;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace vos.ManagedMicroservice.EndpointCaller.Tests;

public class ObservationIngestServiceTests
{
    // ---------- TryTransform ----------

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

    // Note: the `string.IsNullOrWhiteSpace(rawResult) → transformed = "null"` branch in
    // TryTransform is unreachable through Jsonata.Net.Native's public API — Eval returns
    // the literal string "undefined" for missing-path expressions rather than null/empty.
    // This branch is documented as a defensive guard for hypothetical future library
    // behavior; it falls under the [ExcludeFromCodeCoverage] discussion in TEST-STATE.md.

    [Fact]
    public void TryTransform_QueryEvalReturnsScalar_NormalizedToJson()
    {
        // Number-typed jsonata result still round-trips through TryNormalizeJson because
        // a bare number is a valid JSON document.
        var sut = CreateService();
        var query = new JsonataQuery("x");

        var ok = sut.TryTransform("{\"x\":42}", query, out var transformed, out _);

        ok.Should().BeTrue();
        transformed.Should().Be("42");
    }

    [Fact]
    public void TryTransform_QueryReturnsRawStringConcat_TryNormalizeJsonFallsBackToSerialize()
    {
        // String concatenation in jsonata yields raw text (e.g. "abchello"), not a JSON-quoted
        // string. TryNormalizeJson's JsonDocument.Parse rejects bare text, falling through to
        // JsonSerializer.Serialize which wraps the raw string in quotes. Pins:
        //   ObservationIngestService.cs:55-56 (Serialize fallback)
        //   ObservationIngestService.cs:118-121 (TryNormalizeJson JsonException catch)
        var sut = CreateService();
        var query = new JsonataQuery("name & \"hello\"");

        var ok = sut.TryTransform("{\"name\":\"abc\"}", query, out var transformed, out _);

        ok.Should().BeTrue();
        transformed.Should().Be("\"abchello\"");
    }

    [Fact]
    public void TryTransform_QueryEvalThrows_ReturnsErrorFromOuterCatch()
    {
        // Calling an undefined jsonata function throws JsonataException during Eval —
        // pins the outer try/catch (ObservationIngestService.cs:58-61).
        var sut = CreateService();
        var query = new JsonataQuery("$noSuchFunction()");

        var ok = sut.TryTransform("{\"x\":1}", query, out _, out var error);

        ok.Should().BeFalse();
        error.Should().NotBeEmpty();
    }

    // ---------- CreateObservationsAsync — happy path ----------

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

    [Fact]
    public async Task CreateObservationsAsync_WithArrayTransform_CreatesAllObservations()
    {
        var endpointThingId = Guid.NewGuid();
        var observedId = Guid.NewGuid();
        var brokerClient = Substitute.For<IEndpointBrokerClient>();
        brokerClient.FindThingByNameAsync("observed")
            .Returns(new BrokerClient.BrokerThing(observedId, "observed"));
        brokerClient.CreateThingAsync(Arg.Any<string>(), Arg.Any<Dictionary<string, object?>>())
            .Returns(_ => new BrokerClient.BrokerThing(Guid.NewGuid(), "Obs"));
        brokerClient.CreateRelationshipAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>())
            .Returns(true);
        var sut = new ObservationIngestService(brokerClient, Substitute.For<ILogger<ObservationIngestService>>());

        // jsonata expression maps an upstream array into an array of observation objects.
        var query = new JsonataQuery(
            "items.{\"name\":name,\"properties\":{\"value\":value}}");
        var upstream = """
        {
          "items":[
            {"name":"A","value":1},
            {"name":"B","value":2},
            {"name":"C","value":3}
          ]
        }
        """;

        var result = await sut.CreateObservationsAsync(endpointThingId, query, upstream);

        result.Success.Should().BeTrue($"{result.Error} {result.Detail}");
        result.ObservedCount.Should().Be(3);
    }

    // ---------- CreateObservationsAsync — failure paths ----------

    [Fact]
    public async Task CreateObservationsAsync_TransformFails_ReturnsFailureWithDetail()
    {
        var brokerClient = Substitute.For<IEndpointBrokerClient>();
        var sut = new ObservationIngestService(brokerClient, Substitute.For<ILogger<ObservationIngestService>>());
        var query = new JsonataQuery("$");

        var result = await sut.CreateObservationsAsync(Guid.NewGuid(), query, "not-json");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("transform failed");
        result.Detail.Should().Contain("not valid JSON");
        await brokerClient.DidNotReceive().FindThingByNameAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task CreateObservationsAsync_TransformedOutputNotObjectOrArray_ReturnsFailure()
    {
        var brokerClient = Substitute.For<IEndpointBrokerClient>();
        var sut = new ObservationIngestService(brokerClient, Substitute.For<ILogger<ObservationIngestService>>());
        // $ returns the input — but our input is a JSON number, not an object/array.
        var query = new JsonataQuery("$");

        var result = await sut.CreateObservationsAsync(Guid.NewGuid(), query, "42");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not a valid observation thing array");
    }

    [Fact]
    public async Task CreateObservationsAsync_ObservationMissingName_ReturnsFailure()
    {
        var brokerClient = Substitute.For<IEndpointBrokerClient>();
        var sut = new ObservationIngestService(brokerClient, Substitute.For<ILogger<ObservationIngestService>>());
        var query = new JsonataQuery("{\"properties\":{\"v\":1}}");

        var result = await sut.CreateObservationsAsync(Guid.NewGuid(), query, "{\"x\":1}");

        result.Success.Should().BeFalse();
        result.Detail.Should().Contain("missing a string 'name'");
    }

    [Fact]
    public async Task CreateObservationsAsync_ObservationMissingProperties_ReturnsFailure()
    {
        var brokerClient = Substitute.For<IEndpointBrokerClient>();
        var sut = new ObservationIngestService(brokerClient, Substitute.For<ILogger<ObservationIngestService>>());
        var query = new JsonataQuery("{\"name\":\"X\"}");

        var result = await sut.CreateObservationsAsync(Guid.NewGuid(), query, "{\"x\":1}");

        result.Success.Should().BeFalse();
        result.Detail.Should().Contain("missing an object 'properties'");
    }

    [Fact]
    public async Task CreateObservationsAsync_ObservationNotAnObject_ReturnsFailure()
    {
        var brokerClient = Substitute.For<IEndpointBrokerClient>();
        var sut = new ObservationIngestService(brokerClient, Substitute.For<ILogger<ObservationIngestService>>());
        // Array containing a primitive — TryParseObservation rejects non-object element.
        var query = new JsonataQuery("[\"not-an-object\"]");

        var result = await sut.CreateObservationsAsync(Guid.NewGuid(), query, "{\"x\":1}");

        result.Success.Should().BeFalse();
        result.Detail.Should().Contain("must be a JSON object");
    }

    [Fact]
    public async Task CreateObservationsAsync_PredicateNotFoundButCreatesNewPredicate_Succeeds()
    {
        // FindThingByNameAsync("observed") returns null → fallback to CreateThingAsync("observed").
        var endpointThingId = Guid.NewGuid();
        var observedId = Guid.NewGuid();
        var brokerClient = Substitute.For<IEndpointBrokerClient>();
        brokerClient.FindThingByNameAsync("observed").Returns((BrokerClient.BrokerThing?)null);
        brokerClient.CreateThingAsync("observed", Arg.Any<Dictionary<string, object?>>())
            .Returns(new BrokerClient.BrokerThing(observedId, "observed"));
        brokerClient.CreateThingAsync(Arg.Is<string>(s => s != "observed"), Arg.Any<Dictionary<string, object?>>())
            .Returns(new BrokerClient.BrokerThing(Guid.NewGuid(), "Obs"));
        brokerClient.CreateRelationshipAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>()).Returns(true);
        var sut = new ObservationIngestService(brokerClient, Substitute.For<ILogger<ObservationIngestService>>());
        var query = new JsonataQuery("{\"name\":\"Obs\",\"properties\":{\"v\":1}}");

        var result = await sut.CreateObservationsAsync(endpointThingId, query, "{\"x\":1}");

        result.Success.Should().BeTrue();
        await brokerClient.Received(1).CreateThingAsync("observed", Arg.Any<Dictionary<string, object?>>());
    }

    [Fact]
    public async Task CreateObservationsAsync_PredicateResolutionFails_ReturnsFailure()
    {
        // Both FindThingByName and CreateThing for "observed" return null → fail with detail message.
        var brokerClient = Substitute.For<IEndpointBrokerClient>();
        brokerClient.FindThingByNameAsync("observed").Returns((BrokerClient.BrokerThing?)null);
        brokerClient.CreateThingAsync("observed", Arg.Any<Dictionary<string, object?>>())
            .Returns((BrokerClient.BrokerThing?)null);
        var sut = new ObservationIngestService(brokerClient, Substitute.For<ILogger<ObservationIngestService>>());
        var query = new JsonataQuery("{\"name\":\"Obs\",\"properties\":{\"v\":1}}");

        var result = await sut.CreateObservationsAsync(Guid.NewGuid(), query, "{\"x\":1}");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("resolve or create 'observed' predicate");
    }

    [Fact]
    public async Task CreateObservationsAsync_CreateObservationFails_ReturnsFailureWithIndex()
    {
        var endpointThingId = Guid.NewGuid();
        var observedId = Guid.NewGuid();
        var firstObsId = Guid.NewGuid();
        var brokerClient = Substitute.For<IEndpointBrokerClient>();
        brokerClient.FindThingByNameAsync("observed")
            .Returns(new BrokerClient.BrokerThing(observedId, "observed"));
        // First creation succeeds, second returns null → fail at index 1.
        brokerClient.CreateThingAsync("A", Arg.Any<Dictionary<string, object?>>())
            .Returns(new BrokerClient.BrokerThing(firstObsId, "A"));
        brokerClient.CreateThingAsync("B", Arg.Any<Dictionary<string, object?>>())
            .Returns((BrokerClient.BrokerThing?)null);
        brokerClient.CreateRelationshipAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>()).Returns(true);
        var sut = new ObservationIngestService(brokerClient, Substitute.For<ILogger<ObservationIngestService>>());

        var query = new JsonataQuery("items.{\"name\":name,\"properties\":{\"v\":value}}");
        var upstream = """{"items":[{"name":"A","value":1},{"name":"B","value":2}]}""";

        var result = await sut.CreateObservationsAsync(endpointThingId, query, upstream);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Failed to create observation thing");
        result.Index.Should().Be(1);
        result.ObservedCount.Should().Be(1);
    }

    [Fact]
    public async Task CreateObservationsAsync_RelateFails_ReturnsFailureWithObservationId()
    {
        var endpointThingId = Guid.NewGuid();
        var observedId = Guid.NewGuid();
        var obsId = Guid.NewGuid();
        var brokerClient = Substitute.For<IEndpointBrokerClient>();
        brokerClient.FindThingByNameAsync("observed")
            .Returns(new BrokerClient.BrokerThing(observedId, "observed"));
        brokerClient.CreateThingAsync(Arg.Any<string>(), Arg.Any<Dictionary<string, object?>>())
            .Returns(new BrokerClient.BrokerThing(obsId, "Obs"));
        brokerClient.CreateRelationshipAsync(endpointThingId, observedId, obsId).Returns(false);
        var sut = new ObservationIngestService(brokerClient, Substitute.For<ILogger<ObservationIngestService>>());

        var query = new JsonataQuery("{\"name\":\"Obs\",\"properties\":{\"v\":1}}");

        var result = await sut.CreateObservationsAsync(endpointThingId, query, "{\"x\":1}");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("relate observation thing");
        result.ObservationThingId.Should().Be(obsId);
    }

    private static ObservationIngestService CreateService()
    {
        var brokerClient = Substitute.For<IEndpointBrokerClient>();
        var logger = Substitute.For<ILogger<ObservationIngestService>>();
        return new ObservationIngestService(brokerClient, logger);
    }
}
