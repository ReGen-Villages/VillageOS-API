using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Shared;
using Xunit;

namespace vos.Service.Phloem.Tests;

public class WaitTimeLimitTests
{
    private static readonly RunSubject Submission = new(Guid.NewGuid(), "Submission 12");
    private static readonly DateTimeOffset Noon = new(2030, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private sealed record Setup(TestGraphs.WaitBoxFixture Box, FakeGateway Gateway, PipelineExecutor Executor, FakeTimeProvider Real, ModelClock Clock);

    private const long NinetyMinutes = 90;

    private static Setup Arrange(object? waitsAtMostMinutes = null, double rate = 1)
    {
        var box = TestGraphs.WaitBoxPipeline(reportsAFailure: true, waitsAtMostMinutes: waitsAtMostMinutes);
        var gateway = new FakeGateway(box.Fixture.Build())
        {
            OnCall = (_, envelope) => new NodeCallResult(200, JsonSerializer.Serialize(
                new { result = "success", outputs = new { @out = envelope.GetProperty("inputs").GetProperty("in") } })),
        };
        var real = new FakeTimeProvider(new DateTimeOffset(2026, 11, 17, 21, 0, 0, TimeSpan.Zero));
        var clock = new ModelClock(real);
        clock.AnchorTo(Noon, rate);
        return new Setup(box, gateway, new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, clock), real, clock);
    }

    private static async Task<Guid> AWaitingRunAsync(Setup setup) =>
        (await setup.Executor.RunAsync(setup.Box.PipelineId, default, CancellationToken.None, subject: Submission)).RunId;

    [Fact]
    public async Task A_wait_whose_state_is_never_entered_fails_once_its_time_has_passed_and_the_run_follows_its_fail_wire()
    {
        var setup = Arrange(NinetyMinutes);
        var runId = await AWaitingRunAsync(setup);

        setup.Gateway.Watches[(runId, setup.Box.Wait.Id)].Until.Should().Be(Noon.AddMinutes(90));
        setup.Real.Advance(TimeSpan.FromMinutes(89));
        (await setup.Executor.ExpireDueWaitsAsync(CancellationToken.None)).Should().Be(0);
        setup.Real.Advance(TimeSpan.FromMinutes(2));
        (await setup.Executor.ExpireDueWaitsAsync(CancellationToken.None)).Should().Be(1);

        var waited = setup.Gateway.Records[(runId, setup.Box.Wait.Id)];
        waited.Status.Should().Be(RunStatus.Failed);
        waited.Error.Should().Be($"'{TestGraphs.WaitBox}' waited 90 model minutes for 'Submission 12' to enter 'decided', and it did not.");
        setup.Gateway.Called.Should().Equal("report");
        setup.Executor.WaitsHeldWithATimeLimit.Should().Be(0);
    }

    [Fact]
    public async Task A_wait_that_fails_writes_why_it_failed_whatever_error_its_record_read_back_with()
    {
        var setup = Arrange(NinetyMinutes);
        var runId = await AWaitingRunAsync(setup);
        var key = (runId, setup.Box.Wait.Id);
        setup.Gateway.Records[key] = setup.Gateway.Records[key] with { Error = "" };

        setup.Real.Advance(TimeSpan.FromMinutes(91));
        await setup.Executor.ExpireDueWaitsAsync(CancellationToken.None);

        setup.Gateway.Records[key].Error.Should().Be($"'{TestGraphs.WaitBox}' waited 90 model minutes for 'Submission 12' to enter 'decided', and it did not.");
    }

    [Fact]
    public async Task A_wait_answered_before_its_time_carries_on_and_nothing_fails_later()
    {
        var setup = Arrange(NinetyMinutes);
        var runId = await AWaitingRunAsync(setup);

        await setup.Executor.ResumeAsync(runId, setup.Box.Wait.Id, CancellationToken.None);
        setup.Real.Advance(TimeSpan.FromHours(2));

        (await setup.Executor.ExpireDueWaitsAsync(CancellationToken.None)).Should().Be(0);
        setup.Gateway.Records[(runId, setup.Box.Wait.Id)].Status.Should().Be(RunStatus.Succeeded);
        setup.Gateway.Called.Should().Equal("publish");
        setup.Executor.WaitsHeldWithATimeLimit.Should().Be(0);
    }

    [Fact]
    public async Task A_wait_with_no_limit_waits_without_one_and_holds_no_deadline()
    {
        var setup = Arrange(waitsAtMostMinutes: null);
        var runId = await AWaitingRunAsync(setup);

        setup.Gateway.Watches[(runId, setup.Box.Wait.Id)].Until.Should().BeNull();
        setup.Executor.WaitsHeldWithATimeLimit.Should().Be(0);
        setup.Real.Advance(TimeSpan.FromDays(30));
        (await setup.Executor.ExpireDueWaitsAsync(CancellationToken.None)).Should().Be(0);
        setup.Gateway.Records[(runId, setup.Box.Wait.Id)].Status.Should().Be(RunStatus.Waiting);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    public async Task A_wait_whose_limit_is_written_empty_waits_without_one(string written)
    {
        var setup = Arrange(waitsAtMostMinutes: JsonDocument.Parse(written).RootElement);

        var runId = await AWaitingRunAsync(setup);

        setup.Gateway.Records[(runId, setup.Box.Wait.Id)].Status.Should().Be(RunStatus.Waiting);
        setup.Gateway.Watches[(runId, setup.Box.Wait.Id)].Until.Should().BeNull();
        setup.Executor.WaitsHeldWithATimeLimit.Should().Be(0);
    }

    [Fact]
    public async Task A_stopped_run_lets_go_of_its_waits_deadline()
    {
        var setup = Arrange(NinetyMinutes);
        var runId = await AWaitingRunAsync(setup);

        await setup.Executor.StopAsync(Guid.NewGuid(), runId, CancellationToken.None);

        setup.Executor.WaitsHeldWithATimeLimit.Should().Be(0);
        setup.Gateway.Records[(runId, setup.Box.Wait.Id)].Status.Should().Be(RunStatus.Cancelled);
    }

    [Fact]
    public async Task A_wait_whose_node_run_is_gone_when_its_time_passes_ends_quietly()
    {
        var setup = Arrange(NinetyMinutes);
        var runId = await AWaitingRunAsync(setup);
        setup.Gateway.Records.Remove((runId, setup.Box.Wait.Id));

        setup.Real.Advance(TimeSpan.FromMinutes(91));

        (await setup.Executor.ExpireDueWaitsAsync(CancellationToken.None)).Should().Be(1);
        setup.Executor.WaitsHeldWithATimeLimit.Should().Be(0);
        setup.Gateway.Called.Should().BeEmpty();
    }

    [Fact]
    public async Task A_restarted_orchestrator_reads_the_deadline_back_and_fails_the_wait_when_it_passes()
    {
        var setup = Arrange(NinetyMinutes);
        var runId = await AWaitingRunAsync(setup);
        var restarted = new PipelineExecutor(setup.Gateway, NullLogger<PipelineExecutor>.Instance, setup.Clock);

        await restarted.RecoverWaitsAsync(CancellationToken.None);
        restarted.WaitsHeldWithATimeLimit.Should().Be(1);
        setup.Real.Advance(TimeSpan.FromMinutes(91));

        (await restarted.ExpireDueWaitsAsync(CancellationToken.None)).Should().Be(1);
        setup.Gateway.Records[(runId, setup.Box.Wait.Id)].Status.Should().Be(RunStatus.Failed);
    }

    // The limit is in model minutes, so a clock that speeds up mid-wait brings the failure sooner in real time.
    [Fact]
    public async Task A_wait_ends_by_model_time_when_the_clock_speeds_up_during_it()
    {
        var setup = Arrange(NinetyMinutes);
        var runId = await AWaitingRunAsync(setup);
        setup.Real.Advance(TimeSpan.FromMinutes(30));
        setup.Clock.AnchorTo(setup.Clock.GetUtcNow(), 60);

        setup.Real.Advance(TimeSpan.FromSeconds(59));
        (await setup.Executor.ExpireDueWaitsAsync(CancellationToken.None)).Should().Be(0);
        setup.Real.Advance(TimeSpan.FromSeconds(2));
        (await setup.Executor.ExpireDueWaitsAsync(CancellationToken.None)).Should().Be(1);
        setup.Gateway.Records[(runId, setup.Box.Wait.Id)].Status.Should().Be(RunStatus.Failed);
    }

    [Theory]
    [InlineData("soon")]
    [InlineData(0L)]
    [InlineData(-5L)]
    public async Task A_box_waiting_at_most_a_time_that_is_not_a_whole_number_of_minutes_above_zero_refuses_the_run(object written)
    {
        var setup = Arrange(waitsAtMostMinutes: written);

        var result = await setup.Executor.RunAsync(setup.Box.PipelineId, default, CancellationToken.None, subject: Submission);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain($"waits at most '{written}' minutes, which is not a whole number of minutes above zero");
        setup.Executor.WaitsHeldWithATimeLimit.Should().Be(0);
    }

    [Fact]
    public async Task A_relate_box_waits_at_most_the_time_it_names()
    {
        var box = TestGraphs.RelateBoxPipeline(waitsAtMostMinutes: 75L);
        var gateway = new FakeGateway(box.Fixture.Build());
        var real = new FakeTimeProvider(new DateTimeOffset(2026, 11, 17, 21, 0, 0, TimeSpan.Zero));
        var clock = new ModelClock(real);
        clock.AnchorTo(Noon, 1);
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, clock);

        var result = await executor.RunAsync(box.PipelineId, default, CancellationToken.None, subject: Submission);

        gateway.Watches[(result.RunId, box.Relate.Id)].Until.Should().Be(Noon.AddMinutes(75));
        executor.WaitsHeldWithATimeLimit.Should().Be(1);
    }
}
