using FluentAssertions;
using vos.Service.Intake.Configuration;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Intake.Tests;

// The settings every service shares are covered once in vos.Service.Shared.Tests. These cover the
// public form origins, which only Intake has. What the origins then do to a request is covered end to
// end in PublicFormCorsTests.
public class IntakeLaunchSettingsTests
{
    private const string FormOrigin = "https://app.example.org";
    private const string SecondFormOrigin = "https://www.example.org";

    [Fact]
    public void Parse_WhenACommonSettingIsMissing_ReturnsNull() =>
        IntakeLaunchSettings.Parse([$"--publicFormOrigin={FormOrigin}"]).Should().BeNull();

    [Fact]
    public void Parse_CarriesTheCommonSettingsThrough()
    {
        var result = IntakeLaunchSettings.Parse(LaunchFlags.With("--issuer=VillageOS"));

        result.Should().NotBeNull();
        result!.Service.Port.Should().Be(LaunchFlags.Port);
        result.Service.MyceliumUrl.Should().Be(LaunchFlags.MyceliumUrl);
        result.Service.Issuer.Should().Be("VillageOS");
    }

    [Fact]
    public void Parse_WithoutAnOrigin_AllowsNoCrossOriginCaller()
    {
        var result = IntakeLaunchSettings.Parse(LaunchFlags.Required);

        result.Should().NotBeNull();
        result!.PublicFormOrigins.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ReadsOneOrigin()
    {
        var result = IntakeLaunchSettings.Parse(LaunchFlags.With($"--publicFormOrigin={FormOrigin}"));

        result.Should().NotBeNull();
        result!.PublicFormOrigins.Should().Equal(FormOrigin);
    }

    [Fact]
    public void Parse_SeparatesSeveralOriginsOnCommas()
    {
        var result = IntakeLaunchSettings.Parse(
            LaunchFlags.With($"--publicFormOrigin={FormOrigin},{SecondFormOrigin}"));

        result.Should().NotBeNull();
        result!.PublicFormOrigins.Should().Equal(FormOrigin, SecondFormOrigin);
    }

    [Fact]
    public void Parse_TrimsTheSpaceAroundEachOrigin()
    {
        var result = IntakeLaunchSettings.Parse(
            LaunchFlags.With($"--publicFormOrigin= {FormOrigin} , {SecondFormOrigin} "));

        result.Should().NotBeNull();
        result!.PublicFormOrigins.Should().Equal(FormOrigin, SecondFormOrigin);
    }

    [Theory]
    [InlineData("")]
    [InlineData(",")]
    [InlineData(" , ")]
    public void Parse_WithNothingBetweenTheCommas_AllowsNoCrossOriginCaller(string named)
    {
        var result = IntakeLaunchSettings.Parse(LaunchFlags.With($"--publicFormOrigin={named}"));

        result.Should().NotBeNull();
        result!.PublicFormOrigins.Should().BeEmpty();
    }

    [Fact]
    public void Parse_DropsAnEmptyEntryAndKeepsTheRest()
    {
        var result = IntakeLaunchSettings.Parse(
            LaunchFlags.With($"--publicFormOrigin={FormOrigin},,{SecondFormOrigin},"));

        result.Should().NotBeNull();
        result!.PublicFormOrigins.Should().Equal(FormOrigin, SecondFormOrigin);
    }

    [Fact]
    public void Parse_ReadsTheOriginsFromConfiguration()
    {
        var configuration = LaunchFlags.ConfigurationFrom(
            ("Port", "7300"), ("MyceliumUrl", "http://mycelium"),
            ("PublicFormOrigin", $"{FormOrigin},{SecondFormOrigin}"));

        var result = IntakeLaunchSettings.Parse([], configuration);

        result.Should().NotBeNull();
        result!.PublicFormOrigins.Should().Equal(FormOrigin, SecondFormOrigin);
    }

    [Fact]
    public void UsageMessage_NamesEverySettingOfItsOwn()
    {
        var usage = IntakeLaunchSettings.UsageMessage;

        usage.Should().Contain("--publicFormOrigin")
            .And.Contain("--mailDelivery")
            .And.Contain("--mailHost")
            .And.Contain("--mailFrom")
            .And.Contain("--myceliumUrl");
    }
}
