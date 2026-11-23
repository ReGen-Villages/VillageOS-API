using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using vos.Service.Phloem.Execution;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Phloem.Tests;

public class RunPlacesTests
{
    private static readonly Guid Pipeline = Guid.NewGuid();

    private readonly List<Guid> _begun = new();

    private bool BeginsAtOnce(RunPlaces places, Guid runId, int? limit)
    {
        var joining = places.Join(Pipeline, runId, limit, () => _begun.Add(runId));
        Begin(joining.HandedOver);
        return joining.QueueNumber is null;
    }

    private static void Begin(IEnumerable<Action> handedOver)
    {
        foreach (var begin in handedOver) begin();
    }

    private static List<Guid> NewRuns(int howMany) => Enumerable.Range(0, howMany).Select(_ => Guid.NewGuid()).ToList();

    [Fact]
    public void Runs_over_the_limit_are_queued_and_handed_places_in_the_order_they_came()
    {
        var places = new RunPlaces();
        var runs = NewRuns(4);

        runs.Select(run => BeginsAtOnce(places, run, limit: 1)).Should().Equal(true, false, false, false);
        places.Leave(Pipeline, runs[0]).Should().BeTrue("runs are queued for the place it leaves");
        Begin(places.HandOver(Pipeline, limit: 1));
        Begin(places.HandOver(Pipeline, limit: 1));

        _begun.Should().Equal(runs[1]);
        places.Leave(Pipeline, runs[1]);
        Begin(places.HandOver(Pipeline, limit: 2));
        _begun.Should().Equal(runs[1], runs[2], runs[3]);
    }

    [Fact]
    public void A_start_takes_no_place_before_a_run_already_queued()
    {
        var places = new RunPlaces();
        var runs = NewRuns(3);
        BeginsAtOnce(places, runs[0], limit: 1);
        BeginsAtOnce(places, runs[1], limit: 1);
        places.Leave(Pipeline, runs[0]);

        BeginsAtOnce(places, runs[2], limit: 1).Should().BeFalse();

        _begun.Should().Equal(runs[1]);
    }

    [Fact]
    public void Once_the_limit_is_gone_a_start_begins_at_once_and_so_does_every_run_queued()
    {
        var places = new RunPlaces();
        var runs = NewRuns(4);
        foreach (var run in runs.Take(3)) BeginsAtOnce(places, run, limit: 1);

        BeginsAtOnce(places, runs[3], limit: null).Should().BeTrue();

        _begun.Should().Equal(runs[1], runs[2]);
    }

    [Fact]
    public void A_lowered_limit_hands_no_place_over_until_enough_runs_have_left()
    {
        var places = new RunPlaces();
        var runs = NewRuns(3);
        foreach (var run in runs) BeginsAtOnce(places, run, limit: 2);

        places.Leave(Pipeline, runs[0]);
        places.HandOver(Pipeline, limit: 1).Should().BeEmpty();
        places.Leave(Pipeline, runs[1]);
        Begin(places.HandOver(Pipeline, limit: 1));

        _begun.Should().Equal(runs[2]);
    }

    [Fact]
    public void A_run_that_takes_a_place_without_queueing_holds_it_and_may_pass_the_limit()
    {
        var places = new RunPlaces();
        var runs = NewRuns(3);
        places.Take(Pipeline, runs[0], limit: 1);
        places.Take(Pipeline, runs[1], limit: 1);

        BeginsAtOnce(places, runs[2], limit: 1).Should().BeFalse();
        places.Leave(Pipeline, runs[0]);
        places.HandOver(Pipeline, limit: 1).Should().BeEmpty();
        places.Leave(Pipeline, runs[1]);
        places.HandOver(Pipeline, limit: 1).Should().ContainSingle();
    }

    [Fact]
    public void Leaving_twice_frees_one_place()
    {
        var places = new RunPlaces();
        var runs = NewRuns(2);
        foreach (var run in runs) BeginsAtOnce(places, run, limit: 1);

        places.Leave(Pipeline, runs[0]).Should().BeTrue();
        places.Leave(Pipeline, runs[0]).Should().BeFalse();
    }

    [Fact]
    public void Runs_read_back_after_a_restart_begin_in_the_order_of_their_numbers_before_any_run_queued_since()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2030, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var before = new RunPlaces(clock);
        var runs = NewRuns(4);
        before.Join(Pipeline, Guid.NewGuid(), 1, () => { });
        var numbers = runs.Take(2).Select(run => before.Join(Pipeline, run, 1, () => { }).QueueNumber!.Value).ToList();
        var after = new RunPlaces(clock);
        clock.Advance(TimeSpan.FromSeconds(1));
        after.Take(Pipeline, Guid.NewGuid(), 1);

        BeginsAtOnce(after, runs[2], limit: 1);
        after.JoinAgain(Pipeline, runs[1], numbers[1], () => _begun.Add(runs[1]));
        after.JoinAgain(Pipeline, runs[0], numbers[0], () => _begun.Add(runs[0]));
        Begin(after.HandOver(Pipeline, limit: null));

        _begun.Should().Equal(runs[0], runs[1], runs[2]);
    }

    [Fact]
    public void Numbers_keep_rising_on_a_clock_that_stands_still_and_past_one_read_back()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2030, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var places = new RunPlaces(clock);
        var readBack = clock.GetUtcNow().ToUnixTimeMilliseconds() + 5_000;
        places.Join(Pipeline, Guid.NewGuid(), 1, () => { });

        places.JoinAgain(Pipeline, Guid.NewGuid(), readBack, () => { });
        var numbers = Enumerable.Range(0, 3).Select(_ => places.Join(Pipeline, Guid.NewGuid(), 1, () => { }).QueueNumber!.Value).ToList();

        numbers.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems().And.OnlyContain(number => number > readBack);
    }

    [Fact]
    public void A_run_already_queued_or_begun_here_is_not_queued_again()
    {
        var places = new RunPlaces();
        var begun = Guid.NewGuid();
        var queued = Guid.NewGuid();
        BeginsAtOnce(places, begun, limit: 1);
        var number = places.Join(Pipeline, queued, 1, () => _begun.Add(queued)).QueueNumber!.Value;

        places.JoinAgain(Pipeline, begun, number - 1, () => _begun.Add(begun));
        places.JoinAgain(Pipeline, queued, number, () => _begun.Add(queued));
        Begin(places.HandOver(Pipeline, limit: null));

        _begun.Should().Equal(queued);
    }

    [Fact]
    public void A_pipeline_whose_runs_have_all_left_by_the_time_its_limit_is_read_hands_nothing_over() =>
        new RunPlaces().HandOver(Pipeline, limit: 1).Should().BeEmpty();

    [Fact]
    public void A_pipeline_is_held_only_while_it_has_a_limit_and_a_run_or_runs_are_queued_for_it()
    {
        var places = new RunPlaces();
        var unlimited = Guid.NewGuid();
        var limited = Guid.NewGuid();

        BeginsAtOnce(places, unlimited, limit: null);
        places.Take(Pipeline, unlimited, limit: null);
        places.PipelinesHeld.Should().Be(0);
        BeginsAtOnce(places, limited, limit: 1);
        places.PipelinesHeld.Should().Be(1);
        places.Leave(Pipeline, limited);
        places.PipelinesHeld.Should().Be(0);
    }

    [Fact]
    public async Task Two_starts_arriving_together_never_both_take_the_last_place()
    {
        var places = new RunPlaces();
        using var arriving = new Barrier(2);
        var pipelines = NewRuns(2_000);
        var begunIn = new int[pipelines.Count];
        void StartARunOfEach()
        {
            for (var round = 0; round < pipelines.Count; round++)
            {
                arriving.SignalAndWait(Settle.Ceiling).Should().BeTrue();
                if (places.Join(pipelines[round], Guid.NewGuid(), 1, () => { }).QueueNumber is null)
                    Interlocked.Increment(ref begunIn[round]);
            }
        }

        await Task.WhenAll(
            Task.Factory.StartNew(StartARunOfEach, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default),
            Task.Factory.StartNew(StartARunOfEach, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default));

        begunIn.Should().OnlyContain(begun => begun == 1);
    }
}
