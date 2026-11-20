using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Shared;
using Xunit;
using TributaryClient = vos.Service.Tributary.Services.MyceliumClient;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

public class TributaryAsksForAThingsHistoryToBeJudgedTests : IClassFixture<TheEngine>
{
    private const string Property = "temperatureCelsius";
    private static readonly DateTime FirstHour = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly TheEngine _engine;

    public TributaryAsksForAThingsHistoryToBeJudgedTests(TheEngine engine) => _engine = engine;

    private TributaryClient Tributary() => new(
        _engine.ClientFactory, NullLogger<TributaryClient>.Instance, TheEngine.Url, TimeProvider.System,
        launchToken: new LaunchToken(_engine.AdminToken));

    private async Task<double?> CurrentValueAsync(Guid site)
    {
        var properties = await _engine.Admin.GetFromJsonAsync<JsonElement>($"/api/things/{site}/properties");
        foreach (var property in properties.EnumerateObject())
            if (property.Name.EndsWith(Property, StringComparison.OrdinalIgnoreCase)
                && property.Value.TryGetProperty("Value", out var value) && value.ValueKind == JsonValueKind.Number)
                return value.GetDouble();
        return null;
    }

    private static IReadOnlyList<ObservationSample> Hourly(int hours) =>
        [.. Enumerable.Range(0, hours).Select(hour => new ObservationSample(Property, hour + 0.5, FirstHour.AddHours(hour)))];

    [FactAgainstTheEngine]
    public async Task A_deposit_leaves_the_current_value_and_judging_the_history_makes_the_newest_reading_current()
    {
        var site = await _engine.DeclareAsync("Willow Bend", properties: new Dictionary<string, object?> { [Property] = -99.0 });
        (await Tributary().DepositReadingsAsync(site, Hourly(3))).Accepted.Should().BeTrue();

        var afterTheDeposit = await CurrentValueAsync(site);
        var judged = await Tributary().ReportHistoryChangedAsync(site);

        afterTheDeposit.Should().Be(-99.0, "a deposit stays silent until the writer asks for the history to be judged");
        judged.Accepted.Should().BeTrue(judged.Refusal);
        (await CurrentValueAsync(site)).Should().Be(2.5);
    }

    [FactAgainstTheEngine]
    public async Task Asking_twice_changes_nothing_the_first_time_did_not()
    {
        var site = await _engine.DeclareAsync("Elder Field", properties: new Dictionary<string, object?> { [Property] = -99.0 });
        (await Tributary().DepositReadingsAsync(site, Hourly(3))).Accepted.Should().BeTrue();

        (await Tributary().ReportHistoryChangedAsync(site)).Accepted.Should().BeTrue();
        (await Tributary().ReportHistoryChangedAsync(site)).Accepted.Should().BeTrue();

        (await CurrentValueAsync(site)).Should().Be(2.5);
    }

    [FactAgainstTheEngine]
    public async Task A_thing_the_model_does_not_hold_is_refused_for_good()
    {
        var answer = await Tributary().ReportHistoryChangedAsync(Guid.NewGuid());

        answer.Accepted.Should().BeFalse();
        answer.CanBeTriedAgain.Should().BeFalse();
        answer.Refusal.Should().StartWith("404: ");
    }
}
