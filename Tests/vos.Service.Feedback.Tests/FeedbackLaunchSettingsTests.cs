using FluentAssertions;
using Microsoft.Extensions.Configuration;
using vos.Service.Feedback.Configuration;
using Xunit;

namespace vos.Service.Feedback.Tests;

public sealed class FeedbackLaunchSettingsTests
{
    private static readonly string[] Arguments = ["--port=7310", "--myceliumUrl=http://localhost:7243"];

    private static readonly Dictionary<string, string?> Settings = new()
    {
        ["DevOpsOrganization"] = "https://dev.azure.com/Example",
        ["DevOpsAccessToken"] = "secret",
        ["Destinations:Console:Project"] = "Clients",
        ["Destinations:Console:AreaPath"] = "Clients",
        ["Destinations:Console:BugType"] = "Bug",
        ["Destinations:Console:IdeaType"] = "Feature",
    };

    private static IConfiguration Configuration(Action<Dictionary<string, string?>>? change = null)
    {
        var settings = new Dictionary<string, string?>(Settings);
        change?.Invoke(settings);
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    [Fact]
    public void EverySettingGiven_IsRead()
    {
        var parsed = FeedbackLaunchSettings.Parse(
            [.. Arguments, "--allowedOrigin=https://a.example, https://b.example"],
            Configuration(settings => settings["Destinations:Console:Tags:0"] = "Console"));

        var settings = parsed.Settings!;
        settings.Service.Port.Should().Be(7310);
        settings.DevOpsOrganization.Should().Be(new Uri("https://dev.azure.com/Example"));
        settings.DevOpsAccessToken.Should().Be("secret");
        settings.AllowedOrigins.Should().Equal("https://a.example", "https://b.example");
        settings.Destinations.For("console")!.IdeaType.Should().Be("Feature");
        settings.Destinations.For("console")!.Tags.Should().Equal("Console");
    }

    [Fact]
    public void TheRelaysOwnSettingsFile_FilesTrellisReportsInTheConsolesProject()
    {
        var shipped = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DevOpsAccessToken"] = "secret" })
            .Build();

        var parsed = FeedbackLaunchSettings.Parse(Arguments, shipped);

        parsed.Settings!.Destinations.For("Trellis")!.Project.Should().Be("VillageOS-API");
    }

    [Theory]
    [InlineData("Example")]
    [InlineData("http://dev.azure.com/Example")]
    public void AnOrganisationThatIsNotAnHttpsAddress_IsRefused(string organisation)
    {
        var parsed = FeedbackLaunchSettings.Parse(Arguments, Configuration(settings => settings["DevOpsOrganization"] = organisation));

        parsed.Settings.Should().BeNull();
        parsed.WhyRefused.Should().Contain("DevOpsOrganization");
    }

    [Theory]
    [InlineData("http://localhost:7399/Example")]
    [InlineData("http://127.0.0.1:7399/Example")]
    public void PlainHttp_IsTakenForAStandInOnThisMachine(string organisation)
    {
        var parsed = FeedbackLaunchSettings.Parse(Arguments, Configuration(settings => settings["DevOpsOrganization"] = organisation));

        parsed.Settings.Should().NotBeNull();
    }

    [Fact]
    public void TheAccessTokenOnTheCommandLine_IsNotRead()
    {
        var parsed = FeedbackLaunchSettings.Parse(
            [.. Arguments, "--devOpsAccessToken=secret"], Configuration(settings => settings.Remove("DevOpsAccessToken")));

        parsed.Settings.Should().BeNull();
        parsed.WhyRefused.Should().Contain("DevOpsAccessToken");
    }

    [Fact]
    public void SettingsNamingNoDestination_AreRefused()
    {
        var parsed = FeedbackLaunchSettings.Parse(Arguments, Configuration(settings =>
        {
            foreach (var key in settings.Keys.Where(key => key.StartsWith("Destinations:")).ToList()) settings.Remove(key);
        }));

        parsed.Settings.Should().BeNull();
        parsed.WhyRefused.Should().Contain("Destinations");
    }

    [Fact]
    public void ADestinationMissingAWorkItemType_IsRefusedByName()
    {
        var parsed = FeedbackLaunchSettings.Parse(Arguments, Configuration(settings => settings.Remove("Destinations:Console:IdeaType")));

        parsed.Settings.Should().BeNull();
        parsed.WhyRefused.Should().Contain("Destinations:Console");
    }

    [Fact]
    public void NoPortOrPlatformAddress_AsksForTheUsage()
    {
        var parsed = FeedbackLaunchSettings.Parse([], Configuration());

        parsed.Settings.Should().BeNull();
        parsed.WhyRefused.Should().StartWith("Usage:");
    }
}
