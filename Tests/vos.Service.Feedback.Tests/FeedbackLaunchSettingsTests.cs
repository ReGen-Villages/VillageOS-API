using FluentAssertions;
using Microsoft.Extensions.Configuration;
using vos.Service.Feedback.Configuration;
using Xunit;

namespace vos.Service.Feedback.Tests;

public sealed class FeedbackLaunchSettingsTests : IDisposable
{
    private readonly string _destinationsFile = Path.Combine(Path.GetTempPath(), $"destinations-{Guid.NewGuid():N}.json");

    public FeedbackLaunchSettingsTests()
    {
        File.WriteAllText(_destinationsFile, """
            { "Console": { "project": "Clients", "areaPath": "Clients", "bugType": "Bug", "ideaType": "Feature" } }
            """);
    }

    public void Dispose() => File.Delete(_destinationsFile);

    private static IConfiguration Configuration(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(setting => setting.Key, setting => (string?)setting.Value))
            .Build();

    private string[] Arguments(params string[] extra) =>
    [
        "--port=7310", "--myceliumUrl=http://localhost:7243",
        "--devOpsOrganization=https://dev.azure.com/Example", $"--destinations={_destinationsFile}",
        .. extra,
    ];

    private static readonly IConfiguration WithAccessToken = Configuration(("DevOpsAccessToken", "secret"));

    [Fact]
    public void EverySettingGiven_IsRead()
    {
        var parsed = FeedbackLaunchSettings.Parse(Arguments("--allowedOrigin=https://a.example, https://b.example"), WithAccessToken);

        var settings = parsed.Settings!;
        settings.Service.Port.Should().Be(7310);
        settings.DevOpsOrganization.Should().Be(new Uri("https://dev.azure.com/Example"));
        settings.DevOpsAccessToken.Should().Be("secret");
        settings.AllowedOrigins.Should().Equal("https://a.example", "https://b.example");
        settings.Destinations.For("console")!.IdeaType.Should().Be("Feature");
        settings.Destinations.For("console")!.Tags.Should().BeEmpty();
    }

    [Fact]
    public void AnOrganisationThatIsNotAnAddress_IsRefused()
    {
        var parsed = FeedbackLaunchSettings.Parse(
            [.. Arguments().Where(argument => !argument.StartsWith("--devOpsOrganization")), "--devOpsOrganization=Example"],
            WithAccessToken);

        parsed.Settings.Should().BeNull();
        parsed.WhyRefused.Should().Contain("devOpsOrganization");
    }

    [Theory]
    [InlineData("http://dev.azure.com/Example", false)]
    [InlineData("http://localhost:7399/Example", true)]
    [InlineData("http://127.0.0.1:7399/Example", true)]
    public void PlainHttp_IsTakenOnlyForAStandInOnThisMachine(string organisation, bool taken)
    {
        var parsed = FeedbackLaunchSettings.Parse(
            [.. Arguments().Where(argument => !argument.StartsWith("--devOpsOrganization")), $"--devOpsOrganization={organisation}"],
            WithAccessToken);

        (parsed.Settings is not null).Should().Be(taken);
    }

    [Fact]
    public void TheAccessTokenOnTheCommandLine_IsNotRead()
    {
        var parsed = FeedbackLaunchSettings.Parse(Arguments("--devOpsAccessToken=secret"), Configuration());

        parsed.Settings.Should().BeNull();
        parsed.WhyRefused.Should().Contain("DevOpsAccessToken");
    }

    [Fact]
    public void ADestinationsFileThatDoesNotExist_IsRefusedByName()
    {
        File.Delete(_destinationsFile);

        var parsed = FeedbackLaunchSettings.Parse(Arguments(), WithAccessToken);

        parsed.Settings.Should().BeNull();
        parsed.WhyRefused.Should().Contain(_destinationsFile);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{ "Console": { "project": "Clients", "areaPath": "Clients", "bugType": "Bug" } }""")]
    public void ADestinationsFileThatNamesNoUsableDestination_IsRefused(string contents)
    {
        File.WriteAllText(_destinationsFile, contents);

        var parsed = FeedbackLaunchSettings.Parse(Arguments(), WithAccessToken);

        parsed.Settings.Should().BeNull();
        parsed.WhyRefused.Should().Contain(_destinationsFile);
    }

    [Fact]
    public void NoPortOrPlatformAddress_AsksForTheUsage()
    {
        var parsed = FeedbackLaunchSettings.Parse(["--devOpsOrganization=https://dev.azure.com/Example"], WithAccessToken);

        parsed.Settings.Should().BeNull();
        parsed.WhyRefused.Should().StartWith("Usage:");
    }
}
