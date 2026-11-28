using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using vos.Service.Tributary.Helpers;
using vos.Service.Tributary.Services;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Tributary.Tests;

public sealed class ProviderBudgetLedgerTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan PlentyOfTime = TimeSpan.FromSeconds(28);

    private static readonly ProviderBudget OneASecond = new(Guid.NewGuid(), "OneASecond", 1, TimeSpan.FromSeconds(1));
    private static readonly ProviderBudget TwoADay = new(Guid.NewGuid(), "TwoADay", 2, TimeSpan.FromDays(1));

    private readonly string _folder = Directory.CreateTempSubdirectory("tributary-request-budgets-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private sealed class ClockThatSignalsEachWait(DateTimeOffset start) : FakeTimeProvider(start)
    {
        private readonly TaskCompletionSource<TimeSpan> _waited = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TimeSpan> Waited => _waited.Task;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            _waited.TrySetResult(dueTime);
            return timer;
        }
    }

    private ProviderBudgetLedger Ledger(TimeProvider clock) => new(_folder, clock);

    [Fact]
    public async Task A_call_inside_every_budget_goes_at_once()
    {
        var ledger = Ledger(new FakeTimeProvider(Start));

        var slot = await ledger.TakeAsync([OneASecond, TwoADay], PlentyOfTime, CancellationToken.None);

        slot.Taken.Should().BeTrue();
    }

    [Fact]
    public async Task A_second_call_inside_the_window_waits_until_its_slot_when_the_slot_falls_inside_the_time_left()
    {
        var clock = new ClockThatSignalsEachWait(Start);
        var ledger = Ledger(clock);
        await ledger.TakeAsync([OneASecond], PlentyOfTime, CancellationToken.None);

        var second = ledger.TakeAsync([OneASecond], PlentyOfTime, CancellationToken.None);
        await Settle.ForAsync(clock.Waited, "the second call to wait for its slot");

        (await clock.Waited).Should().Be(TimeSpan.FromSeconds(1));
        second.IsCompleted.Should().BeFalse();
        clock.Advance(TimeSpan.FromSeconds(1));
        await Settle.ForAsync(second, "the second call to take its slot once the second has passed");
        (await second).Taken.Should().BeTrue();
    }

    [Fact]
    public async Task A_call_whose_slot_falls_past_the_time_left_is_answered_with_the_wait_in_whole_seconds_and_is_not_counted()
    {
        var clock = new FakeTimeProvider(Start);
        var ledger = Ledger(clock);
        await ledger.TakeAsync([OneASecond], PlentyOfTime, CancellationToken.None);
        clock.Advance(TimeSpan.FromMilliseconds(250));

        var refused = await ledger.TakeAsync([OneASecond], TimeSpan.FromMilliseconds(500), CancellationToken.None);
        clock.Advance(TimeSpan.FromMilliseconds(750));
        var afterTheWait = await ledger.TakeAsync([OneASecond], TimeSpan.Zero, CancellationToken.None);

        refused.Should().Be(new BudgetSlot(false, TimeSpan.FromSeconds(1)));
        afterTheWait.Taken.Should().BeTrue("the refused call took no slot");
    }

    [Fact]
    public async Task The_call_past_a_days_count_waits_for_the_oldest_call_to_leave_the_window()
    {
        var clock = new FakeTimeProvider(Start);
        var ledger = Ledger(clock);
        await ledger.TakeAsync([TwoADay], PlentyOfTime, CancellationToken.None);
        clock.Advance(TimeSpan.FromHours(1));
        await ledger.TakeAsync([TwoADay], PlentyOfTime, CancellationToken.None);

        var third = await ledger.TakeAsync([TwoADay], PlentyOfTime, CancellationToken.None);

        third.Should().Be(new BudgetSlot(false, TimeSpan.FromHours(23)));
    }

    [Fact]
    public void A_call_with_room_in_its_budget_still_goes_after_a_slot_already_handed_out()
    {
        var threeADay = new ProviderBudget(Guid.NewGuid(), "ThreeADay", 3, TimeSpan.FromDays(1));
        var ledger = Ledger(new FakeTimeProvider(Start));
        ledger.Reserve([OneASecond, threeADay], PlentyOfTime);
        var waiting = ledger.Reserve([OneASecond, threeADay], PlentyOfTime);

        var after = ledger.Reserve([threeADay], PlentyOfTime);

        after.At.Should().Be(waiting.At).And.Be(Start + TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Two_registrations_reaching_one_budget_share_its_count()
    {
        var ledger = Ledger(new FakeTimeProvider(Start));
        var readAgain = OneASecond with { Name = "read through another registration" };
        await ledger.TakeAsync([OneASecond], PlentyOfTime, CancellationToken.None);

        var second = await ledger.TakeAsync([readAgain], TimeSpan.Zero, CancellationToken.None);

        second.Taken.Should().BeFalse();
    }

    [Fact]
    public async Task A_waiter_cancelled_before_its_slot_gives_the_slot_back()
    {
        var clock = new ClockThatSignalsEachWait(Start);
        var ledger = Ledger(clock);
        await ledger.TakeAsync([TwoADay], PlentyOfTime, CancellationToken.None);
        await ledger.TakeAsync([OneASecond], PlentyOfTime, CancellationToken.None);
        using var cancel = new CancellationTokenSource();

        var waiting = ledger.TakeAsync([OneASecond, TwoADay], PlentyOfTime, cancel.Token);
        await Settle.ForAsync(clock.Waited, "the call to wait for its slot");
        await cancel.CancelAsync();
        var cancelled = async () => await waiting;
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        clock.Advance(TimeSpan.FromSeconds(1));

        (await ledger.TakeAsync([TwoADay], TimeSpan.Zero, CancellationToken.None)).Taken
            .Should().BeTrue("the cancelled call gave back its place in the day's count");
    }

    [Fact]
    public async Task A_restart_keeps_a_long_windows_count_and_drops_calls_older_than_the_window()
    {
        var clock = new FakeTimeProvider(Start);
        await Ledger(clock).TakeAsync([TwoADay], PlentyOfTime, CancellationToken.None);
        clock.Advance(TimeSpan.FromHours(23));
        await Ledger(clock).TakeAsync([TwoADay], PlentyOfTime, CancellationToken.None);

        var beforeTheFirstLeaves = await Ledger(clock).TakeAsync([TwoADay], TimeSpan.Zero, CancellationToken.None);
        clock.Advance(TimeSpan.FromHours(1));
        var afterTheFirstLeaves = await Ledger(clock).TakeAsync([TwoADay], TimeSpan.Zero, CancellationToken.None);

        beforeTheFirstLeaves.Taken.Should().BeFalse("a new ledger on the same folder reads the day's two calls");
        afterTheFirstLeaves.Taken.Should().BeTrue("the first call has left the day");
        JsonSerializer.Deserialize<long[]>(File.ReadAllBytes(Path.Combine(_folder, TwoADay.Id + ".json")))
            .Should().HaveCount(2, "a kept window holds only the calls inside it");
    }

    [Fact]
    public async Task A_short_window_is_kept_in_memory_only()
    {
        await Ledger(new FakeTimeProvider(Start)).TakeAsync([OneASecond], PlentyOfTime, CancellationToken.None);

        Directory.EnumerateFileSystemEntries(_folder).Should().BeEmpty();
    }

    [Fact]
    public async Task A_copy_written_after_a_later_one_does_not_replace_it_on_disk()
    {
        var clock = new FakeTimeProvider(Start);
        var ledger = Ledger(clock);
        var earlier = ledger.Reserve([TwoADay], PlentyOfTime);
        var later = ledger.Reserve([TwoADay], PlentyOfTime);

        ledger.Keep(later);
        ledger.Keep(earlier);

        (await Ledger(clock).TakeAsync([TwoADay], TimeSpan.Zero, CancellationToken.None)).Taken
            .Should().BeFalse("the file holds both of the day's calls, not only the first");
    }
}
