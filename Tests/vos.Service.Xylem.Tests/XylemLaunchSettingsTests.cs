using FluentAssertions;
using Microsoft.Extensions.Configuration;
using vos.Service.Xylem.Configuration;
using Xunit;

namespace vos.Service.Xylem.Tests;

// The settings every service shares are covered once in vos.Service.Shared.Tests. These cover the
// ingest tool path and the upload limit, which only Xylem has.
public class XylemLaunchSettingsTests
{
    private static readonly string[] RequiredFlags =
        ["--port=7111", "--myceliumUrl=https://localhost:7243"];

    private static string[] FlagsWith(params string[] extra) => [.. RequiredFlags, .. extra];

    private static IConfiguration ConfigurationFrom(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            pairs.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value))).Build();

    [Fact]
    public void Parse_WhenACommonSettingIsMissing_ReturnsNull() =>
        XylemLaunchSettings.Parse(["--ifcIngestDll=/tools/ingest.dll"]).Should().BeNull();

    [Fact]
    public void Parse_CarriesTheCommonSettingsThrough()
    {
        var result = XylemLaunchSettings.Parse(FlagsWith("--token=my.jwt.token"));

        result.Should().NotBeNull();
        result!.Service.Port.Should().Be(7111);
        result.Service.MyceliumUrl.Should().Be("https://localhost:7243");
        result.Service.Token.Should().Be("my.jwt.token");
    }

    [Fact]
    public void Parse_ReadsTheIngestToolPath()
    {
        var result = XylemLaunchSettings.Parse(FlagsWith("--ifcIngestDll=/tools/vos.Tools.IfcIngest.dll"));

        result.Should().NotBeNull();
        result!.IfcIngestDll.Should().Be("/tools/vos.Tools.IfcIngest.dll");
    }

    [Fact]
    public void Parse_WithoutAnIngestToolPath_LeavesItUnset()
    {
        var result = XylemLaunchSettings.Parse(RequiredFlags);

        result.Should().NotBeNull();
        result!.IfcIngestDll.Should().BeNull();
    }

    [Fact]
    public void Parse_WithoutAnUploadLimit_UsesTheDefault()
    {
        var result = XylemLaunchSettings.Parse(RequiredFlags);

        result.Should().NotBeNull();
        result!.MaxUploadBytes.Should().Be(XylemLaunchSettings.DefaultMaxUploadBytes);
    }

    [Fact]
    public void Parse_ReadsTheUploadLimitInMegabytes()
    {
        var result = XylemLaunchSettings.Parse(FlagsWith("--maxUploadMb=64"));

        result.Should().NotBeNull();
        result!.MaxUploadBytes.Should().Be(64L * 1024 * 1024);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("enormous")]
    [InlineData("")]
    public void Parse_WithAnUnusableUploadLimit_FallsBackToTheDefault(string limit)
    {
        var result = XylemLaunchSettings.Parse(FlagsWith($"--maxUploadMb={limit}"));

        result.Should().NotBeNull();
        result!.MaxUploadBytes.Should().Be(XylemLaunchSettings.DefaultMaxUploadBytes);
    }

    [Fact]
    public void Parse_ReadsTheServiceSettingsFromConfiguration()
    {
        var configuration = ConfigurationFrom(
            ("Port", "7100"), ("MyceliumUrl", "http://mycelium"),
            ("IfcIngestDll", "/configured/ingest.dll"), ("MaxUploadMb", "32"));

        var result = XylemLaunchSettings.Parse(Array.Empty<string>(), configuration);

        result.Should().NotBeNull();
        result!.IfcIngestDll.Should().Be("/configured/ingest.dll");
        result.MaxUploadBytes.Should().Be(32L * 1024 * 1024);
    }

    [Fact]
    public void UsageMessage_NamesTheXylemFlags()
    {
        var usage = XylemLaunchSettings.UsageMessage;

        usage.Should().Contain("--ifcIngestDll")
            .And.Contain("--maxUploadMb")
            .And.Contain("--myceliumUrl");
    }
}
