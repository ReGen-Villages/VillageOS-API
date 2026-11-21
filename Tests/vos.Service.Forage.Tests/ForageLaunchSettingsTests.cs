using FluentAssertions;
using vos.Service.Forage.Configuration;
using Xunit;

namespace vos.Service.Forage.Tests;

public class ForageLaunchSettingsTests
{
    [Fact]
    public void Parse_WithoutTheServiceSettings_ReturnsNull()
    {
        ForageLaunchSettings.Parse(Array.Empty<string>()).Should().BeNull();
    }

    [Fact]
    public void UsageMessage_NamesNoSettingForageNoLongerReads()
    {
        ForageLaunchSettings.UsageMessage.Should()
            .NotContain("--fetcherSubdomain")
            .And.NotContain("--sourceTimeoutSeconds")
            .And.NotContain("--sourceWindowDays")
            .And.NotContain("--maxConcurrentSources")
            .And.NotContain("--attemptsPerSlice");
    }
}
