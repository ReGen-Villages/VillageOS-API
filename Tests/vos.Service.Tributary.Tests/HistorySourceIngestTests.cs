using FluentAssertions;
using NSubstitute;
using vos.Service.Shared;
using vos.Service.Shared.Subscriptions;
using vos.Service.Tributary.Helpers;
using vos.Service.Tributary.Services;
using Xunit;

namespace vos.Service.Tributary.Tests;

public class HistorySourceIngestTests
{
    private static readonly JsonataTransform AsAnswered = new("$");

    private const string TimedSliceWithItsOffset = """
        [
          {"observedAt":"2024-01-01T00:00:00Z","properties":{"temperatureCelsius":1.5}},
          {"observedAt":"2024-01-01T01:00:00Z","properties":{"temperatureCelsius":2.5}},
          {"properties":{"utcOffsetSeconds":3600}}
        ]
        """;

    private static readonly Guid Endpoint = Guid.NewGuid();
    private static readonly Guid Site = Guid.NewGuid();

    private static IEndpointMyceliumClient Broker(BrokerAnswer deposited)
    {
        var client = Substitute.For<IEndpointMyceliumClient>();
        client.DepositReadingsAsync(Site, Arg.Any<IReadOnlyList<ObservationSample>>(), Arg.Any<CancellationToken>())
            .Returns(deposited);
        client.SubmitObservationsAsync(Site, Arg.Any<IReadOnlyList<ObservationSample>>(), Arg.Any<CancellationToken>())
            .Returns(true);
        return client;
    }

    private static Task<ObservationIngestResult> IngestAsync(IEndpointMyceliumClient client, bool depositsTimedReadings) =>
        new ObservationIngestService(client, new ModelClock()).CreateObservationsAsync(
            Endpoint, AsAnswered, TimedSliceWithItsOffset, Site,
            new ObservedEdges(Guid.NewGuid(), new HashSet<Guid> { Site }, null),
            depositsTimedReadings: depositsTimedReadings);

    [Fact]
    public async Task A_history_sources_timed_readings_are_deposited_and_a_reading_without_a_time_keeps_the_readings_route()
    {
        var client = Broker(BrokerAnswer.Done);

        var result = await IngestAsync(client, depositsTimedReadings: true);

        result.Success.Should().BeTrue($"{result.Error} {result.Detail}");
        result.ObservationsSubmitted.Should().Be(3);
        await client.Received(1).DepositReadingsAsync(Site,
            Arg.Is<IReadOnlyList<ObservationSample>>(readings =>
                readings.Count == 2 && readings.All(reading => reading.Property == "temperatureCelsius" && reading.ObservedAt != null)),
            Arg.Any<CancellationToken>());
        await client.Received(1).SubmitObservationsAsync(Site,
            Arg.Is<IReadOnlyList<ObservationSample>>(readings =>
                readings.Count == 1 && readings[0].Property == "utcOffsetSeconds" && readings[0].ObservedAt == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_source_that_is_not_a_history_source_sends_every_reading_to_the_readings_route()
    {
        var client = Broker(BrokerAnswer.Done);

        var result = await IngestAsync(client, depositsTimedReadings: false);

        result.Success.Should().BeTrue($"{result.Error} {result.Detail}");
        await client.DidNotReceive().DepositReadingsAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyList<ObservationSample>>(), Arg.Any<CancellationToken>());
        await client.Received(1).SubmitObservationsAsync(Site,
            Arg.Is<IReadOnlyList<ObservationSample>>(readings => readings.Count == 3), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_refused_deposit_fails_the_call_in_the_brokers_words_and_writes_nothing_else()
    {
        var client = Broker(new BrokerAnswer(false, "503: The readings queue is full.", CanBeTriedAgain: true));

        var result = await IngestAsync(client, depositsTimedReadings: true);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("The readings queue is full.");
        result.CanBeTriedAgain.Should().BeTrue();
        await client.DidNotReceive().SubmitObservationsAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyList<ObservationSample>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_deposit_the_broker_refuses_for_good_cannot_be_tried_again()
    {
        var client = Broker(new BrokerAnswer(false, "403: Only an admin writes a directory."));

        var result = await IngestAsync(client, depositsTimedReadings: true);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Only an admin writes a directory.");
        result.CanBeTriedAgain.Should().BeFalse();
    }

    [Fact]
    public async Task Readings_resolved_by_name_keep_the_readings_route_even_from_a_history_source()
    {
        var entity = Guid.NewGuid();
        var client = Substitute.For<IEndpointMyceliumClient>();
        client.FindThingByNameAsync("Sensor-1", Arg.Any<CancellationToken>())
            .Returns(new MyceliumClient.MyceliumThing(entity, "Sensor-1"));
        client.SubmitObservationsAsync(entity, Arg.Any<IReadOnlyList<ObservationSample>>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await new ObservationIngestService(client, new ModelClock()).CreateObservationsAsync(
            Endpoint,
            new JsonataTransform("{\"name\":\"Sensor-1\",\"observedAt\":\"2024-01-01T00:00:00Z\",\"properties\":{\"temp\":5.8}}"),
            "{\"x\":1}",
            alreadyObserved: new ObservedEdges(Guid.NewGuid(), new HashSet<Guid> { entity }, null),
            depositsTimedReadings: true);

        result.Success.Should().BeTrue($"{result.Error} {result.Detail}");
        await client.DidNotReceive().DepositReadingsAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyList<ObservationSample>>(), Arg.Any<CancellationToken>());
        await client.Received(1).SubmitObservationsAsync(
            entity, Arg.Any<IReadOnlyList<ObservationSample>>(), Arg.Any<CancellationToken>());
    }
}
