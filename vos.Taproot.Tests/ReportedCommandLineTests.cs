using Xunit;

namespace vos.Taproot.Tests;

public class ReportedCommandLineTests
{
    [Theory]
    [InlineData("list things --type=Reservoir --limit=5")]
    [InlineData("set Reservoir capacity 1200")]
    [InlineData("create property Reservoir capacity int 1200")]
    [InlineData("get thing Monkey Island")]
    public void A_line_carrying_no_secret_is_reported_as_typed(string line)
    {
        Assert.Equal(line, ReportedCommandLine.For(line));
    }

    [Theory]
    [InlineData("ingest --apiKey=abc123 --new", "ingest --apiKey=**** --new")]
    [InlineData("call endpoint intake token=xyz", "call endpoint intake token=****")]
    [InlineData("set Gateway accessToken abc def", "set Gateway accessToken ****")]
    [InlineData("create property Gateway relayPassword string hunter2", "create property Gateway relayPassword string ****")]
    public void A_secret_is_masked(string line, string reported)
    {
        Assert.Equal(reported, ReportedCommandLine.For(line));
    }
}
