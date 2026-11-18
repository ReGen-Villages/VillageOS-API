using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using vos.Service.Forage.Steps;
using vos.Service.Shared.DagNode;
using Xunit;

namespace vos.Service.Forage.Tests;

// The /handle surface in vos.Service.Forage/Program.cs: a pipeline box's call is answered by its step
// (StepRequestTests), and anything else is refused.
public class HandleEndpointTests
{
    [Fact]
    public async Task Handle_ARelationshipCall_IsRefusedSayingDiscoveryIsTheDrawnPipeline_AndReadsAndCallsNothing()
    {
        var requests = new System.Collections.Concurrent.ConcurrentBag<string>();
        await using var factory = new ForageWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = request =>
        {
            requests.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { subjectId = Guid.NewGuid(), targetName = "discoversSite" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(ForageSteps.RelationshipCallRefusal);
        requests.Should().NotContain(request => request.Contains("/api/snapshots") || request.Contains("/api/endpoints/"));
    }

    [Fact]
    public async Task Handle_ABodyNamingNoSubject_IsRefusedInTheSharedWords()
    {
        await using var factory = new ForageWebApplicationFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should()
            .Contain(HandleRequestRouter.DescribeExpectedShapes("Forage"));
    }

    // A body the service cannot read at all is still a body it can refuse. Raising instead leaves the
    // caller with a failed request, and a failed service call is called again — so text that can never parse
    // would be retried on a loop rather than turned down once.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("this is not json")]
    [InlineData("{\"subjectId\":")]
    public async Task Handle_ABodyThatIsNotJson_IsRefusedInTheSharedWords(string body)
    {
        await using var factory = new ForageWebApplicationFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/handle", new StringContent(body, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should()
            .Contain(HandleRequestRouter.DescribeExpectedShapes("Forage"));
    }

    [Fact]
    public async Task Health_ReportsTheService()
    {
        await using var factory = new ForageWebApplicationFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"status\":\"Healthy\"").And.Contain("\"service\":\"Forage\"");
    }

    [Fact]
    public async Task Shutdown_Returns200WithMessage()
    {
        await using var factory = new ForageWebApplicationFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/shutdown", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Shutting down Forage");
    }
}
