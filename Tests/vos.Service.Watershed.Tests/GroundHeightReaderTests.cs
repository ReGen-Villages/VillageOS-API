using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Shared;
using vos.Service.Shared.DagNode;
using vos.Service.Watershed.Services;
using vos.Service.Watershed.Terrain;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Watershed.Tests;

public class GroundHeightReaderTests
{
    private const string MyceliumUrl = "http://localhost:7243";
    private const string Endpoint = "a heights source";

    private sealed class Fetcher(Func<int, HttpResponseMessage>? answer = null)
    {
        public List<JsonElement> Asked { get; } = [];
        public List<string> Paths { get; } = [];

        public HttpResponseMessage Answer(HttpRequestMessage request)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            var asked = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()).RootElement.Clone();
            Asked.Add(asked);
            return answer?.Invoke(Asked.Count - 1) ?? HeightsOf(LocationsIn(asked).Select(location => location.Latitude * 10));
        }
    }

    private static IReadOnlyList<Position> LocationsIn(JsonElement asked) =>
    [
        .. asked.GetProperty("addressParameters").GetProperty("locations").GetString()!.Split('|').Select(pair =>
        {
            var parts = pair.Split(',');
            return new Position(double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
        }),
    ];

    private static HttpResponseMessage HeightsOf(IEnumerable<double?> heights) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            results = heights.Select(height => new { elevation = height }),
            status = "OK",
        }), Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage HeightsOf(IEnumerable<double> heights) => HeightsOf(heights.Select(height => (double?)height));

    private static IReadOnlyList<Position> Points(int count) => [.. Enumerable.Range(0, count).Select(index => new Position(index, -index))];

    private static (GroundHeightReader Reader, ClockThatWaitsNoTime Clock) ReaderAnsweredBy(Fetcher fetcher)
    {
        var clock = new ClockThatWaitsNoTime();
        return (new GroundHeightReader(new PerCallHttpClientFactory(new MockHttpMessageHandler(fetcher.Answer)),
            NullLogger<GroundHeightReader>.Instance, MyceliumUrl, clock, new LaunchToken("svc-jwt")), clock);
    }

    [Fact]
    public async Task The_heights_are_asked_through_the_fetcher_by_the_sources_name_at_most_a_hundred_points_a_call_in_order()
    {
        var fetcher = new Fetcher();
        var (reader, _) = ReaderAnsweredBy(fetcher);

        var heights = await reader.HeightsAtAsync(Endpoint, Points(250), CancellationToken.None);

        heights.Should().Equal(Enumerable.Range(0, 250).Select(index => index * 10.0));
        fetcher.Paths.Should().AllBe($"/api/endpoints/{OutsideCalls.FetcherSubdomain}");
        fetcher.Asked.Select(asked => asked.GetProperty("endpointName").GetString()).Should().AllBe(Endpoint);
        fetcher.Asked.Select(asked => LocationsIn(asked).Count).Should().Equal(100, 100, 50);
    }

    [Fact]
    public async Task Each_call_after_the_first_waits_a_second_after_the_last()
    {
        var (reader, clock) = ReaderAnsweredBy(new Fetcher());

        await reader.HeightsAtAsync(Endpoint, Points(250), CancellationToken.None);

        clock.Waits.Should().HaveCount(2).And.OnlyContain(wait => wait > TimeSpan.FromSeconds(0.9) && wait <= GroundHeightReader.GapBetweenCalls);
    }

    [Fact]
    public async Task A_point_the_provider_holds_no_height_for_is_open_water_at_nought_metres()
    {
        var (reader, _) = ReaderAnsweredBy(new Fetcher(_ => HeightsOf(new double?[] { 12.5, null })));

        var heights = await reader.HeightsAtAsync(Endpoint, Points(2), CancellationToken.None);

        heights.Should().Equal(12.5, 0);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task A_limit_or_a_provider_away_can_be_tried_again(HttpStatusCode status)
    {
        var (reader, _) = ReaderAnsweredBy(new Fetcher(_ => new HttpResponseMessage(status)));

        var asking = () => reader.HeightsAtAsync(Endpoint, Points(3), CancellationToken.None);

        await asking.Should().ThrowAsync<FailureCanBeTriedAgainException>();
    }

    [Fact]
    public async Task A_limit_that_says_how_long_to_wait_passes_the_wait_on()
    {
        var (reader, _) = ReaderAnsweredBy(new Fetcher(_ =>
        {
            var refused = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            refused.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return refused;
        }));

        var asking = () => reader.HeightsAtAsync(Endpoint, Points(3), CancellationToken.None);

        (await asking.Should().ThrowAsync<FailureCanBeTriedAgainException>()).Which.Wait.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Any_other_refusal_fails_the_run(HttpStatusCode status)
    {
        var (reader, _) = ReaderAnsweredBy(new Fetcher(_ => new HttpResponseMessage(status)));

        var asking = () => reader.HeightsAtAsync(Endpoint, Points(3), CancellationToken.None);

        (await asking.Should().ThrowAsync<NodeFailedException>()).Which.Should().NotBeOfType<FailureCanBeTriedAgainException>();
    }

    [Fact]
    public async Task An_answer_holding_fewer_heights_than_points_asked_fails_the_run()
    {
        var (reader, _) = ReaderAnsweredBy(new Fetcher(_ => HeightsOf(new double[] { 1 })));

        var asking = () => reader.HeightsAtAsync(Endpoint, Points(3), CancellationToken.None);

        (await asking.Should().ThrowAsync<NodeFailedException>()).Which.Should().NotBeOfType<FailureCanBeTriedAgainException>();
    }
}
