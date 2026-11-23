using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Shared.Subscriptions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Shared.Tests;

public class HeldModelTokenTests
{
    private static readonly Guid Model = Guid.NewGuid();

    private sealed class ScriptedExchange : IServiceTokenExchange
    {
        private readonly Queue<string?> _answers;

        public ScriptedExchange(params string?[] answers) => _answers = new Queue<string?>(answers);

        public List<string> Presented { get; } = new();

        public Task<ModelScopedBearer?> ExchangeAsync(string bearer, CancellationToken cancellationToken = default)
        {
            Presented.Add(bearer);
            return Task.FromResult(ModelScopedBearer.Read(_answers.Dequeue()));
        }
    }

    private static string TokenExpiringIn(TimeSpan lifetime) =>
        TestTokens.For(Model, DateTimeOffset.UtcNow.Add(lifetime), scope: $"endpoint:test:{Guid.NewGuid():N}");

    private static async Task<HeldModelToken?> SwapDuringACallCarrying(string? callToken, IServiceTokenExchange exchange)
    {
        if (callToken is null)
            return await HeldModelToken.SwapFromTheCallAsync(exchange, NullLogger.Instance);

        HeldModelToken? held = null;
        await MyceliumModelToken.ActingForAsync(callToken, async () =>
            held = await HeldModelToken.SwapFromTheCallAsync(exchange, NullLogger.Instance));
        return held;
    }

    private static async Task<string?> TokenSeenBy(HeldModelToken held)
    {
        string? seen = null;
        await held.ActAsync(() =>
        {
            seen = MyceliumModelToken.Current;
            return Task.CompletedTask;
        });
        return seen;
    }

    [Fact]
    public async Task WorkActsUnderTheTokenSwappedForTheCalls()
    {
        var callToken = TokenExpiringIn(TimeSpan.FromMinutes(5));
        var swapped = TokenExpiringIn(TimeSpan.FromHours(24));
        var exchange = new ScriptedExchange(swapped);

        var held = await SwapDuringACallCarrying(callToken, exchange);

        exchange.Presented.Should().Equal(callToken);
        (await TokenSeenBy(held!)).Should().Be(swapped);
    }

    [Fact]
    public async Task ACallWhoseTokenMyceliumWillNotSwap_HoldsNothing()
    {
        var held = await SwapDuringACallCarrying(TokenExpiringIn(TimeSpan.FromMinutes(5)), new ScriptedExchange([null]));

        held.Should().BeNull();
    }

    [Fact]
    public async Task ACallCarryingNoToken_AsksForNoSwap_AndItsWorkPresentsTheServicesOwnCredential()
    {
        var exchange = new ScriptedExchange();

        var held = await SwapDuringACallCarrying(null, exchange);

        held.Should().BeSameAs(HeldModelToken.None);
        exchange.Presented.Should().BeEmpty();
        (await TokenSeenBy(held!)).Should().BeNull();
    }

    // A simulation runs for as long as its relationship says, far past the day a swapped token lasts.
    [Fact]
    public async Task AHeldTokenCloseToExpiring_IsReplacedBeforeTheWorkActs()
    {
        var expiringSoon = TokenExpiringIn(TimeSpan.FromMinutes(30));
        var replacement = TokenExpiringIn(TimeSpan.FromHours(24));
        var exchange = new ScriptedExchange(expiringSoon, replacement);
        var held = await SwapDuringACallCarrying(TokenExpiringIn(TimeSpan.FromMinutes(5)), exchange);

        var seen = await TokenSeenBy(held!);

        exchange.Presented.Last().Should().Be(expiringSoon);
        seen.Should().Be(replacement);
    }

    [Fact]
    public async Task AHeldTokenWithTimeToRun_IsNotReplaced()
    {
        var swapped = TokenExpiringIn(TimeSpan.FromHours(24));
        var exchange = new ScriptedExchange(swapped);
        var held = await SwapDuringACallCarrying(TokenExpiringIn(TimeSpan.FromMinutes(5)), exchange);

        await TokenSeenBy(held!);
        await TokenSeenBy(held!);

        exchange.Presented.Should().HaveCount(1);
    }

    private static async Task<HeldModelToken> HeldAsync() =>
        (await SwapDuringACallCarrying(TokenExpiringIn(TimeSpan.FromMinutes(5)), new ScriptedExchange(TokenExpiringIn(TimeSpan.FromHours(24)))))!;

    [Fact]
    public async Task Bug8737_WorkActingUnderAHeldToken_CanReadWhichOneItIs_AndOutsideItThereIsNone()
    {
        var held = await HeldAsync();
        HeldModelToken? during = null;

        await held.ActAsync(async () =>
        {
            await Task.Yield();
            during = HeldModelToken.Current;
        });

        during.Should().BeSameAs(held);
        HeldModelToken.Current.Should().BeSameAs(HeldModelToken.None);
    }

    [Fact]
    public async Task Bug8737_WorkActingUnderASecondHeldToken_LeavesTheFirstInForceOnceItEnds()
    {
        var (outer, inner) = (await HeldAsync(), await HeldAsync());
        HeldModelToken? during = null, after = null;

        await outer.ActAsync(async () =>
        {
            await inner.ActAsync(() =>
            {
                during = HeldModelToken.Current;
                return Task.CompletedTask;
            });
            after = HeldModelToken.Current;
        });

        during.Should().BeSameAs(inner);
        after.Should().BeSameAs(outer);
    }

    [Fact]
    public async Task Bug8737_WorkActingUnderNoHeldTokenInsideAnothers_GoesOnUnderTheOuterOne()
    {
        var outer = await HeldAsync();
        HeldModelToken? during = null;
        string? outerToken = null, presented = null;

        await outer.ActAsync(async () =>
        {
            outerToken = MyceliumModelToken.Current;
            await HeldModelToken.None.ActAsync(() =>
            {
                (during, presented) = (HeldModelToken.Current, MyceliumModelToken.Current);
                return Task.CompletedTask;
            });
        });

        during.Should().BeSameAs(outer);
        presented.Should().Be(outerToken);
    }

    [Fact]
    public async Task AReplacementMyceliumRefuses_LeavesTheWorkActingUnderTheTokenItHas()
    {
        var expiringSoon = TokenExpiringIn(TimeSpan.FromMinutes(30));
        var exchange = new ScriptedExchange(expiringSoon, null);
        var held = await SwapDuringACallCarrying(TokenExpiringIn(TimeSpan.FromMinutes(5)), exchange);

        (await TokenSeenBy(held!)).Should().Be(expiringSoon);
    }
}
