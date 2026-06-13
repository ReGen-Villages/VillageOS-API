using Moq;
using Xunit;

namespace vos.Taproot.Tests;

public class FileSystemCommandHandlerTests
{
    private readonly Mock<BrokerClient> _brokerMock;
    private readonly StringWriter _writer;

    public FileSystemCommandHandlerTests()
    {
        _brokerMock = new Mock<BrokerClient>("https://localhost:7243") { CallBase = false };
        _writer = new StringWriter();
    }

    private async Task ExecuteHandler(string cmd, string arg)
    {
        var handler = new FileSystemCommandHandler(cmd, arg, _writer, _brokerMock.Object);
        await handler.ExecuteAsync();
    }

    [Fact]
    public async Task Pwd_DisplaysCurrentDirectory()
    {
        await ExecuteHandler("pwd", "");

        var output = _writer.ToString();
        Assert.Contains("Current directory:", output);
        Assert.Contains(Directory.GetCurrentDirectory(), output);
    }

    [Fact]
    public async Task Cd_NoPath_ShowsUsage()
    {
        await ExecuteHandler("cd", "");

        Assert.Contains("Usage:", _writer.ToString());
    }

    [Fact]
    public async Task Cd_ValidPath_ChangesDirectory()
    {
        var originalDir = Directory.GetCurrentDirectory();
        var tempDir = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);

        try
        {
            await ExecuteHandler("cd", tempDir);

            Assert.Contains("Changed directory to:", _writer.ToString());
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }

    [Fact]
    public async Task Cd_InvalidPath_ShowsError()
    {
        await ExecuteHandler("cd", "/nonexistent/path/that/does/not/exist");

        Assert.Contains("Error:", _writer.ToString());
    }

    [Fact]
    public async Task Serialize_NoPath_OutputsToWriter()
    {
        _brokerMock.Setup(b => b.GetModelJsonAsync()).ReturnsAsync("{\"things\": []}");

        await ExecuteHandler("serialize", "");

        var output = _writer.ToString();
        Assert.Contains("{\"things\": []}", output);
        _brokerMock.Verify(b => b.GetModelJsonAsync(), Times.Once);
    }

    [Fact]
    public async Task Serialize_WithPath_WritesToFile()
    {
        var tempFile = Path.GetTempFileName();
        _brokerMock.Setup(b => b.GetModelJsonAsync()).ReturnsAsync("{\"test\": true}");

        try
        {
            await ExecuteHandler("serialize", tempFile);

            Assert.True(File.Exists(tempFile));
            var content = File.ReadAllText(tempFile);
            Assert.Contains("test", content);
            Assert.Contains("Model saved to", _writer.ToString());
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Deserialize_NoPath_ShowsUsage()
    {
        await ExecuteHandler("deserialize", "");

        Assert.Contains("Usage:", _writer.ToString());
    }

    [Fact]
    public async Task Deserialize_NonExistentFile_ShowsError()
    {
        await ExecuteHandler("deserialize", "/nonexistent/file.json");

        Assert.Contains("Error:", _writer.ToString());
        Assert.Contains("not found", _writer.ToString());
    }

    [Fact]
    public async Task Deserialize_ValidFile_CallsSetModel()
    {
        var tempFile = Path.GetTempFileName();
        var jsonContent = "{\"things\": []}";

        File.WriteAllText(tempFile, jsonContent);
        _brokerMock.Setup(b => b.SetModelAsync(jsonContent)).ReturnsAsync("OK");

        try
        {
            await ExecuteHandler("deserialize", tempFile);

            _brokerMock.Verify(b => b.SetModelAsync(jsonContent), Times.Once);
            Assert.Contains("Model loaded from", _writer.ToString());
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Serialize_BrokerError_ShowsError()
    {
        _brokerMock.Setup(b => b.GetModelJsonAsync()).ThrowsAsync(new HttpRequestException("Connection failed"));

        await ExecuteHandler("serialize", "");

        Assert.Contains("Error:", _writer.ToString());
    }

    [Fact]
    public async Task Deserialize_BrokerError_ShowsError()
    {
        var tempFile = Path.GetTempFileName();
        var jsonContent = "{\"things\": []}";

        File.WriteAllText(tempFile, jsonContent);
        _brokerMock.Setup(b => b.SetModelAsync(It.IsAny<string>())).ThrowsAsync(new HttpRequestException("Connection failed"));

        try
        {
            await ExecuteHandler("deserialize", tempFile);

            Assert.Contains("Error:", _writer.ToString());
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Deserialize_WithoutExtension_AddsJsonExtension()
    {
        // Arrange - Create a file with .json extension
        var tempDir = Path.GetTempPath();
        var baseName = $"test_model_{Guid.NewGuid():N}";
        var tempFile = Path.Combine(tempDir, baseName + ".json");
        var jsonContent = "{\"things\": []}";

        File.WriteAllText(tempFile, jsonContent);
        _brokerMock.Setup(b => b.SetModelAsync(jsonContent)).ReturnsAsync("OK");

        try
        {
            // Act - Use path without .json extension
            await ExecuteHandler("deserialize", Path.Combine(tempDir, baseName));

            // Assert - Should find the file with .json appended
            _brokerMock.Verify(b => b.SetModelAsync(jsonContent), Times.Once);
            Assert.Contains("Model loaded from", _writer.ToString());
            Assert.Contains(".json", _writer.ToString());
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Deserialize_WithJsonExtension_DoesNotDoubleAppend()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var jsonFile = tempFile + ".json";
        var jsonContent = "{\"things\": []}";

        // Rename to .json extension
        File.Move(tempFile, jsonFile);
        File.WriteAllText(jsonFile, jsonContent);
        _brokerMock.Setup(b => b.SetModelAsync(jsonContent)).ReturnsAsync("OK");

        try
        {
            // Act - Use path with .json extension already
            await ExecuteHandler("deserialize", jsonFile);

            // Assert - Should not look for .json.json
            _brokerMock.Verify(b => b.SetModelAsync(jsonContent), Times.Once);
            Assert.DoesNotContain(".json.json", _writer.ToString());
        }
        finally
        {
            if (File.Exists(jsonFile))
                File.Delete(jsonFile);
        }
    }

    [Fact]
    public async Task Serialize_WithoutExtension_AddsJsonExtension()
    {
        // Arrange
        var tempDir = Path.GetTempPath();
        var baseName = $"test_model_{Guid.NewGuid():N}";
        var expectedFile = Path.Combine(tempDir, baseName + ".json");

        _brokerMock.Setup(b => b.GetModelJsonAsync()).ReturnsAsync("{\"test\": true}");

        try
        {
            // Act - Use path without .json extension
            await ExecuteHandler("serialize", Path.Combine(tempDir, baseName));

            // Assert - Should create file with .json appended
            Assert.True(File.Exists(expectedFile), $"Expected file {expectedFile} to exist");
            var content = File.ReadAllText(expectedFile);
            Assert.Contains("test", content);
            Assert.Contains(".json", _writer.ToString());
        }
        finally
        {
            if (File.Exists(expectedFile))
                File.Delete(expectedFile);
        }
    }

    [Fact]
    public async Task Serialize_WithJsonExtension_DoesNotDoubleAppend()
    {
        // Arrange
        var tempDir = Path.GetTempPath();
        var fileName = $"test_model_{Guid.NewGuid():N}.json";
        var tempFile = Path.Combine(tempDir, fileName);

        _brokerMock.Setup(b => b.GetModelJsonAsync()).ReturnsAsync("{\"test\": true}");

        try
        {
            // Act - Use path with .json extension already
            await ExecuteHandler("serialize", tempFile);

            // Assert - Should NOT create .json.json file
            Assert.True(File.Exists(tempFile));
            Assert.False(File.Exists(tempFile + ".json"), "Should not create .json.json file");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Deserialize_WithOtherExtension_DoesNotAddJson()
    {
        // Arrange - Create a file with .txt extension
        var tempFile = Path.GetTempFileName(); // Creates .tmp file
        var txtFile = Path.ChangeExtension(tempFile, ".txt");
        var jsonContent = "{\"things\": []}";

        File.Move(tempFile, txtFile);
        File.WriteAllText(txtFile, jsonContent);
        _brokerMock.Setup(b => b.SetModelAsync(jsonContent)).ReturnsAsync("OK");

        try
        {
            // Act - Use .txt file path
            await ExecuteHandler("deserialize", txtFile);

            // Assert - Should load the .txt file without adding .json
            _brokerMock.Verify(b => b.SetModelAsync(jsonContent), Times.Once);
        }
        finally
        {
            if (File.Exists(txtFile))
                File.Delete(txtFile);
        }
    }
}
