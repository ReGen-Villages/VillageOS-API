using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Shared.Hosting;
using vos.Service.Shared.Subscriptions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Shared.Tests.Hosting;

// The broker launches a service with a token that lasts a day, and a service runs for longer. Every call
// the service makes outside a broker request presents that token, so it has to be swapped before it runs out.
public class LaunchTokenRenewalTests
{
    private const string MyceliumUrl = "http://localhost:7243";
    private static readonly Guid Model = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset LaunchTokenExpiry = new(2026, 11, 8, 19, 5, 54, TimeSpan.Zero);

    private sealed class RecordingExchange : IServiceTokenExchange
    {
        private readonly Func<ModelScopedBearer?> _answer;
        public RecordingExchange(Func<ModelScopedBearer?> answer) => _answer = answer;

        public List<string> PresentedBearers { get; } = new();

        public Task<ModelScopedBearer?> ExchangeAsync(string bearer, CancellationToken cancellationToken = default)
        {
            lock (PresentedBearers) PresentedBearers.Add(bearer);
            return Task.FromResult(_answer());
        }
    }

    private static ModelScopedBearer Issued(DateTimeOffset expiresAt) =>
        ModelScopedBearer.Read(TestTokens.For(Model, expiresAt))!;

    private static LaunchTokenRenewal Renewal(
        LaunchToken launchToken, IServiceTokenExchange exchange, TimeSpan? checkInterval = null) =>
        new(launchToken, exchange, NullLogger.Instance, checkInterval ?? TimeSpan.FromMinutes(15));

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Lines { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Lines) Lines.Add((logLevel, formatter(state, exception)));
        }
    }

    // The broker mints this for a key: five minutes long, and refused by the exchange.
    private static string ApiKeyToken(DateTimeOffset expiresAt) => TestTokens.Jwt(new Dictionary<string, object>
    {
        ["vos:model_id"] = Model.ToString(),
        ["vos:token_type"] = "api_key",
        ["exp"] = expiresAt.ToUnixTimeSeconds(),
    });

    private static (LaunchTokenRenewal Renewal, RecordingExchange Exchange, RecordingLogger Logger) Started(string launched)
    {
        var exchange = new RecordingExchange(() => Issued(DateTimeOffset.UtcNow.AddHours(24)));
        var logger = new RecordingLogger();
        return (new LaunchTokenRenewal(new LaunchToken(launched), exchange, logger, TimeSpan.FromMilliseconds(10)), exchange, logger);
    }

    [Fact]
    public async Task A_token_minted_for_an_API_key_is_never_offered_for_a_swap_and_its_lapse_is_never_called_a_refusal()
    {
        var (renewal, exchange, logger) = Started(ApiKeyToken(DateTimeOffset.UtcNow.AddSeconds(-1)));

        await renewal.StartAsync(CancellationToken.None);
        await Settle.ForAsync(renewal.ExecuteTask!, "the renewal stops at once for a token it cannot swap");

        exchange.PresentedBearers.Should().BeEmpty();
        logger.Lines.Should().ContainSingle()
            .Which.Should().Be((LogLevel.Information,
                "The token this service was launched with is not a service token, so the broker cannot swap it and it is not renewed"));
    }

    [Fact]
    public async Task A_service_token_is_swapped_without_a_warning()
    {
        var (renewal, exchange, logger) = Started(TestTokens.For(Model, DateTimeOffset.UtcNow.AddMinutes(30)));

        await renewal.StartAsync(CancellationToken.None);
        try
        {
            await Settle.UntilAsync(() => exchange.PresentedBearers.Count > 0, "the renewal offers a service token for a swap");
        }
        finally
        {
            await renewal.StopAsync(CancellationToken.None);
        }

        logger.Lines.Should().NotContain(line => line.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task A_launch_token_with_two_hours_or_less_left_is_swapped_and_the_new_one_becomes_current()
    {
        var launched = TestTokens.For(Model, LaunchTokenExpiry);
        var replacement = Issued(LaunchTokenExpiry.AddHours(22));
        var exchange = new RecordingExchange(() => replacement);
        var launchToken = new LaunchToken(launched);

        await Renewal(launchToken, exchange).ReplaceIfDueAsync(LaunchTokenExpiry.AddHours(-2));

        exchange.PresentedBearers.Should().Equal(launched);
        launchToken.Current.Should().Be(replacement.Token);
    }

    [Fact]
    public async Task A_launch_token_with_more_than_two_hours_left_is_kept()
    {
        var launched = TestTokens.For(Model, LaunchTokenExpiry);
        var exchange = new RecordingExchange(() => Issued(LaunchTokenExpiry.AddHours(22)));
        var launchToken = new LaunchToken(launched);

        var renewable = await Renewal(launchToken, exchange).ReplaceIfDueAsync(LaunchTokenExpiry.AddHours(-3));

        renewable.Should().BeTrue();
        exchange.PresentedBearers.Should().BeEmpty();
        launchToken.Current.Should().Be(launched);
    }

    [Fact]
    public async Task A_refused_swap_keeps_the_launch_token_and_the_next_check_asks_again()
    {
        var launched = TestTokens.For(Model, LaunchTokenExpiry);
        var exchange = new RecordingExchange(() => null);
        var launchToken = new LaunchToken(launched);
        var renewal = Renewal(launchToken, exchange);

        var renewableAfterFirst = await renewal.ReplaceIfDueAsync(LaunchTokenExpiry.AddHours(-1));
        await renewal.ReplaceIfDueAsync(LaunchTokenExpiry.AddMinutes(-45));

        renewableAfterFirst.Should().BeTrue();
        exchange.PresentedBearers.Should().Equal(launched, launched);
        launchToken.Current.Should().Be(launched);
    }

    [Fact]
    public async Task An_expired_launch_token_is_not_offered_for_a_swap_the_broker_would_refuse()
    {
        var exchange = new RecordingExchange(() => Issued(LaunchTokenExpiry.AddHours(22)));
        var launchToken = new LaunchToken(TestTokens.For(Model, LaunchTokenExpiry));

        var renewable = await Renewal(launchToken, exchange).ReplaceIfDueAsync(LaunchTokenExpiry.AddSeconds(1));

        renewable.Should().BeFalse();
        exchange.PresentedBearers.Should().BeEmpty();
    }

    [Fact]
    public Task A_token_that_cannot_be_read_is_held_as_given() => HeldAsGiven("not-a-token");

    [Fact]
    public Task A_token_naming_no_model_is_held_as_given() =>
        HeldAsGiven(TestTokens.Jwt(new { exp = LaunchTokenExpiry.ToUnixTimeSeconds() }));

    private static async Task HeldAsGiven(string held)
    {
        var exchange = new RecordingExchange(() => Issued(LaunchTokenExpiry.AddHours(22)));
        var launchToken = new LaunchToken(held);

        var renewable = await Renewal(launchToken, exchange).ReplaceIfDueAsync(LaunchTokenExpiry.AddHours(-1));

        renewable.Should().BeFalse();
        exchange.PresentedBearers.Should().BeEmpty();
        launchToken.Current.Should().Be(held);
    }

    [Fact]
    public async Task Once_started_the_renewal_swaps_a_token_that_falls_due_without_being_asked()
    {
        var launched = TestTokens.For(Model, DateTimeOffset.UtcNow.AddHours(1));
        var replacement = Issued(DateTimeOffset.UtcNow.AddHours(24));
        var exchange = new RecordingExchange(() => replacement);
        var launchToken = new LaunchToken(launched);
        var renewal = Renewal(launchToken, exchange, checkInterval: TimeSpan.FromMilliseconds(10));

        await renewal.StartAsync(CancellationToken.None);
        try
        {
            await Settle.UntilAsync(
                () => launchToken.Current == replacement.Token, "the renewal replaces the launch token by itself");
        }
        finally
        {
            await renewal.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void A_service_launched_with_a_token_holds_it_and_runs_its_renewal()
    {
        var services = new ServiceCollection().AddLogging().AddHttpClient();
        var launched = TestTokens.For(Model);

        var launchToken = services.AddLaunchToken(launched, MyceliumUrl);

        launchToken!.Current.Should().Be(launched);
        using var provider = services.BuildServiceProvider();
        provider.GetServices<IHostedService>().Should().ContainSingle()
            .Which.Should().BeOfType<LaunchTokenRenewal>()
            .Which.HoldsAServiceToken().Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_service_launched_without_a_token_holds_none_and_runs_no_renewal(string? token)
    {
        var services = new ServiceCollection().AddLogging().AddHttpClient();

        var launchToken = services.AddLaunchToken(token, MyceliumUrl);

        launchToken.Should().BeNull();
        using var provider = services.BuildServiceProvider();
        provider.GetServices<IHostedService>().Should().BeEmpty();
    }
}
