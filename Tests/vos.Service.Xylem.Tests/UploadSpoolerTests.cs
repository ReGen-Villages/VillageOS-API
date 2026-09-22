using vos.Service.Xylem.Services;
using Xunit;
using FluentAssertions;

namespace vos.Service.Xylem.Tests;

public class UploadSpoolerTests
{
    private static string Temp() => Path.Combine(Path.GetTempPath(), $"spool_{Guid.NewGuid():N}.tmp");

    [Fact]
    public async Task Spools_a_stream_under_the_cap_and_reports_bytes_written()
    {
        var path = Temp();
        try
        {
            var written = await UploadSpooler.SpoolAsync(new MemoryStream(new byte[100]), path, maxBytes: 1000, default);
            written.Should().Be(100);
            new FileInfo(path).Length.Should().Be(100);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Aborts_when_the_stream_exceeds_the_cap()
    {
        var path = Temp();
        try
        {
            var written = await UploadSpooler.SpoolAsync(new MemoryStream(new byte[5000]), path, maxBytes: 1000, default);
            written.Should().BeNegative("the copy stops once the cap is exceeded");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Empty_stream_reports_zero_bytes()
    {
        var path = Temp();
        try
        {
            (await UploadSpooler.SpoolAsync(new MemoryStream(), path, 1000, default)).Should().Be(0);
        }
        finally { File.Delete(path); }
    }
}
