using FluentAssertions;
using vos.Service.Phloem.Execution;
using Xunit;

namespace vos.Service.Phloem.Tests;

public class RunTurnsTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task A_second_turn_on_one_run_waits_for_the_first_and_is_handed_it_when_the_first_leaves()
    {
        var turns = new RunTurns();
        var runId = Guid.NewGuid();
        var first = await turns.TakeAsync(runId, CancellationToken.None);
        var second = turns.TakeAsync(runId, CancellationToken.None);

        second.IsCompleted.Should().BeFalse();
        first.Dispose();

        (await second.WaitAsync(Patience)).Dispose();
    }

    [Fact]
    public async Task A_turn_given_up_while_waiting_leaves_the_run_free_for_the_next()
    {
        var turns = new RunTurns();
        var runId = Guid.NewGuid();
        using var givingUp = new CancellationTokenSource();
        var first = await turns.TakeAsync(runId, CancellationToken.None);
        var givenUp = turns.TakeAsync(runId, givingUp.Token);

        givingUp.Cancel();
        await FluentActions.Awaiting(() => givenUp).Should().ThrowAsync<OperationCanceledException>();
        first.Dispose();

        (await turns.TakeAsync(runId, CancellationToken.None).WaitAsync(Patience)).Dispose();
    }
}
