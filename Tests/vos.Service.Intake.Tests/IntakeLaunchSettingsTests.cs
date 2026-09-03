using FluentAssertions;
using Microsoft.Extensions.Configuration;
using vos.Service.Intake.Configuration;
using Xunit;

namespace vos.Service.Intake.Tests;

// The settings every service shares are covered once in vos.Service.Shared.Tests. These cover the
// public form origins, which only Intake has. What the origins then do to a request is covered end to
// end in PublicFormCorsTests.
public class IntakeLaunchSettingsTests
{
    private const string FormOrigin = "https://app.example.org";
    private const string SecondFormOrigin = "https://www.example.org";

    private static readonly string[] RequiredFlags = ["--port=7300", "--myceliumUrl=https://localhost:7243"];

    private static string[] FlagsWith(params string[] extra) => [.. RequiredFlags, .. extra];

    private static IConfiguration ConfigurationFrom(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            pairs.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value))).Build();

    [Fact]
    public void Parse_WhenACommonSettingIsMissing_ReturnsNull() =>
        IntakeLaunchSettings.Parse([$"--publicFormOrigin={FormOrigin}"]).Should().BeNull();

    [Fact]
    public void Parse_CarriesTheCommonSettingsThrough()
    {
        var result = IntakeLaunchSettings.Parse(FlagsWith("--issuer=VillageOS"));

        result.Should().NotBeNull();
        result!.Service.Port.Should().Be(7300);
        result.Service.MyceliumUrl.Should().Be("https://localhost:7243");
        result.Service.Issuer.Should().Be("VillageOS");
    }

    [Fact]
    public void Parse_WithoutAnOrigin_AllowsNoCrossOriginCaller()
    {
        var result = IntakeLaunchSettings.Parse(RequiredFlags);

        result.Should().NotBeNull();
        result!.PublicFormOrigins.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ReadsOneOrigin()
    {
        var result = IntakeLaunchSettings.Parse(FlagsWith($"--publicFormOrigin={FormOrigin}"));

        result.Should().NotBeNull();
        result!.PublicFormOrigins.Should().Equal(FormOrigin);
    }

    [Fact]
    public void Parse_SeparatesSeveralOriginsOnCommas()
    {
        var result = IntakeLaunchSettings.Parse(
            FlagsWith($"--publicFormOrigin={FormOrigin},{SecondFormOrigin}"));

        result.Should().NotBeNull();
        result!.PublicFormOrigins.Should().Equal(FormOrigin, SecondFormOrigin);
    }

    [Fact]
    public void Parse_TrimsTheSpaceAroundEachOrigin()
    {
        var result = IntakeLaunchSettings.Parse(
            FlagsWith($"--publicFormOrigin= {FormOrigin} , {SecondFormOrigin} "));

        result.Should().NotBeNull();
        result!.PublicFormOrigins.Should().Equal(FormOrigin, SecondFormOrigin);
    }

    [Theory]
    [InlineData("")]
    [InlineData(",")]
    [InlineData(" , ")]
    public void Parse_WithNothingBetweenTheCommas_AllowsNoCrossOriginCaller(string named)
    {
        var result = IntakeLaunchSettings.Parse(FlagsWith($"--publicFormOrigin={named}"));

        result.Should().NotBeNull();
        result!.PublicFormOrigins.Should().BeEmpty();
    }

    [Fact]
    public void Parse_DropsAnEmptyEntryAndKeepsTheRest()
    {
        var result = IntakeLaunchSettings.Parse(
            FlagsWith($"--publicFormOrigin={FormOrigin},,{SecondFormOrigin},"));

        result.Should().NotBeNull();
        result!.PublicFormOrigins.Should().Equal(FormOrigin, SecondFormOrigin);
    }

    [Fact]
    public void Parse_ReadsTheOriginsFromConfiguration()
    {
        var configuration = ConfigurationFrom(
            ("Port", "7300"), ("MyceliumUrl", "http://mycelium"),
            ("PublicFormOrigin", $"{FormOrigin},{SecondFormOrigin}"));

        var result = IntakeLaunchSettings.Parse([], configuration);

        result.Should().NotBeNull();
        result!.PublicFormOrigins.Should().Equal(FormOrigin, SecondFormOrigin);
    }

    // The same default the discovery service uses, because both forward through the routing label the
    // shipped analysis template declares for the fetching service.
    [Fact]
    public void Parse_WithoutAFetcherSubdomain_ForwardsThroughTheShippedLabel()
    {
        var result = IntakeLaunchSettings.Parse(RequiredFlags);

        result!.FetcherSubdomain.Should().Be("tributary");
    }

    [Fact]
    public void Parse_ReadsTheFetcherSubdomain()
    {
        var result = IntakeLaunchSettings.Parse(FlagsWith("--fetcherSubdomain=fetch-relay"));

        result!.FetcherSubdomain.Should().Be("fetch-relay");
    }

    [Fact]
    public void UsageMessage_NamesEverySettingOfItsOwn()
    {
        var usage = IntakeLaunchSettings.UsageMessage;

        usage.Should().Contain("--fetcherSubdomain");
        usage.Should().Contain("--publicFormOrigin")
            .And.Contain("--mailDelivery")
            .And.Contain("--mailHost")
            .And.Contain("--mailFrom")
            .And.Contain("--myceliumUrl");
    }
}
