using FluentAssertions;
using vos.Service.Shared.Dispatch;
using Xunit;

namespace vos.Service.Shared.Tests.Dispatch;

public class IdempotentExecutionTests
{
    private const string Delivered = "b0a1f6d2-9c4e-4a55-8d3b-1e7c02f4a918";
    private const string Another = "5c9d7e10-3b62-4f8a-9a41-7d05c8e2b634";

    [Fact]
    public async Task ARepeatDeliveryOfTheSameRelationship_DoesNotRunTheEffectAgain()
    {
        var execution = new IdempotentExecution();
        var runs = 0;

        var first = await execution.RunOnceAsync(Delivered, () => { runs++; return Task.CompletedTask; });
        var second = await execution.RunOnceAsync(Delivered, () => { runs++; return Task.CompletedTask; });

        first.Should().BeTrue();
        second.Should().BeFalse();
        runs.Should().Be(1);
    }

    [Fact]
    public async Task ARelationshipThatRan_StaysClaimed()
    {
        var execution = new IdempotentExecution();

        await execution.RunOnceAsync(Delivered, () => Task.CompletedTask);

        execution.IsClaimed(Delivered).Should().BeTrue();
    }

    [Fact]
    public async Task EachRelationship_IsClaimedOnItsOwn()
    {
        var execution = new IdempotentExecution();
        var runs = 0;

        await execution.RunOnceAsync(Delivered, () => { runs++; return Task.CompletedTask; });
        var other = await execution.RunOnceAsync(Another, () => { runs++; return Task.CompletedTask; });

        other.Should().BeTrue();
        runs.Should().Be(2);
    }

    [Fact]
    public async Task AnEffectThatThrows_ReleasesTheClaim_AndTheFailureReachesTheCaller()
    {
        var execution = new IdempotentExecution();
        var refused = new InvalidOperationException("the write was refused");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => execution.RunOnceAsync(Delivered, () => throw refused));

        thrown.Should().BeSameAs(refused);
        execution.IsClaimed(Delivered).Should().BeFalse();
    }

    [Fact]
    public async Task AfterAFailure_TheNextDeliveryRunsTheEffect()
    {
        var execution = new IdempotentExecution();
        var runs = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => execution.RunOnceAsync(Delivered, () => throw new InvalidOperationException("the write was refused")));

        var redelivered = await execution.RunOnceAsync(Delivered, () => { runs++; return Task.CompletedTask; });

        redelivered.Should().BeTrue();
        runs.Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentDeliveriesOfOneRelationship_RunTheEffectExactlyOnce()
    {
        var execution = new IdempotentExecution();
        var runs = 0;
        var released = new TaskCompletionSource();

        var deliveries = Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
        {
            await released.Task;
            return await execution.RunOnceAsync(Delivered, () =>
            {
                Interlocked.Increment(ref runs);
                return Task.CompletedTask;
            });
        })).ToArray();

        released.SetResult();
        var outcomes = await Task.WhenAll(deliveries);

        outcomes.Count(ran => ran).Should().Be(1);
        runs.Should().Be(1);
    }

    [Fact]
    public void AClaimTakenOnce_IsRefusedTheSecondTime()
    {
        var execution = new IdempotentExecution();

        execution.TryClaim(Delivered).Should().BeTrue();
        execution.TryClaim(Delivered).Should().BeFalse();
        execution.IsClaimed(Delivered).Should().BeTrue();

        execution.ReleaseClaim(Delivered);

        execution.IsClaimed(Delivered).Should().BeFalse();
        execution.TryClaim(Delivered).Should().BeTrue();
    }

    [Fact]
    public async Task AClaimInsideTheRetentionWindow_StillTurnsAwayARepeat()
    {
        var clock = new StubClock();
        var execution = new IdempotentExecution(TimeSpan.FromHours(1), clock.Read);
        var runs = 0;

        await execution.RunOnceAsync(Delivered, () => { runs++; return Task.CompletedTask; });
        clock.Advance(TimeSpan.FromMinutes(59));
        var repeat = await execution.RunOnceAsync(Delivered, () => { runs++; return Task.CompletedTask; });

        repeat.Should().BeFalse();
        runs.Should().Be(1);
    }

    [Fact]
    public async Task AClaimOlderThanTheRetentionWindow_NoLongerTurnsAwayADelivery()
    {
        var clock = new StubClock();
        var execution = new IdempotentExecution(TimeSpan.FromHours(1), clock.Read);
        var runs = 0;

        await execution.RunOnceAsync(Delivered, () => { runs++; return Task.CompletedTask; });
        clock.Advance(TimeSpan.FromHours(1));

        execution.IsClaimed(Delivered).Should().BeFalse();
        var later = await execution.RunOnceAsync(Delivered, () => { runs++; return Task.CompletedTask; });

        later.Should().BeTrue();
        runs.Should().Be(2);
    }

    [Fact]
    public async Task LapsedClaims_AreDroppedRatherThanHeldForever()
    {
        var clock = new StubClock();
        var execution = new IdempotentExecution(TimeSpan.FromHours(1), clock.Read);

        for (var delivery = 0; delivery < 500; delivery++)
            await execution.RunOnceAsync($"relationship-{delivery}", () => Task.CompletedTask);

        execution.HeldClaims.Should().Be(500);

        clock.Advance(TimeSpan.FromHours(2));
        await execution.RunOnceAsync(Delivered, () => Task.CompletedTask);

        execution.HeldClaims.Should().Be(1);
    }

    [Fact]
    public void TheRetentionWindow_DefaultsToTheSharedValue()
    {
        new IdempotentExecution().Retention.Should().Be(IdempotentExecution.DefaultRetention);
    }

    private sealed class StubClock
    {
        private DateTimeOffset _now = new(2026, 8, 30, 9, 0, 0, TimeSpan.Zero);

        public DateTimeOffset Read() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
