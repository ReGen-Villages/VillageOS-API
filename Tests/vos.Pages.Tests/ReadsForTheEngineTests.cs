using Xunit;

namespace vos.Pages.Tests;

public class ReadsForTheEngineTests
{
    [Fact]
    public async Task A_path_the_reader_asks_for_is_passed_on_as_asked_and_its_answer_passed_back()
    {
        var reads = new ReadsAnsweredFromMemory { ReservoirsRunningLow = 3 };
        var host = new ReadsForTheEngine(reads, CancellationToken.None);

        var answer = await host.get("/api/states/RunningLow/things?countOnly=true");

        Assert.Equal("""{"StateName":"RunningLow","Count":3}""", answer);
        Assert.Equal("/api/states/RunningLow/things?countOnly=true", Assert.Single(reads.Got));
    }

    [Fact]
    public async Task A_body_the_reader_posts_is_passed_on_with_its_path_and_a_refusal_comes_back_as_one()
    {
        var reads = new ReadsAnsweredFromMemory();
        var host = new ReadsForTheEngine(reads, CancellationToken.None);

        await Assert.ThrowsAsync<HttpRequestException>(() => host.post("/api/temporal/reduce", """{"property":"levelMetres"}"""));

        Assert.Equal(("/api/temporal/reduce", """{"property":"levelMetres"}"""), Assert.Single(reads.Posted));
    }
}
