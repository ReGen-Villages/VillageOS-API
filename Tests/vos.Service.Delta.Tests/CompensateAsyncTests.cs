using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace vos.Service.Delta.Tests;

public class CompensateAsyncTests
{
    [Fact]
    public async Task Compensate_DeleteAlsoFails_StillAttemptsDelete()
    {
        var model = new EndpointModelStandIn();
        model.SeedTree(Guid.Empty);
        model.RefusePropertySet = true;
        model.RefuseThingDelete = true;
        await using var factory = new DeltaWebApplicationFactory { HandlerCallback = model.Route };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle",
            new { name = "MyEndpoint", properties = new { url = "https://x.example/", httpMethod = "GET" } });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        model.ThingsDeleted.Should().ContainSingle("compensation must attempt the delete even when it will fail");
    }
}
