using vos.Service.Xylem.Services;
using vos.Tests.Shared;
using Xunit;
using FluentAssertions;

namespace vos.Service.Xylem.Tests;

public class IngestJobStoreTests
{
    [Fact]
    public async Task A_started_job_is_running_until_its_ingest_answers()
    {
        var store = new IngestJobStore();
        var answer = new TaskCompletionSource<IngestResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        var job = store.Start(_ => answer.Task);

        job.Id.Should().NotBeNullOrWhiteSpace();
        store.Get(job.Id)!.Status.Should().Be(IngestJobStatus.Running);
        answer.SetResult(IngestResult.Ok(5, 1, 3));
        await Settle.UntilAsync(() => store.Get(job.Id)!.Status == IngestJobStatus.Succeeded, "the job succeeds");
        store.Get(job.Id)!.Result!.ThingsCreated.Should().Be(5);
    }

    [Fact]
    public async Task A_job_whose_ingest_fails_is_marked_failed_with_its_reason()
    {
        var store = new IngestJobStore();

        var job = store.Start(_ => Task.FromResult(IngestResult.Failed("boom")));

        await Settle.UntilAsync(() => store.Get(job.Id)!.Status == IngestJobStatus.Failed, "the job fails");
        store.Get(job.Id)!.Result!.Error.Should().Be("boom");
    }

    [Fact]
    public async Task A_job_whose_ingest_throws_is_marked_failed_with_the_message()
    {
        var store = new IngestJobStore();

        var job = store.Start(_ => throw new InvalidOperationException("the tool could not start"));

        await Settle.UntilAsync(() => store.Get(job.Id)!.Status == IngestJobStatus.Failed, "the job fails");
        store.Get(job.Id)!.Result!.Error.Should().Be("the tool could not start");
    }

    [Fact]
    public async Task Stopping_cancels_each_running_job_and_waits_for_it_to_end()
    {
        var store = new IngestJobStore();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ended = false;
        var job = store.Start(async serviceStopping =>
        {
            try
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, serviceStopping);
                return IngestResult.Ok(1, 0, 0);
            }
            finally
            {
                await Task.Yield();
                ended = true;
            }
        });
        await Settle.ForAsync(started.Task, "the ingest started");

        await store.StopAsync(CancellationToken.None);

        ended.Should().BeTrue();
        store.Get(job.Id)!.Result.Should().Be(IngestJobStore.StoppedWithTheService);
    }

    [Fact]
    public async Task A_job_started_once_the_store_is_stopping_is_failed_without_running()
    {
        var store = new IngestJobStore();
        await store.StopAsync(CancellationToken.None);
        var ran = false;

        var job = store.Start(_ =>
        {
            ran = true;
            return Task.FromResult(IngestResult.Ok(1, 0, 0));
        });

        ran.Should().BeFalse();
        store.Get(job.Id)!.Result.Should().Be(IngestJobStore.StoppedWithTheService);
    }

    [Fact]
    public void Get_returns_null_for_an_unknown_job()
    {
        new IngestJobStore().Get("nope").Should().BeNull();
    }
}
