using vos.Service.Shared;
using vos.Service.Tributary.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace vos.Service.Tributary.Tests;

public class ObservationIngestServiceTests
{
    // ---------- TryTransform ----------

    [Fact]
    public void TryTransform_WithValidJsonAndExpression_ReturnsNormalizedJson()
    {
        var sut = CreateService();
        var query = new JsonataTransform("{\"value\":hourly.temperature_2m[0]}");
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
        var query = new JsonataTransform("{\"x\":1}");

        var ok = sut.TryTransform("not-json", query, out _, out var error);

        ok.Should().BeFalse();
        error.Should().Contain("not valid JSON");
    }

    [Fact]
    public void TryTransform_QueryEvalReturnsScalar_NormalizedToJson()
    {
        var sut = CreateService();
        var query = new JsonataTransform("x");

        var ok = sut.TryTransform("{\"x\":42}", query, out var transformed, out _);

        ok.Should().BeTrue();
        transformed.Should().Be("42");
    }

    [Fact]
    public void TryTransform_QueryReturnsRawStringConcat_TryNormalizeJsonFallsBackToSerialize()
    {
        var sut = CreateService();
        var query = new JsonataTransform("name & \"hello\"");

        var ok = sut.TryTransform("{\"name\":\"abc\"}", query, out var transformed, out _);

        ok.Should().BeTrue();
        transformed.Should().Be("\"abchello\"");
    }

    [Fact]
    public void TryTransform_QueryEvalThrows_ReturnsErrorFromOuterCatch()
    {
        var sut = CreateService();
        var query = new JsonataTransform("$noSuchFunction()");

        var ok = sut.TryTransform("{\"x\":1}", query, out _, out var error);

        ok.Should().BeFalse();
        error.Should().NotBeEmpty();
    }

    // ---------- CreateObservationsAsync — hybrid ingest (readings -> observations) ----------

    [Fact]
    public async Task ExistingEntity_WritesAllReadingsAsObservations_NoThingCreated()
    {
        var endpointThingId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var client = Substitute.For<IEndpointMyceliumClient>();
        client.FindThingByNameAsync("Sensor-1").Returns(new MyceliumClient.MyceliumThing(entityId, "Sensor-1"));
        client.SubmitObservationsAsync(entityId, Arg.Any<IReadOnlyList<ObservationSample>>()).Returns(true);
        var sut = new ObservationIngestService(client, Substitute.For<ILogger<ObservationIngestService>>());

        var query = new JsonataTransform("readings.{\"name\":\"Sensor-1\",\"properties\":{\"temp\":temp},\"observedAt\":at}");
        var upstream = """{"readings":[{"temp":5.8,"at":"2026-03-03T00:00:00Z"},{"temp":6.1,"at":"2026-03-03T01:00:00Z"}]}""";

        var result = await sut.CreateObservationsAsync(endpointThingId, query, upstream);

        result.Success.Should().BeTrue($"{result.Error} {result.Detail}");
        result.EntitiesTouched.Should().Be(1);
        result.ObservationsSubmitted.Should().Be(2);
        await client.DidNotReceive().CreateThingAsync(Arg.Any<string>(), Arg.Any<Dictionary<string, object?>>());
        await client.Received(1).SubmitObservationsAsync(entityId,
            Arg.Is<IReadOnlyList<ObservationSample>>(s => s.Count == 2 && s.All(x => x.Property == "temp")));
    }

    [Fact]
    public async Task NewEntity_DeclaresThing_SetsMode_RelatesToSource_ObservesRemaining()
    {
        var endpointThingId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var observedId = Guid.NewGuid();
        var client = Substitute.For<IEndpointMyceliumClient>();
        client.FindThingByNameAsync("Sensor-1").Returns((MyceliumClient.MyceliumThing?)null);
        client.FindThingByNameAsync("observed").Returns(new MyceliumClient.MyceliumThing(observedId, "observed"));
        client.CreateThingAsync("Sensor-1", Arg.Any<Dictionary<string, object?>>())
            .Returns(new MyceliumClient.MyceliumThing(entityId, "Sensor-1"));
        client.SetPropertyModeAsync(entityId, Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        client.CreateRelationshipAsync(endpointThingId, observedId, entityId).Returns(true);
        client.SubmitObservationsAsync(entityId, Arg.Any<IReadOnlyList<ObservationSample>>()).Returns(true);
        var sut = new ObservationIngestService(client, Substitute.For<ILogger<ObservationIngestService>>());

        var query = new JsonataTransform("readings.{\"name\":\"Sensor-1\",\"properties\":{\"temp\":temp}}");
        var upstream = """{"readings":[{"temp":5.8},{"temp":6.1}]}""";

        var result = await sut.CreateObservationsAsync(endpointThingId, query, upstream);

        result.Success.Should().BeTrue($"{result.Error} {result.Detail}");
        result.EntitiesTouched.Should().Be(1);
        // First reading seeds the property declaration (a Fact); the second is observed.
        result.ObservationsSubmitted.Should().Be(1);
        await client.Received(1).CreateThingAsync("Sensor-1", Arg.Any<Dictionary<string, object?>>());
        await client.Received(1).SetPropertyModeAsync(entityId, "temp", ObservationIngestService.DefaultObservationMode);
        await client.Received(1).CreateRelationshipAsync(endpointThingId, observedId, entityId);
        await client.Received(1).SubmitObservationsAsync(entityId,
            Arg.Is<IReadOnlyList<ObservationSample>>(s => s.Count == 1));
    }

    [Fact]
    public async Task MultipleEntities_AreGroupedAndTouchedOnce()
    {
        var endpointThingId = Guid.NewGuid();
        var observedId = Guid.NewGuid();
        var client = Substitute.For<IEndpointMyceliumClient>();
        client.FindThingByNameAsync("observed").Returns(new MyceliumClient.MyceliumThing(observedId, "observed"));
        client.FindThingByNameAsync(Arg.Is<string>(s => s != "observed")).Returns((MyceliumClient.MyceliumThing?)null);
        client.CreateThingAsync(Arg.Is<string>(s => s != "observed"), Arg.Any<Dictionary<string, object?>>())
            .Returns(_ => new MyceliumClient.MyceliumThing(Guid.NewGuid(), "e"));
        client.SetPropertyModeAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        client.CreateRelationshipAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>()).Returns(true);
        client.SubmitObservationsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<ObservationSample>>()).Returns(true);
        var sut = new ObservationIngestService(client, Substitute.For<ILogger<ObservationIngestService>>());

        var query = new JsonataTransform("items.{\"name\":name,\"properties\":{\"v\":value}}");
        var upstream = """{"items":[{"name":"A","value":1},{"name":"B","value":2},{"name":"A","value":3}]}""";

        var result = await sut.CreateObservationsAsync(endpointThingId, query, upstream);

        result.Success.Should().BeTrue($"{result.Error} {result.Detail}");
        result.EntitiesTouched.Should().Be(2); // A and B, each created once
        await client.Received(1).CreateThingAsync("A", Arg.Any<Dictionary<string, object?>>());
        await client.Received(1).CreateThingAsync("B", Arg.Any<Dictionary<string, object?>>());
    }

    [Fact]
    public async Task ObservedAt_IsParsedFromReading()
    {
        var entityId = Guid.NewGuid();
        IReadOnlyList<ObservationSample>? captured = null;
        var client = Substitute.For<IEndpointMyceliumClient>();
        client.FindThingByNameAsync("S").Returns(new MyceliumClient.MyceliumThing(entityId, "S"));
        client.SubmitObservationsAsync(entityId, Arg.Do<IReadOnlyList<ObservationSample>>(s => captured = s)).Returns(true);
        var sut = new ObservationIngestService(client, Substitute.For<ILogger<ObservationIngestService>>());

        var query = new JsonataTransform("{\"name\":\"S\",\"properties\":{\"v\":1},\"observedAt\":\"2026-03-03T12:00:00Z\"}");
        var result = await sut.CreateObservationsAsync(Guid.NewGuid(), query, "{\"x\":1}");

        result.Success.Should().BeTrue($"{result.Error} {result.Detail}");
        captured.Should().NotBeNull();
        captured![0].ObservedAt.Should().Be(new DateTime(2026, 3, 3, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task ObservedAt_LeftUnsetWhenTheReadingNamesNoTime()
    {
        var entityId = Guid.NewGuid();
        IReadOnlyList<ObservationSample>? captured = null;
        var client = Substitute.For<IEndpointMyceliumClient>();
        client.FindThingByNameAsync("S").Returns(new MyceliumClient.MyceliumThing(entityId, "S"));
        client.SubmitObservationsAsync(entityId, Arg.Do<IReadOnlyList<ObservationSample>>(s => captured = s)).Returns(true);
        var sut = new ObservationIngestService(client, Substitute.For<ILogger<ObservationIngestService>>());

        var query = new JsonataTransform("{\"name\":\"S\",\"properties\":{\"v\":1}}");
        var result = await sut.CreateObservationsAsync(Guid.NewGuid(), query, "{\"x\":1}");

        result.Success.Should().BeTrue($"{result.Error} {result.Detail}");
        captured.Should().NotBeNull();
        captured![0].ObservedAt.Should().BeNull(
            "Mycelium stamps an undated sample from the model clock, which a run can anchor away "
            + "from real time — stamping here would use this process's wall clock instead");
    }

    // ---------- failure paths ----------

    [Fact]
    public async Task TransformFails_ReturnsFailure_AndTouchesNothing()
    {
        var client = Substitute.For<IEndpointMyceliumClient>();
        var sut = new ObservationIngestService(client, Substitute.For<ILogger<ObservationIngestService>>());

        var result = await sut.CreateObservationsAsync(Guid.NewGuid(), new JsonataTransform("$"), "not-json");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("transform failed");
        result.Detail.Should().Contain("not valid JSON");
        await client.DidNotReceive().FindThingByNameAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task TransformedOutputNotObjectOrArray_ReturnsFailure()
    {
        var client = Substitute.For<IEndpointMyceliumClient>();
        var sut = new ObservationIngestService(client, Substitute.For<ILogger<ObservationIngestService>>());

        var result = await sut.CreateObservationsAsync(Guid.NewGuid(), new JsonataTransform("$"), "42");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not a valid reading array");
    }

    [Fact]
    public async Task ReadingMissingName_ReturnsFailure()
    {
        var client = Substitute.For<IEndpointMyceliumClient>();
        var sut = new ObservationIngestService(client, Substitute.For<ILogger<ObservationIngestService>>());

        var result = await sut.CreateObservationsAsync(Guid.NewGuid(), new JsonataTransform("{\"properties\":{\"v\":1}}"), "{\"x\":1}");

        result.Success.Should().BeFalse();
        result.Detail.Should().Contain("missing a string 'name'");
    }

    [Fact]
    public async Task ReadingMissingProperties_ReturnsFailure()
    {
        var client = Substitute.For<IEndpointMyceliumClient>();
        var sut = new ObservationIngestService(client, Substitute.For<ILogger<ObservationIngestService>>());

        var result = await sut.CreateObservationsAsync(Guid.NewGuid(), new JsonataTransform("{\"name\":\"X\"}"), "{\"x\":1}");

        result.Success.Should().BeFalse();
        result.Detail.Should().Contain("missing an object 'properties'");
    }

    [Fact]
    public async Task ReadingNotAnObject_ReturnsFailure()
    {
        var client = Substitute.For<IEndpointMyceliumClient>();
        var sut = new ObservationIngestService(client, Substitute.For<ILogger<ObservationIngestService>>());

        var result = await sut.CreateObservationsAsync(Guid.NewGuid(), new JsonataTransform("[\"not-an-object\"]"), "{\"x\":1}");

        result.Success.Should().BeFalse();
        result.Detail.Should().Contain("must be a JSON object");
    }

    [Fact]
    public async Task CreateEntityFails_ReturnsFailure()
    {
        var client = Substitute.For<IEndpointMyceliumClient>();
        client.FindThingByNameAsync("X").Returns((MyceliumClient.MyceliumThing?)null);
        client.CreateThingAsync("X", Arg.Any<Dictionary<string, object?>>()).Returns((MyceliumClient.MyceliumThing?)null);
        var sut = new ObservationIngestService(client, Substitute.For<ILogger<ObservationIngestService>>());

        var result = await sut.CreateObservationsAsync(Guid.NewGuid(),
            new JsonataTransform("{\"name\":\"X\",\"properties\":{\"v\":1}}"), "{\"x\":1}");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Failed to create entity thing");
    }

    [Fact]
    public async Task ObservedPredicateResolutionFails_ReturnsFailure()
    {
        var entityId = Guid.NewGuid();
        var client = Substitute.For<IEndpointMyceliumClient>();
        client.FindThingByNameAsync("E").Returns((MyceliumClient.MyceliumThing?)null);
        client.CreateThingAsync("E", Arg.Any<Dictionary<string, object?>>())
            .Returns(new MyceliumClient.MyceliumThing(entityId, "E"));
        client.SetPropertyModeAsync(entityId, Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        client.FindThingByNameAsync("observed").Returns((MyceliumClient.MyceliumThing?)null);
        client.CreateThingAsync("observed", Arg.Any<Dictionary<string, object?>>()).Returns((MyceliumClient.MyceliumThing?)null);
        var sut = new ObservationIngestService(client, Substitute.For<ILogger<ObservationIngestService>>());

        var result = await sut.CreateObservationsAsync(Guid.NewGuid(),
            new JsonataTransform("{\"name\":\"E\",\"properties\":{\"v\":1}}"), "{\"x\":1}");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("resolve or create 'observed' predicate");
    }

    [Fact]
    public async Task RelateFails_ReturnsFailure()
    {
        var entityId = Guid.NewGuid();
        var observedId = Guid.NewGuid();
        var client = Substitute.For<IEndpointMyceliumClient>();
        client.FindThingByNameAsync("E").Returns((MyceliumClient.MyceliumThing?)null);
        client.FindThingByNameAsync("observed").Returns(new MyceliumClient.MyceliumThing(observedId, "observed"));
        client.CreateThingAsync("E", Arg.Any<Dictionary<string, object?>>())
            .Returns(new MyceliumClient.MyceliumThing(entityId, "E"));
        client.SetPropertyModeAsync(entityId, Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        client.CreateRelationshipAsync(Arg.Any<Guid>(), observedId, entityId).Returns(false);
        var sut = new ObservationIngestService(client, Substitute.For<ILogger<ObservationIngestService>>());

        var result = await sut.CreateObservationsAsync(Guid.NewGuid(),
            new JsonataTransform("{\"name\":\"E\",\"properties\":{\"v\":1}}"), "{\"x\":1}");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("relate entity to endpoint");
    }

    [Fact]
    public async Task SubmitObservationsFails_ReturnsFailure()
    {
        var entityId = Guid.NewGuid();
        var client = Substitute.For<IEndpointMyceliumClient>();
        client.FindThingByNameAsync("S").Returns(new MyceliumClient.MyceliumThing(entityId, "S"));
        client.SubmitObservationsAsync(entityId, Arg.Any<IReadOnlyList<ObservationSample>>()).Returns(false);
        var sut = new ObservationIngestService(client, Substitute.For<ILogger<ObservationIngestService>>());

        var result = await sut.CreateObservationsAsync(Guid.NewGuid(),
            new JsonataTransform("{\"name\":\"S\",\"properties\":{\"v\":1}}"), "{\"x\":1}");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("submit observations");
    }

    private static ObservationIngestService CreateService()
    {
        var client = Substitute.For<IEndpointMyceliumClient>();
        var logger = Substitute.For<ILogger<ObservationIngestService>>();
        return new ObservationIngestService(client, logger);
    }
}
