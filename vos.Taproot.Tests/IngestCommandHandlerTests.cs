using System.Text.Json;
using FluentAssertions;
using Moq;
using Xunit;

namespace vos.Taproot.Tests;

// Covers the `ingest <file.ifc>` command (US #5843): upload an IFC to the Xylem service and report counts.
public class IngestCommandHandlerTests : IDisposable
{
    private readonly Mock<MyceliumClient> _myceliumMock;
    private readonly StringWriter _writer;
    private readonly string _tempDir;
    private const string Url = "http://localhost:6100";

    public IngestCommandHandlerTests()
    {
        _myceliumMock = new Mock<MyceliumClient>("https://localhost:7243") { CallBase = false };
        _writer = new StringWriter();
        _tempDir = Path.Combine(Path.GetTempPath(), $"IngestTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        Environment.SetEnvironmentVariable("VOS_INGEST_URL", null);
    }

    private async Task Execute(string arg)
    {
        var handler = new IngestCommandHandler(arg, _writer, _myceliumMock.Object);
        await handler.ExecuteAsync();
    }

    private string WriteIfc(string name = "model.ifc")
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, "ISO-10303-21;\nENDSEC;\n");
        return path;
    }

    private static JsonElement Result(bool success, int created = 0, int updated = 0, int rels = 0, string? error = null) =>
        JsonDocument.Parse(
            $"{{\"success\":{success.ToString().ToLower()},\"thingsCreated\":{created},\"thingsUpdated\":{updated}," +
            $"\"relationshipsCreated\":{rels},\"error\":{(error is null ? "null" : $"\"{error}\"")}}}")
            .RootElement;

    [Fact]
    public async Task Ingest_uploads_the_file_and_reports_counts()
    {
        var path = WriteIfc();
        string? seenUrl = null, seenName = null, seenMode = null;
        _myceliumMock.Setup(m => m.IngestIfcAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string, string>((u, _, n, mode) => { seenUrl = u; seenName = n; seenMode = mode; })
            .ReturnsAsync(Result(true, 5, 1, 3));

        await Execute($"{path} --url={Url} --name=Demo");

        seenUrl.Should().Be(Url);
        seenName.Should().Be("Demo");
        seenMode.Should().Be("merge");
        var output = _writer.ToString();
        output.Should().Contain("5");
        output.Should().Contain("3");
    }

    [Fact]
    public async Task Ingest_new_flag_selects_new_model_mode()
    {
        var path = WriteIfc();
        string? seenMode = null;
        _myceliumMock.Setup(m => m.IngestIfcAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string, string>((_, _, _, mode) => seenMode = mode)
            .ReturnsAsync(Result(true));

        await Execute($"{path} --url={Url} --new");

        seenMode.Should().Be("new-model");
    }

    [Fact]
    public async Task Ingest_defaults_the_model_name_to_the_file_name()
    {
        var path = WriteIfc("Village.ifc");
        string? seenName = null;
        _myceliumMock.Setup(m => m.IngestIfcAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string, string>((_, _, n, _) => seenName = n)
            .ReturnsAsync(Result(true));

        await Execute($"{path} --url={Url}");

        seenName.Should().Be("Village");
    }

    [Fact]
    public async Task Ingest_missing_file_errors_without_calling_the_service()
    {
        await Execute($"{Path.Combine(_tempDir, "nope.ifc")} --url={Url}");

        _writer.ToString().Should().Contain("not found");
        _myceliumMock.Verify(m => m.IngestIfcAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Ingest_without_a_service_url_errors()
    {
        var path = WriteIfc();
        await Execute(path); // no --url and no VOS_INGEST_URL

        _writer.ToString().Should().Contain("URL");
        _myceliumMock.Verify(m => m.IngestIfcAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Ingest_no_argument_shows_usage()
    {
        await Execute("");
        _writer.ToString().Should().Contain("Usage");
    }

    [Fact]
    public async Task Ingest_service_failure_prints_the_error()
    {
        var path = WriteIfc();
        _myceliumMock.Setup(m => m.IngestIfcAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Result(false, error: "xbim parse error"));

        await Execute($"{path} --url={Url}");

        _writer.ToString().Should().Contain("xbim parse error");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }
}
