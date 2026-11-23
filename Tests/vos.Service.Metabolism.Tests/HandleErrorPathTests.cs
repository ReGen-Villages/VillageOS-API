using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace vos.Service.Metabolism.Tests;

// A value in the body's properties that ExtractConfig cannot parse, such as an unparseable startUtc, is
// answered with a clean 500 rather than crashing the host or answering with an opaque success.
public class HandleErrorPathTests
{
    [Fact]
    public async Task Handle_InvalidStartUtcInProperties_Returns500()
    {
        var factory = new MetabolismWebApplicationFactory();
        await factory.InitializeAsync();
        try
        {
            using var client = factory.CreateClient();

            var response = await client.PostAsJsonAsync("/handle", new
            {
                relationshipId = Guid.NewGuid().ToString(),
                subjectId = Guid.NewGuid().ToString(),
                targetId = Guid.NewGuid().ToString(),
                properties = new
                {
                    startUtc = "not a date"   // DateTime.Parse throws inside ExtractConfig
                }
            });

            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }
}
