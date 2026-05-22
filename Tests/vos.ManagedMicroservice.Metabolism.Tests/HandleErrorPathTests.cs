using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace vos.ManagedMicroservice.Metabolism.Tests;

/// <summary>
/// Integration test for Metabolism's /handle endpoint error path: when the inbound
/// JSON properties contain a value that cannot be parsed by <c>ExtractConfig</c>
/// (e.g. an unparseable <c>startUtc</c>), the handler returns 500 rather than crashing
/// the host or returning an opaque success.
///
/// Pins the contract that malformed request bodies surface as a clean error response.
/// Moved from <c>CoverageGapTests.cs</c> under Task #5437.
/// </summary>
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
