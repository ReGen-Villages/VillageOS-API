using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Shared;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;
using vos.Service.Tributary.Helpers;
using vos.Service.Tributary.Services;
using Xunit;
using TributaryClient = vos.Service.Tributary.Services.MyceliumClient;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

public class TributaryDepositsAHistorySourcesReadingsTests : IClassFixture<TheEngine>
{
    private const string Property = "temperatureCelsius";
    private static readonly DateTime FirstHour = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Role DirectoryRole = new("directory", RoleIdentity.Of("directory"));

    private readonly TheEngine _engine;

    public TributaryDepositsAHistorySourcesReadingsTests(TheEngine engine) => _engine = engine;

    private TributaryClient Tributary(string bearer) => new(
        _engine.ClientFactory, NullLogger<TributaryClient>.Instance, TheEngine.Url, TimeProvider.System,
        launchToken: new LaunchToken(bearer));

    private SubscriptionClient Subscriptions() => new(
        _engine.ClientFactory, NullLogger<SubscriptionClient>.Instance, TheEngine.Url, new LaunchToken(_engine.AdminToken));

    private static List<ObservationSample> Hourly(string property, int hours) =>
        [.. Enumerable.Range(0, hours).Select(hour => new ObservationSample(property, hour + 0.5, FirstHour.AddHours(hour)))];

    private static string Slice(int hours) => JsonSerializer.Serialize(Enumerable.Range(0, hours).Select(hour => new
    {
        observedAt = FirstHour.AddHours(hour),
        properties = new Dictionary<string, double> { [Property] = hour + 0.5 },
    }));

    private async Task<double?> CurrentValueAsync(Guid site)
    {
        var properties = await _engine.Admin.GetFromJsonAsync<JsonElement>($"/api/things/{site}/properties");
        foreach (var property in properties.EnumerateObject())
            if (property.Name.EndsWith(Property, StringComparison.OrdinalIgnoreCase)
                && property.Value.TryGetProperty("Value", out var value) && value.ValueKind == JsonValueKind.Number)
                return value.GetDouble();
        return null;
    }

    private async Task<int> StoredReadingsAsync(Guid site, string property) =>
        (await _engine.Admin.GetFromJsonAsync<JsonElement>($"/api/things/{site}/properties/{property}/history"))
        .GetProperty("samples").EnumerateArray().Count(sample => sample.GetProperty("source").GetString() != "fact");

    private async Task<Guid> RegistrationOfTheHistoryKindAsync()
    {
        var isPredicate = await _engine.DeclareOnceAsync("is");
        var endpointRoot = await _engine.DeclareOnceAsync("Endpoint", isArchetype: true);
        var historyKind = await _engine.DeclareOnceAsync("HistoryEndpoint", isArchetype: true);
        await _engine.RelateAsync(historyKind, isPredicate, endpointRoot);
        await _engine.BindOnceAsync(LandIntakeRoles.HistorySource, historyKind);

        var registration = await _engine.DeclareAsync($"climate-history {Guid.NewGuid():N}");
        await _engine.RelateAsync(registration, isPredicate, historyKind);
        return registration;
    }

    [FactAgainstTheEngine]
    public async Task A_history_sources_slice_waits_in_storage_until_its_subjects_history_is_judged()
    {
        var registration = await RegistrationOfTheHistoryKindAsync();
        var site = await _engine.DeclareAsync("Willow Bend", properties: new Dictionary<string, object?> { [Property] = -99.0 });
        var read = await Subscriptions().ReadSnapshotAsync(EndpointKindResolver.SelectorFor(registration));
        var ingest = new ObservationIngestService(Tributary(_engine.AdminToken), new ModelClock());

        EndpointKindResolver.IsHistorySource(read.Snapshot, registration).Should().BeTrue();
        var result = await ingest.CreateObservationsAsync(
            registration, new JsonataTransform("$"), Slice(3), site,
            new ObservedEdges(Guid.NewGuid(), new HashSet<Guid> { site }, null), depositsTimedReadings: true);
        var afterTheSlice = await CurrentValueAsync(site);
        (await Tributary(_engine.AdminToken).ReportHistoryChangedAsync(site)).Accepted.Should().BeTrue();

        result.Success.Should().BeTrue($"{result.Error} {result.Detail}");
        afterTheSlice.Should().Be(-99.0, "a deposit stays silent until the subject's history is judged");
        (await CurrentValueAsync(site)).Should().Be(2.5);
    }

    [FactAgainstTheEngine]
    public async Task A_deposit_onto_a_directory_is_refused_for_a_service_as_the_readings_route_refuses_it()
    {
        var isPredicate = await _engine.DeclareOnceAsync("is");
        var directoryKind = await _engine.DeclareOnceAsync("Directory", isArchetype: true);
        await _engine.BindOnceAsync(DirectoryRole, directoryKind);
        var directory = await _engine.DeclareAsync($"Sign-in directory {Guid.NewGuid():N}",
            properties: new Dictionary<string, object?> { [Property] = 0.0 });
        await _engine.RelateAsync(directory, isPredicate, directoryKind);

        var answer = await Tributary(await _engine.ServiceTokenAsync("tributary")).DepositReadingsAsync(directory, Hourly(Property, 2));

        answer.Accepted.Should().BeFalse();
        answer.CanBeTriedAgain.Should().BeFalse();
        answer.Refusal.Should().Be("403: Only an admin writes a directory.");
        (await StoredReadingsAsync(directory, Property)).Should().Be(0);
    }

    // Requests go in property-name order, so rainMillimetres is stored before the request for an undeclared
    // temperatureCelsius is refused.
    [FactAgainstTheEngine]
    public async Task Sending_a_refused_deposit_again_from_the_start_stores_no_reading_twice()
    {
        var site = await _engine.DeclareAsync("Elder Field", properties: new Dictionary<string, object?> { ["rainMillimetres"] = 0.0 });
        var slice = Hourly("rainMillimetres", TributaryClient.ReadingsPerDeposit / 2 + 1)
            .Concat(Hourly(Property, TributaryClient.ReadingsPerDeposit / 2)).ToList();

        var refused = await Tributary(_engine.AdminToken).DepositReadingsAsync(site, slice);
        var storedBeforeTheRefusal = await StoredReadingsAsync(site, "rainMillimetres");
        (await _engine.Admin.PostAsJsonAsync($"/api/things/{site}/properties", new { name = Property, type = "vos.Double", value = 0.0 }))
            .EnsureSuccessStatusCode();
        var sentAgain = await Tributary(_engine.AdminToken).DepositReadingsAsync(site, slice);

        refused.Accepted.Should().BeFalse();
        storedBeforeTheRefusal.Should().Be(TributaryClient.ReadingsPerDeposit / 2 + 1);
        sentAgain.Accepted.Should().BeTrue(sentAgain.Refusal);
        (await StoredReadingsAsync(site, "rainMillimetres")).Should().Be(TributaryClient.ReadingsPerDeposit / 2 + 1);
        (await StoredReadingsAsync(site, Property)).Should().Be(TributaryClient.ReadingsPerDeposit / 2);
    }
}
