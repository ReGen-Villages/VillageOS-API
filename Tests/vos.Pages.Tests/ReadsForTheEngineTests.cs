using Xunit;

namespace vos.Pages.Tests;

public class ReadsForTheEngineTests
{
    [Fact]
    public async Task Ranges_that_could_not_be_read_reach_the_reader_as_no_answer()
    {
        var host = new ReadsForTheEngine(new ReadsAnsweredFromMemory(), CancellationToken.None);

        var answer = await host.read("thingRanges", """["00000000-0000-4000-8000-000000000201"]""");

        Assert.Equal("null", answer);
    }

    [Fact]
    public async Task A_narrowing_the_reader_left_out_is_passed_on_as_none()
    {
        var reads = new ReadsAnsweredFromMemory();
        var host = new ReadsForTheEngine(reads, CancellationToken.None);

        await host.read("thingsInState", """["RunningLow", null]""");

        Assert.Equal(("RunningLow", null), Assert.Single(reads.StatesAsked));
    }

    [Theory]
    [InlineData("aggregate", """[{"buckets":24}]""", """{"asked":"aggregate","query":{"buckets":24}}""")]
    [InlineData("reduce", """[{"property":"levelMetres"}]""", """{"asked":"reduce","query":{"property":"levelMetres"}}""")]
    [InlineData("fromService", """["/api/endpoints/inflow",{"reservoir":"Spring reservoir"}]""", """{"asked":"/api/endpoints/inflow","body":{"reservoir":"Spring reservoir"}}""")]
    [InlineData("fromService", """["/api/endpoints/inflow",null]""", """{"asked":"/api/endpoints/inflow","body":null}""")]
    public async Task A_question_is_passed_on_as_the_reader_asked_it_and_its_answer_passed_back(string read, string argumentsJson, string answered)
    {
        var host = new ReadsForTheEngine(new ReadsAnsweredFromMemory(), CancellationToken.None);

        Assert.Equal(answered, await host.read(read, argumentsJson));
    }

    [Fact]
    public async Task A_read_this_library_does_not_know_is_refused_by_its_name()
    {
        var host = new ReadsForTheEngine(new ReadsAnsweredFromMemory(), CancellationToken.None);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => host.read("everyThing", "[]"));

        Assert.Contains("everyThing", refused.Message);
    }
}
