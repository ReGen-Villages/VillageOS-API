using System.Net;
using System.Text;
using vos.Service.Shared;
using vos.Service.Xylem.Services;
using vos.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using FluentAssertions;

namespace vos.Service.Xylem.Tests;

public class ModelIngestRunnerTests
{
    private const string MyceliumUrl = "http://localhost:5000";

    private static ServiceCredential Credential(
        string? serviceToken = null, string? apiKey = null, string? mintedToken = null)
    {
        var handler = new MockHttpMessageHandler(_ => mintedToken is null
            ? new HttpResponseMessage(HttpStatusCode.Forbidden)
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"token\":\"{mintedToken}\"}}", Encoding.UTF8, "application/json")
            });

        return new ServiceCredential(
            new TestHttpClientFactory(new HttpClient(handler)), NullLogger.Instance, MyceliumUrl,
            serviceToken, apiKey);
    }

    private static ModelIngestRunner Runner(ServiceCredential credential, string dll = "/tools/ModelIngest.dll") =>
        new(dll, MyceliumUrl, credential, NullLogger<ModelIngestRunner>.Instance);

    [Fact]
    public async Task RunAsync_missing_tool_reports_a_clear_error_without_spawning()
    {
        var runner = Runner(Credential(serviceToken: "tok"), dll: "/no/such/ModelIngest.dll");

        var r = await runner.RunAsync("/tmp/whatever.ifc", "Demo", default);

        r.Success.Should().BeFalse();
        r.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task The_ingest_process_is_handed_its_token_through_the_environment()
    {
        var startInfo = await Runner(Credential(serviceToken: "the.service.jwt"))
            .BuildStartInfoAsync("/tmp/model.ifc", "Demo", default);

        startInfo.Environment["Token"].Should().Be("the.service.jwt");
        startInfo.ArgumentList.Should().NotContain("--token").And.NotContain("the.service.jwt");
    }

    // The ingest tool authenticates with this setting and nothing else, so a service holding a key rather
    // than a token would launch it with no credential at all.
    [Fact]
    public async Task A_held_api_key_reaches_the_ingest_process_as_the_token_it_was_exchanged_for()
    {
        var minted = TestTokens.For(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1));

        var startInfo = await Runner(Credential(apiKey: "key-1", mintedToken: minted))
            .BuildStartInfoAsync("/tmp/model.ifc", "Demo", default);

        startInfo.Environment["Token"].Should().Be(minted);
    }

    [Fact]
    public async Task An_absent_token_leaves_the_ingest_process_without_one()
    {
        var startInfo = await Runner(Credential()).BuildStartInfoAsync("/tmp/model.ifc", "Demo", default);

        startInfo.Environment.Should().NotContainKey("Token");
    }

    [Theory]
    [InlineData("Ingested 12 things, 5 relationships.\nFragment POST: 200 applied", 12, 5)]
    [InlineData("Ingested 0 things, 0 relationships.", 0, 0)]
    [InlineData("no count line here", 0, 0)]
    public void ParseCounts_reads_the_totals(string stdout, int things, int rels)
    {
        ModelIngestRunner.ParseCounts(stdout).Should().Be((things, rels));
    }
}
