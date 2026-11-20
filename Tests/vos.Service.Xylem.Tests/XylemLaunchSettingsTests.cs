using FluentAssertions;
using vos.Service.Xylem.Configuration;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Xylem.Tests;

// The settings every service shares are covered once in vos.Service.Shared.Tests. These cover the
// ingest tool path, which only Xylem has.
public class XylemLaunchSettingsTests
{
    [Fact]
    public void Parse_WhenACommonSettingIsMissing_ReturnsNull() =>
        XylemLaunchSettings.Parse(["--modelIngestDll=/tools/ingest.dll"]).Should().BeNull();

    [Fact]
    public void Parse_CarriesTheCommonSettingsThrough()
    {
        var result = XylemLaunchSettings.Parse(LaunchFlags.With("--issuer=VillageOS"));

        result.Should().NotBeNull();
        result!.Service.Port.Should().Be(LaunchFlags.Port);
        result.Service.MyceliumUrl.Should().Be(LaunchFlags.MyceliumUrl);
        result.Service.Issuer.Should().Be("VillageOS");
    }

    [Fact]
    public void Parse_ReadsTheIngestToolPath()
    {
        var result = XylemLaunchSettings.Parse(LaunchFlags.With("--modelIngestDll=/tools/vos.Tools.ModelIngest.dll"));

        result.Should().NotBeNull();
        result!.ModelIngestDll.Should().Be("/tools/vos.Tools.ModelIngest.dll");
    }

    [Fact]
    public void Parse_WithoutAnIngestToolPath_LeavesItUnset()
    {
        var result = XylemLaunchSettings.Parse(LaunchFlags.Required);

        result.Should().NotBeNull();
        result!.ModelIngestDll.Should().BeNull();
    }

    [Fact]
    public void Parse_ReadsTheServiceSettingsFromConfiguration()
    {
        var configuration = LaunchFlags.ConfigurationFrom(
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
