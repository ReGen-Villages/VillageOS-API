using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Shared;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Phloem.Tests;

public class WaitDeadlinesTests
{
    private static readonly DateTimeOffset Noon = new(2030, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Only_the_waits_whose_time_has_come_are_taken_earliest_first_and_the_rest_stay()
    {
        var deadlines = new WaitDeadlines();
        var (late, early, onTime) = ((Guid.NewGuid(), Guid.NewGuid()), (Guid.NewGuid(), Guid.NewGuid()), (Guid.NewGuid(), Guid.NewGuid()));
        deadlines.Hold(late.Item1, late.Item2, Noon.AddMinutes(30));
        deadlines.Hold(early.Item1, early.Item2, Noon.AddMinutes(5));
        deadlines.Hold(onTime.Item1, onTime.Item2, Noon.AddMinutes(10));

        deadlines.TakeDue(Noon.AddMinutes(10)).Should().Equal(early, onTime);

        deadlines.Count.Should().Be(1);
        deadlines.TakeDue(Noon.AddMinutes(29)).Should().BeEmpty();
    }

    [Fact]
    public void A_wait_held_again_keeps_only_its_newer_deadline()
    {
        var deadlines = new WaitDeadlines();
        var (runId, nodeId) = (Guid.NewGuid(), Guid.NewGuid());
        deadlines.Hold(runId, nodeId, Noon.AddMinutes(5));

        deadlines.Hold(runId, nodeId, Noon.AddMinutes(50));

        deadlines.Count.Should().Be(1);
        deadlines.TakeDue(Noon.AddMinutes(10)).Should().BeEmpty();
    }

    [Fact]
    public void A_released_wait_is_never_taken()
    {
        var deadlines = new WaitDeadlines();
        var (runId, nodeId) = (Guid.NewGuid(), Guid.NewGuid());
        deadlines.Hold(runId, nodeId, Noon);

        deadlines.Release(runId, nodeId);
        deadlines.Release(runId, nodeId);

        deadlines.Count.Should().Be(0);
        deadlines.TakeDue(Noon.AddDays(1)).Should().BeEmpty();
    }

    [Fact]
    public async Task The_keeper_ends_a_wait_within_a_real_second_of_its_time_passing()
    {
        var box = TestGraphs.WaitBoxPipeline(reportsAFailure: true, waitsAtMostMinutes: 1L);
        var gateway = new FakeGateway(box.Fixture.Build())
        {
            OnCall = (_, envelope) => new NodeCallResult(200, JsonSerializer.Serialize(
                new { result = "success", outputs = new { @out = envelope.GetProperty("inputs").GetProperty("in") } })),
        };
        var real = new FakeTimeProvider(new DateTimeOffset(2026, 11, 17, 21, 0, 0, TimeSpan.Zero));
        var clock = new ModelClock(real);
        clock.AnchorTo(Noon, 1);
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, clock);
        var run = await executor.RunAsync(box.PipelineId, default, CancellationToken.None, subject: new RunSubject(Guid.NewGuid(), "Submission 12"));
        using var keeper = new WaitTimeKeeper(executor, NullLogger<WaitTimeKeeper>.Instance, real);
        await keeper.StartAsync(CancellationToken.None);

        real.Advance(TimeSpan.FromMinutes(1));
        // The keeper may not have set its next one-second delay yet, so each check moves the real clock on a second.
        await Settle.UntilAsync(() =>
        {
            real.Advance(TimeSpan.FromSeconds(1));
            return gateway.Records[(run.RunId, box.Wait.Id)].Status == RunStatus.Failed;
        }, "the keeper ends the wait");

        await keeper.StopAsync(CancellationToken.None);
    }
}
