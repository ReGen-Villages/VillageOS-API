using vos.ManagedMicroservice.Xylem.Services;
using Xunit;
using FluentAssertions;

namespace vos.ManagedMicroservice.Xylem.Tests;

public class IngestJobStoreTests
{
    [Fact]
    public void Create_starts_a_running_job_that_can_be_fetched()
    {
        var store = new IngestJobStore();
        var job = store.Create();

        job.Status.Should().Be(IngestJobStatus.Running);
        job.Id.Should().NotBeNullOrWhiteSpace();
        store.Get(job.Id)!.Status.Should().Be(IngestJobStatus.Running);
    }

    [Fact]
    public void Complete_with_a_successful_result_marks_the_job_succeeded()
    {
        var store = new IngestJobStore();
        var job = store.Create();

        store.Complete(job.Id, IngestResult.Ok(5, 1, 3));

        var done = store.Get(job.Id)!;
        done.Status.Should().Be(IngestJobStatus.Succeeded);
        done.Result!.ThingsCreated.Should().Be(5);
    }

    [Fact]
    public void Complete_with_a_failed_result_marks_the_job_failed()
    {
        var store = new IngestJobStore();
        var job = store.Create();

        store.Complete(job.Id, IngestResult.Failed("boom"));

        store.Get(job.Id)!.Status.Should().Be(IngestJobStatus.Failed);
        store.Get(job.Id)!.Result!.Error.Should().Be("boom");
    }

    [Fact]
    public void Get_returns_null_for_an_unknown_job()
    {
        new IngestJobStore().Get("nope").Should().BeNull();
    }
}
