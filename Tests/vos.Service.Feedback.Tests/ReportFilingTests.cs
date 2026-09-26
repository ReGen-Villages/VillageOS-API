using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Feedback.Configuration;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Feedback.Tests;

// The body is up to eight megabytes, so it is read only once the platform has accepted the caller.
public class ReportFilingTests
{
    private static ReportFiling FilingWherePlatformAnswers(HttpStatusCode status)
    {
        var clients = new PerCallHttpClientFactory(new MockHttpMessageHandler(_ => new HttpResponseMessage(status)));
        var destinations = Destinations.From(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Destinations:Trellis:Project"] = "Clients", ["Destinations:Trellis:AreaPath"] = "Clients",
                ["Destinations:Trellis:BugType"] = "Bug", ["Destinations:Trellis:IdeaType"] = "Feature",
            }).Build()).Destinations!;
        return new ReportFiling(
            new PlatformCallers(clients, "http://platform.test"),
            new DevOpsWorkItems(clients, new Uri("https://dev.azure.test/Example"), "token"),
            destinations, TimeProvider.System, NullLogger<ReportFiling>.Instance);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("a-token-the-platform-refuses")]
    public async Task ACallerNotSignedIn_NeverHasTheirReportRead(string? token)
    {
        var read = false;

        var answer = await FilingWherePlatformAnswers(HttpStatusCode.Unauthorized).FileAsync(
            token, _ => { read = true; return Task.FromResult<ReportRequest?>(null); }, CancellationToken.None);

        read.Should().BeFalse();
        answer.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }
}
