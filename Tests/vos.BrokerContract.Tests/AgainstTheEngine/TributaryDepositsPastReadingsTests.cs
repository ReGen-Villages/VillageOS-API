using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Shared;
using Xunit;
using TributaryClient = vos.Service.Tributary.Services.MyceliumClient;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

public class TributaryDepositsPastReadingsTests : IClassFixture<TheEngine>
{
    private static readonly DateTime FirstHour = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly TheEngine _engine;

    public TributaryDepositsPastReadingsTests(TheEngine engine) => _engine = engine;

    private TributaryClient Tributary() => new(
        _engine.ClientFactory, NullLogger<TributaryClient>.Instance, TheEngine.Url, TimeProvider.System,
        launchToken: new LaunchToken(_engine.AdminToken));

    private async Task<JsonElement> HistoryAsync(Guid site, string property) =>
        await _engine.Admin.GetFromJsonAsync<JsonElement>($"/api/things/{site}/properties/{property}/history");

    private static IEnumerable<ObservationSample> Hourly(string property, int hours) =>
        Enumerable.Range(0, hours).Select(hour => new ObservationSample(property, hour % 40 + 0.5, FirstHour.AddHours(hour)));

    [FactAgainstTheEngine]
    public async Task A_deposit_through_the_bulk_route_lands_each_reading_at_its_time()
    {
        var site = await _engine.DeclareAsync("Willow Bend", properties: new Dictionary<string, object?> { ["temperatureCelsius"] = 0.0 });

        var answer = await Tributary().DepositReadingsAsync(site, [.. Hourly("temperatureCelsius", 3)]);

        answer.Accepted.Should().BeTrue(answer.Refusal);
        var stored = (await HistoryAsync(site, "temperatureCelsius")).GetProperty("samples").EnumerateArray()
            .Where(sample => sample.GetProperty("observedAt").GetDateTime() < FirstHour.AddDays(1))
            .Select(sample => (sample.GetProperty("observedAt").GetDateTime(), sample.GetProperty("value").GetDouble()));
        stored.Should().Equal((FirstHour, 0.5), (FirstHour.AddHours(1), 1.5), (FirstHour.AddHours(2), 2.5));
    }

    [FactAgainstTheEngine]
    public async Task A_slice_over_one_requests_worth_lands_whole_and_sending_it_again_stores_each_reading_once()
    {
        var site = await _engine.DeclareAsync("Elder Field", properties: new Dictionary<string, object?>
        {
            ["temperatureCelsius"] = 0.0,
            ["rainMillimetres"] = 0.0,
        });
        var slice = Hourly("temperatureCelsius", TributaryClient.ReadingsPerDeposit / 2 + 1)
            .Concat(Hourly("rainMillimetres", TributaryClient.ReadingsPerDeposit / 2)).ToList();

        (await Tributary().DepositReadingsAsync(site, slice)).Accepted.Should().BeTrue();
        (await Tributary().DepositReadingsAsync(site, slice)).Accepted.Should().BeTrue();

        (await HistoryAsync(site, "temperatureCelsius")).GetProperty("samples").EnumerateArray()
            .Count(sample => sample.GetProperty("source").GetString() != "fact")
            .Should().Be(TributaryClient.ReadingsPerDeposit / 2 + 1);
        (await HistoryAsync(site, "rainMillimetres")).GetProperty("samples").EnumerateArray()
            .Count(sample => sample.GetProperty("source").GetString() != "fact")
            .Should().Be(TributaryClient.ReadingsPerDeposit / 2);
    }

    [FactAgainstTheEngine]
    public async Task A_reading_of_a_property_the_thing_does_not_declare_is_refused_with_the_brokers_words()
    {
        var site = await _engine.DeclareAsync("Hazel Croft", properties: new Dictionary<string, object?> { ["temperatureCelsius"] = 0.0 });

        var answer = await Tributary().DepositReadingsAsync(site, [.. Hourly("snowfallCentimetres", 1)]);

        answer.Accepted.Should().BeFalse();
        answer.CanBeTriedAgain.Should().BeFalse();
        answer.Refusal.Should().StartWith("404: ").And.Contain("snowfallCentimetres");
    }
}
