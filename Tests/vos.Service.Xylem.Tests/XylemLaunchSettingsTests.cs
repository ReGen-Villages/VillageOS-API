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
        XylemLaunchSettings.Parse(["--modelIngestDll=/tools/ingest.dll"]).Should().BeNull();

    [Fact]
    public void Parse_CarriesTheCommonSettingsThrough()
    {
        var result = XylemLaunchSettings.Parse(FlagsWith("--issuer=VillageOS"));

        result.Should().NotBeNull();
        result!.Service.Port.Should().Be(7111);
        result.Service.MyceliumUrl.Should().Be("https://localhost:7243");
        result.Service.Issuer.Should().Be("VillageOS");
    }

    [Fact]
    public void Parse_ReadsTheIngestToolPath()
    {
        var result = XylemLaunchSettings.Parse(FlagsWith("--modelIngestDll=/tools/vos.Tools.ModelIngest.dll"));

        result.Should().NotBeNull();
        result!.ModelIngestDll.Should().Be("/tools/vos.Tools.ModelIngest.dll");
    }

    [Fact]
    public void Parse_WithoutAnIngestToolPath_LeavesItUnset()
    {
        var result = XylemLaunchSettings.Parse(RequiredFlags);

        result.Should().NotBeNull();
        result!.ModelIngestDll.Should().BeNull();
    }

    [Fact]
    public void Parse_ReadsTheServiceSettingsFromConfiguration()
    {
        var configuration = ConfigurationFrom(
            ("Port", "7100"), ("MyceliumUrl", "http://mycelium"), ("ModelIngestDll", "/configured/ingest.dll"));

        var result = XylemLaunchSettings.Parse(Array.Empty<string>(), configuration);

        result.Should().NotBeNull();
        result!.ModelIngestDll.Should().Be("/configured/ingest.dll");
    }

    [Fact]
    public void UsageMessage_NamesTheXylemFlags()
    {
        var usage = XylemLaunchSettings.UsageMessage;

        usage.Should().Contain("--modelIngestDll")
            .And.Contain("--myceliumUrl")
            .And.NotContain("--maxUploadMb");
    }
}
