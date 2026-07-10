using vos.ManagedMicroservice.Xylem.Configuration;
using Xunit;
using FluentAssertions;

namespace vos.ManagedMicroservice.Xylem.Tests;

public class CliArgsTests
{
    [Fact]
    public void Parse_requires_port_and_myceliumUrl()
    {
        CliArgs.Parse(new[] { "--myceliumUrl=http://localhost:5000" }).Should().BeNull();
        CliArgs.Parse(new[] { "--port=6000" }).Should().BeNull();
    }

    [Fact]
    public void Parse_rejects_a_bad_port()
    {
        CliArgs.Parse(new[] { "--port=0", "--myceliumUrl=http://x" }).Should().BeNull();
        CliArgs.Parse(new[] { "--port=abc", "--myceliumUrl=http://x" }).Should().BeNull();
    }

    [Fact]
    public void Parse_reads_all_fields()
    {
        var a = CliArgs.Parse(new[]
        {
            "--port=6100", "--myceliumUrl=http://localhost:5000", "--token=jwt",
            "--ifcIngestDll=/tools/IfcIngest.dll", "--maxUploadMb=256",
        });
        a.Should().NotBeNull();
        a!.Port.Should().Be(6100);
        a.MyceliumUrl.Should().Be("http://localhost:5000");
        a.Token.Should().Be("jwt");
        a.IfcIngestDll.Should().Be("/tools/IfcIngest.dll");
        a.MaxUploadBytes.Should().Be(256L * 1024 * 1024);
    }

    [Fact]
    public void Parse_defaults_the_upload_cap()
    {
        var a = CliArgs.Parse(new[] { "--port=6100", "--myceliumUrl=http://x" });
        a!.MaxUploadBytes.Should().Be(512L * 1024 * 1024);
    }
}
