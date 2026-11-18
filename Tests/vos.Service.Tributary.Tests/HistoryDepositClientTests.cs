using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using vos.Service.Shared;
using vos.Service.Tributary.Services;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Tributary.Tests;

public class HistoryDepositClientTests
{
    private static readonly DateTime FirstHour = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed class PerCallFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class Broker(Func<HttpRequestMessage, string, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string Body)> Asked { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Asked.Add((request.Method, request.RequestUri!.AbsolutePath, body));
            return answer(request, body);
        }
    }

    private static MyceliumClient ClientOver(HttpMessageHandler broker) => new(
        new PerCallFactory(broker), Substitute.For<ILogger<MyceliumClient>>(), "http://localhost",
        new ClockThatWaitsNoTime(), new LaunchToken("test-token"));

    private static HttpResponseMessage Answer(HttpStatusCode status, string body = "{}", TimeSpan? retryAfter = null)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (retryAfter is { } wait) response.Headers.RetryAfter = new RetryConditionHeaderValue(wait);
        return response;
    }

    private static IReadOnlyList<ObservationSample> Hourly(string property, int hours) =>
        [.. Enumerable.Range(0, hours).Select(hour => new ObservationSample(property, 1.5, FirstHour.AddHours(hour)))];

    [Fact]
    public async Task A_deposit_posts_each_reading_with_its_subject_to_the_bulk_route()
    {
        var site = Guid.NewGuid();
        var broker = new Broker((_, _) => Answer(HttpStatusCode.Accepted));

        var answer = await ClientOver(broker).DepositReadingsAsync(site, [new ObservationSample("temperatureCelsius", 4.5, FirstHour)]);

        answer.Accepted.Should().BeTrue();
        var (method, path, body) = broker.Asked.Should().ContainSingle().Subject;
        (method, path).Should().Be((HttpMethod.Post, "/api/sediment"));
        var item = JsonDocument.Parse(body).RootElement.EnumerateArray().Should().ContainSingle().Subject;
        item.GetProperty("objectId").GetGuid().Should().Be(site);
        item.GetProperty("property").GetString().Should().Be("temperatureCelsius");
        item.GetProperty("value").GetDouble().Should().Be(4.5);
        item.GetProperty("observedAt").GetDateTime().Should().Be(FirstHour);
    }

    [Fact]
    public async Task A_reading_with_no_time_is_refused_and_nothing_is_sent()
    {
        var broker = new Broker((_, _) => Answer(HttpStatusCode.Accepted));

        var answer = await ClientOver(broker).DepositReadingsAsync(Guid.NewGuid(), [new ObservationSample("utcOffsetSeconds", 3600)]);

        answer.Accepted.Should().BeFalse();
        answer.CanBeTriedAgain.Should().BeFalse();
        answer.Refusal.Should().Contain("utcOffsetSeconds");
        broker.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task The_first_refused_request_ends_the_deposit_with_the_brokers_words()
    {
        var readings = Hourly("temperatureCelsius", MyceliumClient.ReadingsPerDeposit).Concat(Hourly("rainMillimetres", 1)).ToList();
        var broker = new Broker((_, _) => Answer(HttpStatusCode.BadRequest, """{"error":"Property 'rainMillimetres' not defined"}"""));

        var answer = await ClientOver(broker).DepositReadingsAsync(Guid.NewGuid(), readings);

        answer.Accepted.Should().BeFalse();
        answer.CanBeTriedAgain.Should().BeFalse();
        answer.Refusal.Should().Be("400: Property 'rainMillimetres' not defined");
        broker.Asked.Should().ContainSingle("nothing after a refused request is sent");
    }

    [Fact]
    public async Task A_busy_broker_answers_that_the_deposit_may_be_tried_again_after_its_wait()
    {
        var broker = new Broker((_, _) => Answer(HttpStatusCode.ServiceUnavailable, """{"error":"busy"}""", TimeSpan.FromSeconds(3)));

        var answer = await ClientOver(broker).DepositReadingsAsync(Guid.NewGuid(), Hourly("temperatureCelsius", 2));

        answer.Accepted.Should().BeFalse();
        answer.CanBeTriedAgain.Should().BeTrue();
        answer.Wait.Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void A_slice_just_over_the_cap_is_two_requests_and_no_series_is_split_where_it_fits()
    {
        var first = Hourly("temperatureCelsius", MyceliumClient.ReadingsPerDeposit / 2 + 1);
        var second = Hourly("rainMillimetres", MyceliumClient.ReadingsPerDeposit / 2);

        var requests = DepositRequests.Of([.. second, .. first], MyceliumClient.ReadingsPerDeposit);

        requests.Select(request => request.Select(reading => reading.Property).Distinct()).Should().BeEquivalentTo(
            new[] { new[] { "rainMillimetres" }, new[] { "temperatureCelsius" } }, options => options.WithStrictOrdering());
        requests.Sum(request => request.Count).Should().Be(MyceliumClient.ReadingsPerDeposit + 1);
    }

    [Fact]
    public void A_series_longer_than_the_cap_is_split_across_requests_in_time_order()
    {
        var series = Hourly("temperatureCelsius", 25).Reverse().ToList();

        var requests = DepositRequests.Of(series, perRequest: 10);

        requests.Select(request => request.Count).Should().Equal(10, 10, 5);
        requests.SelectMany(request => request).Select(reading => reading.ObservedAt).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Readings_under_the_cap_go_in_one_request()
    {
        DepositRequests.Of([.. Hourly("a", 3), .. Hourly("b", 4)], perRequest: 10).Should().ContainSingle()
            .Which.Should().HaveCount(7);
    }

    [Fact]
    public void A_full_deposit_request_stays_under_the_brokers_request_limit()
    {
        const long brokersRequestLimitBytes = 30_000_000;
        var site = Guid.NewGuid();
        var items = Enumerable.Range(0, MyceliumClient.ReadingsPerDeposit).Select(hour => new
        {
            objectId = site,
            property = "windSpeedAtHeightMetresPerSecond",
            value = -1234.56789,
            observedAt = FirstHour.AddHours(hour),
        });

        JsonSerializer.SerializeToUtf8Bytes(items, JsonSerializerOptions.Web).LongLength.Should().BeLessThan(brokersRequestLimitBytes);
    }

    [Fact]
    public async Task Asking_for_a_things_history_to_be_judged_posts_no_body_to_its_history_route()
    {
        var thing = Guid.NewGuid();
        var broker = new Broker((_, _) => Answer(HttpStatusCode.OK, """{"series":24}"""));

        var answer = await ClientOver(broker).ReportHistoryChangedAsync(thing);

        answer.Accepted.Should().BeTrue();
        var (method, path, body) = broker.Asked.Should().ContainSingle().Subject;
        (method, path, body).Should().Be((HttpMethod.Post, $"/api/things/{thing}/history/changed", ""));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.GatewayTimeout, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    public async Task A_refused_judging_names_the_brokers_words_and_says_whether_it_may_be_tried_again(HttpStatusCode status, bool mayBeTriedAgain)
    {
        var broker = new Broker((_, _) => Answer(status, """{"error":"Thing not found"}"""));

        var answer = await ClientOver(broker).ReportHistoryChangedAsync(Guid.NewGuid());

        answer.Accepted.Should().BeFalse();
        answer.CanBeTriedAgain.Should().Be(mayBeTriedAgain);
        answer.Refusal.Should().Be($"{(int)status}: Thing not found");
    }

    [Fact]
    public async Task A_full_queue_names_its_wait()
    {
        var broker = new Broker((_, _) => Answer(HttpStatusCode.ServiceUnavailable, """{"error":"full"}""", TimeSpan.FromSeconds(1)));

        (await ClientOver(broker).ReportHistoryChangedAsync(Guid.NewGuid())).Wait.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task A_refusal_whose_body_is_not_json_is_named_by_its_body()
    {
        var broker = new Broker((_, _) => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("upstream gone", Encoding.UTF8, "text/plain"),
        });

        (await ClientOver(broker).ReportHistoryChangedAsync(Guid.NewGuid())).Refusal.Should().Be("502: upstream gone");
    }

    [Fact]
    public async Task A_call_its_caller_gave_up_is_not_answered_as_a_refusal()
    {
        using var givenUp = new CancellationTokenSource();
        givenUp.Cancel();
        var broker = new Broker((_, _) => Answer(HttpStatusCode.OK));

        var asking = () => ClientOver(broker).DepositReadingsAsync(Guid.NewGuid(), Hourly("temperatureCelsius", 1), givenUp.Token);

        await asking.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task A_broker_that_cannot_be_reached_may_be_asked_again()
    {
        var broker = new Broker((_, _) => throw new HttpRequestException("Connection refused"));

        var answer = await ClientOver(broker).ReportHistoryChangedAsync(Guid.NewGuid());

        answer.Accepted.Should().BeFalse();
        answer.CanBeTriedAgain.Should().BeTrue();
        answer.Refusal.Should().Contain("Connection refused");
    }
}
