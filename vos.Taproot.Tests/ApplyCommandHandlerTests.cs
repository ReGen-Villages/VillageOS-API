using System.Text.Json;
using FluentAssertions;
using Moq;
using Xunit;

namespace vos.Taproot.Tests;

// Covers the `apply <file.json>` command (US #5814): read a fragment payload from a file,
// upsert it via MyceliumClient.ApplyFragmentAsync, and report the counts.
public class ApplyCommandHandlerTests : IDisposable
{
    private readonly Mock<MyceliumClient> _myceliumMock;
    private readonly StringWriter _writer;
    private readonly string _tempDir;

    public ApplyCommandHandlerTests()
    {
        _myceliumMock = new Mock<MyceliumClient>("https://localhost:7243") { CallBase = false };
        _writer = new StringWriter();
        _tempDir = Path.Combine(Path.GetTempPath(), $"ApplyTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    private async Task Execute(string arg)
    {
        var handler = new ApplyCommandHandler(arg, _writer, _myceliumMock.Object);
        await handler.ExecuteAsync();
    }

    private string WriteFragmentFile(string content)
    {
        var path = Path.Combine(_tempDir, "fragment.json");
        File.WriteAllText(path, content);
        return path;
    }

    private static JsonElement Counts(int created, int updated, int rels) =>
        JsonDocument.Parse(
            $"{{\"thingsCreated\":{created},\"thingsUpdated\":{updated},\"relationshipsCreated\":{rels}}}")
            .RootElement;

    [Fact]
    public async Task Apply_ReadsFile_PostsExactContent_AndReportsCounts()
    {
        var fragment = "{\"Name\":\"demo\",\"Things\":[{\"Id\":\"1\",\"Name\":\"a\"}],\"Relationships\":[]}";
        var path = WriteFragmentFile(fragment);
        string? posted = null;
        _myceliumMock
            .Setup(m => m.ApplyFragmentAsync(It.IsAny<string>()))
            .Callback<string>(s => posted = s)
            .ReturnsAsync(Counts(2, 1, 3));

        await Execute(path);

        posted.Should().Be(fragment);
        var output = _writer.ToString();
        output.Should().Contain("2");
        output.Should().Contain("created");
        output.Should().Contain("3");
    }

    [Fact]
    public async Task Apply_MissingFile_PrintsErrorAndDoesNotCallClient()
    {
        await Execute(Path.Combine(_tempDir, "does-not-exist.json"));

        _writer.ToString().Should().Contain("not found");
        _myceliumMock.Verify(m => m.ApplyFragmentAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Apply_NoArgument_ShowsUsage()
    {
        await Execute("");

        _writer.ToString().Should().Contain("Usage");
        _myceliumMock.Verify(m => m.ApplyFragmentAsync(It.IsAny<string>()), Times.Never);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }
}
