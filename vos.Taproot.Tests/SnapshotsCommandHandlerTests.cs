using System.Text.Json;
using Moq;
using Xunit;

namespace vos.Taproot.Tests;

public class SnapshotsCommandHandlerTests
{
    private readonly Mock<MyceliumClient> _myceliumMock;
    private readonly StringWriter _writer;

    public SnapshotsCommandHandlerTests()
    {
        _myceliumMock = new Mock<MyceliumClient>("https://localhost:7243") { CallBase = false };
        _writer = new StringWriter();
    }

    private async Task ExecuteHandler(string arg)
    {
        var handler = new SnapshotsCommandHandler(arg, _writer, _myceliumMock.Object);
        await handler.ExecuteAsync();
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private void MyceliumReports(long resolutions, long retries, long lockedPasses) =>
        _myceliumMock.Setup(m => m.GetSnapshotResolutionMetricsAsync()).ReturnsAsync(Parse(
            $$"""{"Resolutions":{{resolutions}},"Retries":{{retries}},"LockedPasses":{{lockedPasses}}}"""));

    [Fact]
    public async Task Execute_NoArgs_PrintsTheThreeTotals()
    {
        MyceliumReports(resolutions: 1000, retries: 40, lockedPasses: 2);

        await ExecuteHandler("");

        var output = _writer.ToString();
        Assert.Contains("1000", output);
        Assert.Contains("40", output);
        Assert.Contains("2", output);
    }

    /// A retry total is only readable against the number of resolutions it happened in.
    [Fact]
    public async Task Execute_PrintsEachCountAsAShareOfResolutions()
    {
        MyceliumReports(resolutions: 1000, retries: 40, lockedPasses: 5);

        await ExecuteHandler("");

        var output = _writer.ToString();
        Assert.Contains("4.0%", output);
        Assert.Contains("0.5%", output);
    }

    /// The totals include the seed load, so the output has to say what a run's rate needs.
    [Fact]
    public async Task Execute_SaysHowToMeasureOneRun()
    {
        MyceliumReports(resolutions: 10, retries: 0, lockedPasses: 0);

        await ExecuteHandler("");

        Assert.Contains("difference", _writer.ToString());
    }

    [Fact]
    public async Task Execute_BeforeAnyResolution_PrintsNoShare()
    {
        MyceliumReports(resolutions: 0, retries: 0, lockedPasses: 0);

        await ExecuteHandler("");

        Assert.DoesNotContain("%", _writer.ToString());
    }

    [Fact]
    public async Task Execute_UnknownArgument_PrintsUsageAndAsksMyceliumNothing()
    {
        await ExecuteHandler("reactors");

        Assert.Contains("Usage:", _writer.ToString());
        _myceliumMock.Verify(m => m.GetSnapshotResolutionMetricsAsync(), Times.Never);
    }

    [Fact]
    public async Task Execute_MyceliumUnreachable_ReportsTheErrorInsteadOfThrowing()
    {
        _myceliumMock.Setup(m => m.GetSnapshotResolutionMetricsAsync())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        await ExecuteHandler("");

        Assert.Contains("Error: Connection refused", _writer.ToString());
    }
}
