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

    private static GroundHeightReader LineAnsweredBy(Fetcher fetcher, SteppingClock clock) =>
        new(new PerCallHttpClientFactory(new MockHttpMessageHandler(fetcher.Answer)),
            NullLogger<GroundHeightReader>.Instance, MyceliumUrl, clock, new LaunchToken("svc-jwt"));

    private static async Task<IReadOnlyList<double>> AskAsync(GroundHeightReader reader, int points, CancellationToken cancellationToken)
    {
        using var place = reader.Join(GroundHeightReader.CallsFor(points), timeLeft: null);
        return await reader.HeightsAtAsync(place, Endpoint, Points(points), cancellationToken);
    }

    [Fact]
    public void A_step_whose_calls_behind_those_ahead_would_pass_its_time_left_answers_at_once_to_try_again_when_those_ahead_are_done()
    {
        var fetcher = new Fetcher();
        var reader = LineAnsweredBy(fetcher, new SteppingClock());
        using var ahead = reader.Join(20, timeLeft: null);

        var joining = () => reader.Join(9, TimeSpan.FromSeconds(10));

        var answer = joining.Should().Throw<FailureCanBeTriedAgainException>().Which;
        answer.Wait.Should().Be(TimeSpan.FromSeconds(20));
        answer.Message.Should().StartWith("The height line is full: 20 calls are ahead of this step's 9");
        fetcher.Asked.Should().BeEmpty();
    }

    [Theory]
    [InlineData(28, false)]
    [InlineData(29, true)]
    public void Before_any_call_is_timed_a_call_costs_the_one_second_gap(int secondsLeft, bool joins)
    {
        var reader = LineAnsweredBy(new Fetcher(), new SteppingClock());
        using var ahead = reader.Join(20, timeLeft: null);

        var joining = () => reader.Join(9, TimeSpan.FromSeconds(secondsLeft)).Dispose();

        if (joins)
            joining.Should().NotThrow();
        else
            joining.Should().Throw<FailureCanBeTriedAgainException>().Which.Wait.Should().Be(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public void A_step_with_nothing_ahead_joins_even_when_its_own_calls_would_pass_its_time_left()
    {
        var reader = LineAnsweredBy(new Fetcher(), new SteppingClock());

        var joining = () => reader.Join(40, TimeSpan.FromSeconds(5)).Dispose();

        joining.Should().NotThrow();
    }

    [Fact]
    public void A_step_that_joins_counts_as_ahead_of_the_next_one()
    {
        var reader = LineAnsweredBy(new Fetcher(), new SteppingClock());
        using var first = reader.Join(5, TimeSpan.FromSeconds(28));

        var joining = () => reader.Join(9, TimeSpan.FromSeconds(13));

        joining.Should().Throw<FailureCanBeTriedAgainException>().Which.Message.Should().Contain("5 calls are ahead");
    }

    [Fact]
    public async Task Each_call_made_leaves_the_line()
    {
        var reader = LineAnsweredBy(new Fetcher(), new SteppingClock());
        using var first = reader.Join(3, timeLeft: null);
        await reader.HeightsAtAsync(first, Endpoint, Points(GroundHeightReader.MostPointsInACall), CancellationToken.None);

        var joining = () => reader.Join(1, TimeSpan.Zero);

        var answer = joining.Should().Throw<FailureCanBeTriedAgainException>().Which;
        answer.Message.Should().Contain("2 calls are ahead");
        answer.Wait.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task A_step_cancelled_in_the_line_takes_the_calls_it_never_made_out_of_it()
    {
        using var cancelling = new CancellationTokenSource();
        var reader = LineAnsweredBy(new Fetcher(_ =>
        {
            cancelling.Cancel();
            throw new TaskCanceledException("the caller cancelled");
        }), new SteppingClock());

        var asking = () => AskAsync(reader, 9 * GroundHeightReader.MostPointsInACall, cancelling.Token);
        await asking.Should().ThrowAsync<OperationCanceledException>();

        var joining = () => reader.Join(1, TimeSpan.Zero).Dispose();
        joining.Should().NotThrow("nothing is left ahead");
    }

    [Fact]
    public async Task A_call_costs_the_gap_and_the_time_the_last_call_took()
    {
        var clock = new SteppingClock();
        var reader = LineAnsweredBy(new Fetcher(_ =>
        {
            clock.Advance(TimeSpan.FromSeconds(2));
            return HeightsOf(Enumerable.Repeat(1.0, GroundHeightReader.MostPointsInACall));
        }), clock);
        using var first = reader.Join(2, timeLeft: null);
        await reader.HeightsAtAsync(first, Endpoint, Points(GroundHeightReader.MostPointsInACall), CancellationToken.None);

        var joining = () => reader.Join(1, TimeSpan.FromSeconds(5));

        joining.Should().Throw<FailureCanBeTriedAgainException>().Which.Wait.Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void With_no_limit_a_step_joins_however_long_the_line()
    {
        var reader = LineAnsweredBy(new Fetcher(), new SteppingClock());
        using var ahead = reader.Join(500, timeLeft: null);

        var joining = () => reader.Join(9, timeLeft: null).Dispose();

        joining.Should().NotThrow();
    }

    [Fact]
    public async Task The_heights_are_asked_through_the_fetcher_by_the_sources_name_at_most_a_hundred_points_a_call_in_order()
    {
        var fetcher = new Fetcher();
        var (reader, _) = ReaderAnsweredBy(fetcher);

        var heights = await AskAsync(reader, 250, CancellationToken.None);

        heights.Should().Equal(Enumerable.Range(0, 250).Select(index => index * 10.0));
        fetcher.Paths.Should().AllBe($"/api/endpoints/{OutsideCalls.FetcherSubdomain}");
        fetcher.Asked.Select(asked => asked.GetProperty("endpointName").GetString()).Should().AllBe(Endpoint);
        fetcher.Asked.Select(asked => LocationsIn(asked).Count).Should().Equal(100, 100, 50);
    }

    [Fact]
    public async Task Each_call_after_the_first_waits_a_second_after_the_last()
    {
        var (reader, clock) = ReaderAnsweredBy(new Fetcher());

        await AskAsync(reader, 250, CancellationToken.None);

        clock.Waits.Should().HaveCount(2).And.OnlyContain(wait => wait > TimeSpan.FromSeconds(0.9) && wait <= GroundHeightReader.GapBetweenCalls);
    }

    [Fact]
    public async Task A_point_the_provider_holds_no_height_for_is_open_water_at_nought_metres()
    {
        var (reader, _) = ReaderAnsweredBy(new Fetcher(_ => HeightsOf(new double?[] { 12.5, null })));

        var heights = await AskAsync(reader, 2, CancellationToken.None);

        heights.Should().Equal(12.5, 0);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task A_limit_or_a_provider_away_can_be_tried_again(HttpStatusCode status)
    {
        var (reader, _) = ReaderAnsweredBy(new Fetcher(_ => new HttpResponseMessage(status)));

        var asking = () => AskAsync(reader, 3, CancellationToken.None);

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

        var asking = () => AskAsync(reader, 3, CancellationToken.None);

        (await asking.Should().ThrowAsync<FailureCanBeTriedAgainException>()).Which.Wait.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_source_that_cannot_be_reached_or_answers_too_late_can_be_tried_again(bool answersTooLate)
    {
        var (reader, _) = ReaderAnsweredBy(new Fetcher(_ =>
            throw (answersTooLate ? new TaskCanceledException("the call timed out") : (Exception)new HttpRequestException("connection refused"))));

        var asking = () => AskAsync(reader, 3, CancellationToken.None);

        await asking.Should().ThrowAsync<FailureCanBeTriedAgainException>();
    }

    [Fact]
    public async Task A_run_cancelled_by_its_caller_is_not_tried_again()
    {
        using var cancelling = new CancellationTokenSource();
        var (reader, _) = ReaderAnsweredBy(new Fetcher(_ =>
        {
            cancelling.Cancel();
            throw new TaskCanceledException("the caller cancelled");
        }));

        var asking = () => AskAsync(reader, 3, cancelling.Token);

        await asking.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Any_other_refusal_fails_the_run(HttpStatusCode status)
    {
        var (reader, _) = ReaderAnsweredBy(new Fetcher(_ => new HttpResponseMessage(status)));

        var asking = () => AskAsync(reader, 3, CancellationToken.None);

        (await asking.Should().ThrowAsync<NodeFailedException>()).Which.Should().NotBeOfType<FailureCanBeTriedAgainException>();
    }

    [Fact]
    public async Task An_answer_holding_fewer_heights_than_points_asked_fails_the_run()
    {
        var (reader, _) = ReaderAnsweredBy(new Fetcher(_ => HeightsOf(new double[] { 1 })));

        var asking = () => AskAsync(reader, 3, CancellationToken.None);

        (await asking.Should().ThrowAsync<NodeFailedException>()).Which.Should().NotBeOfType<FailureCanBeTriedAgainException>();
    }
}
