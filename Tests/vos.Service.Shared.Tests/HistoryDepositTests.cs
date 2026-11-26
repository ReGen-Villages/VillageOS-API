using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Shared.Tests;

public class HistoryDepositTests
{
    private const string MyceliumUrl = "http://localhost:7243";
    private static readonly DateTime FirstHour = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed class Broker(HttpStatusCode status)
    {
        public List<(string Path, string Body)> Asked { get; } = [];

        public HttpResponseMessage Answer(HttpRequestMessage request)
        {
            Asked.Add((request.RequestUri!.AbsolutePath, request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? ""));
            return new HttpResponseMessage(status) { Content = new StringContent("{\"error\":\"refused\"}", Encoding.UTF8, "application/json") };
        }
    }

    private static TestableMyceliumClient ClientOver(Broker broker) =>
        new(new PerCallHttpClientFactory(new MockHttpMessageHandler(broker.Answer)), NullLogger.Instance, MyceliumUrl,
            new LaunchToken("service-token"));

    private static IReadOnlyList<ObservationSample> Hourly(string property, int hours) =>
        [.. Enumerable.Range(0, hours).Select(hour => new ObservationSample(property, 1.5, FirstHour.AddHours(hour)))];

    [Fact]
    public async Task Every_service_deposits_readings_through_the_bulk_route_in_requests_that_fit_its_limit()
    {
        var broker = new Broker(HttpStatusCode.Accepted);
        var site = Guid.NewGuid();

        var answer = await ClientOver(broker).DepositReadingsAsync(site, Hourly("humidityRatioGramsPerKilogram", MyceliumClientBase.ReadingsPerDeposit + 1));

        answer.Accepted.Should().BeTrue();
        broker.Asked.Select(asked => asked.Path).Should().Equal("/api/sediment", "/api/sediment");
        var first = JsonDocument.Parse(broker.Asked[0].Body).RootElement;
        first.GetArrayLength().Should().Be(MyceliumClientBase.ReadingsPerDeposit);
        first[0].GetProperty("objectId").GetGuid().Should().Be(site);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public async Task A_refused_deposit_stops_and_says_whether_it_can_be_tried_again(HttpStatusCode refusal, bool triedAgain)
    {
        var broker = new Broker(refusal);

        var answer = await ClientOver(broker).DepositReadingsAsync(Guid.NewGuid(), Hourly("windowsOpenHours", MyceliumClientBase.ReadingsPerDeposit + 1));

        answer.Accepted.Should().BeFalse();
        answer.CanBeTriedAgain.Should().Be(triedAgain);
        broker.Asked.Should().ContainSingle();
    }

    [Fact]
    public async Task A_history_change_is_reported_once_for_the_thing()
    {
        var broker = new Broker(HttpStatusCode.OK);
        var site = Guid.NewGuid();

        (await ClientOver(broker).ReportHistoryChangedAsync(site)).Accepted.Should().BeTrue();

        broker.Asked.Select(asked => asked.Path).Should().Equal($"/api/things/{site}/history/changed");
    }
}
