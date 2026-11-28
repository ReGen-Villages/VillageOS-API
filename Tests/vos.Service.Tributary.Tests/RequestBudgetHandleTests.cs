using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using vos.Service.Shared;
using Xunit;
using static vos.Service.Tributary.Tests.MyceliumStub;

namespace vos.Service.Tributary.Tests;

public sealed class RequestBudgetHandleTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 28, 12, 0, 0, TimeSpan.Zero);

    private const string ElevationEndpoint = """
    {
      "Endpoint.url":        {"Value":"https://elevation.test/v1/srtm30m?locations=1,2"},
      "Endpoint.httpMethod": {"Value":"GET"}
    }
    """;

    private const string CachedElevationEndpoint = """
    {
      "Endpoint.url":        {"Value":"https://elevation.test/v1/srtm30m?locations=1,2"},
      "Endpoint.httpMethod": {"Value":"GET"},
      "Endpoint.cacheTtl":   {"Value":"300"}
    }
    """;

    private const string PagedEndpoint = """
    {
      "Endpoint.url":           {"Value":"https://elevation.test/query"},
      "Endpoint.httpMethod":    {"Value":"GET"},
      "Esri.pagingKind":        {"Value":"offset"},
      "Esri.offsetParameter":   {"Value":"resultOffset"},
      "Esri.pageSizeParameter": {"Value":"resultRecordCount"},
      "Esri.pageSize":          {"Value":"2"},
      "Esri.hasMorePath":       {"Value":"exceededTransferLimit"},
      "Esri.itemsPath":         {"Value":"features"}
    }
    """;

    private readonly string _cacheFolder = Directory.CreateTempSubdirectory("tributary-budget-handle-").FullName;

    public void Dispose() => Directory.Delete(_cacheFolder, recursive: true);

    // The deadline's own timer is longer than any wait passed here, so it keeps running.
    private sealed class ClockThatPassesShortWaits() : FakeTimeProvider(RequestBudgetHandleTests.Start)
    {
        private static readonly TimeSpan LongestWaitPassed = TimeSpan.FromSeconds(25);
        private readonly List<TimeSpan> _waits = [];

        public IReadOnlyList<TimeSpan> Waits { get { lock (_waits) return [.. _waits]; } }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            if (dueTime > TimeSpan.Zero && dueTime <= LongestWaitPassed)
            {
                lock (_waits) _waits.Add(dueTime);
                _ = Task.Run(() => Advance(dueTime));
            }
            return timer;
        }
    }

    private sealed record Answer(HttpStatusCode Status, string? RetryAfter, string Body);

    private sealed record Handled(IReadOnlyList<Answer> Answers, int Asked, IReadOnlyList<TimeSpan> Waits);

    private async Task<Handled> HandleAsync(
        int calls, Budget[] budgets, string endpoint = ElevationEndpoint, string? limitSeconds = "30",
        Kind[]? kinds = null, Func<HttpRequestMessage, HttpResponseMessage>? provider = null)
    {
        var endpointId = Guid.NewGuid();
        var asked = 0;
        var clock = new ClockThatPassesShortWaits();
        await using var factory = new TributaryWebApplicationFactory { CacheDirectory = _cacheFolder };
        await factory.InitializeAsync();
        factory.HandlerCallback = request =>
        {
            if (request.RequestUri!.Host == "elevation.test")
            {
                Interlocked.Increment(ref asked);
                return provider?.Invoke(request) ?? Json("""{"results":[{"elevation":84.2}]}""");
            }
            return RouteFindThing(request, endpointId, "EP")
                ?? RouteEffectiveProperties(request, endpointId, endpoint)
                ?? (kinds is null
                    ? RouteKindsFromProperties(request, endpointId, endpoint, budgets: budgets)
                    : RouteKindsAndBudgets(request, endpointId, budgets, kinds))
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        await using var host = factory.WithWebHostBuilder(builder =>
        {
            if (limitSeconds is not null) builder.UseSetting("PassedOnRequestLimitSeconds", limitSeconds);
            builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(clock));
        });
        using var client = host.CreateClient();

        var answers = new List<Answer>();
        for (var call = 0; call < calls; call++)
        {
            var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });
            answers.Add(new Answer(response.StatusCode, response.Headers.RetryAfter?.ToString(), await response.Content.ReadAsStringAsync()));
        }
        return new Handled(answers, asked, clock.Waits);
    }

    private static Budget OneIn(string seconds) => new(Guid.NewGuid(), "OpenTopoDataBudget", "1", seconds);

    [Fact]
    public async Task A_second_call_inside_the_budgets_window_waits_for_its_slot_inside_the_time_left()
    {
        var handled = await HandleAsync(calls: 2, [OneIn("1")]);

        handled.Answers.Select(answer => answer.Status).Should().Equal(HttpStatusCode.OK, HttpStatusCode.OK);
        handled.Asked.Should().Be(2);
        handled.Waits.Should().Equal(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task A_call_whose_slot_falls_past_the_time_left_is_answered_429_with_the_wait_and_asks_nothing()
    {
        var handled = await HandleAsync(calls: 2, [OneIn("86400")]);

        handled.Answers[1].Status.Should().Be(HttpStatusCode.TooManyRequests);
        handled.Answers[1].RetryAfter.Should().Be("86400");
        handled.Answers[1].Body.Should().Contain("OpenTopoDataBudget");
        handled.Asked.Should().Be(1);
    }

    [Fact]
    public async Task With_no_deadline_a_call_waits_for_a_slot_inside_the_endpoints_timeout()
    {
        var handled = await HandleAsync(calls: 2, [OneIn("20")], limitSeconds: null);

        handled.Answers.Select(answer => answer.Status).Should().Equal(HttpStatusCode.OK, HttpStatusCode.OK);
        handled.Waits.Should().Equal(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task With_no_deadline_a_call_whose_slot_falls_past_the_endpoints_timeout_is_answered_429()
    {
        var handled = await HandleAsync(calls: 2, [OneIn("40")], limitSeconds: null);

        handled.Answers[1].Status.Should().Be(HttpStatusCode.TooManyRequests);
        handled.Answers[1].RetryAfter.Should().Be("40");
        handled.Asked.Should().Be(1);
    }

    [Fact]
    public async Task A_cached_answer_takes_no_slot()
    {
        var handled = await HandleAsync(calls: 2, [OneIn("86400")], CachedElevationEndpoint,
            kinds: [new Kind(EndpointKindRoles.Caching, "DiskCache", "cacheTtl")]);

        handled.Answers.Select(answer => answer.Status).Should().Equal(HttpStatusCode.OK, HttpStatusCode.OK);
        handled.Asked.Should().Be(1);
    }

    [Fact]
    public async Task Each_page_of_a_paged_walk_takes_a_slot()
    {
        var handled = await HandleAsync(calls: 1, [OneIn("86400")], PagedEndpoint,
            provider: request => request.RequestUri!.Query.Contains("resultOffset=2")
                ? Json("""{"features":[{"id":3}],"exceededTransferLimit":false}""")
                : Json("""{"features":[{"id":1},{"id":2}],"exceededTransferLimit":true}"""));

        handled.Answers[0].Status.Should().Be(HttpStatusCode.TooManyRequests);
        handled.Answers[0].RetryAfter.Should().Be("86400");
        handled.Asked.Should().Be(1);
    }

    [Fact]
    public async Task A_budget_that_cannot_be_read_refuses_the_call_with_502_and_asks_nothing()
    {
        var handled = await HandleAsync(calls: 1, [new Budget(Guid.NewGuid(), "OpenTopoDataBudget", "0", "1")]);

        handled.Answers[0].Status.Should().Be(HttpStatusCode.BadGateway);
        handled.Answers[0].Body.Should().Contain("OpenTopoDataBudget");
        handled.Asked.Should().Be(0);
    }
}
