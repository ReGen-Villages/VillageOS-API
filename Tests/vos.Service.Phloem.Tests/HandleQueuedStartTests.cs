using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Phloem.Tests;

public class HandleQueuedStartTests
{
    [Fact]
    public async Task Handle_AStartItsCallerWaitsFor_IsAnsweredOnlyOnceItsQueuedRunHasEnded()
    {
        var (drawing, pipelineId) = TestGraphs.DemoPipeline();
        drawing.State(drawing.Get("Demo"), ModelNames.RunsAtOnce, 1);
        var answer = new TaskCompletionSource<NodeCallResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateway = new FakeGateway(drawing.Build()) { FreshGraph = drawing.Build, OnCallAsync = (_, _) => answer.Task };
        await using var factory = new FactoryOver(gateway);
        var client = factory.CreateClient();
        var first = StartAsync(client, pipelineId);
        await Settle.UntilAsync(() => gateway.CalledSoFar().Count == 1, "the first run holds the only place");

        var second = StartAsync(client, pipelineId);
        await Settle.UntilAsync(() => { lock (gateway.RunsCreated) return gateway.RunsCreated.Count == 2; }, "the second run is recorded");
        var queued = gateway.RunStatusOf(gateway.RunsCreated[1].RunId);
        answer.SetResult(new NodeCallResult(200, """{"result":"success","outputs":{"echo":"x"}}"""));

        queued.Should().Be(RunStatus.Queued);
        (await SucceededAsync(await first)).Should().BeTrue();
        (await SucceededAsync(await second)).Should().BeTrue();
    }

    private static Task<HttpResponseMessage> StartAsync(HttpClient client, Guid pipelineId) => client.PostAsync("/handle",
        new StringContent(JsonSerializer.Serialize(new { pipelineId, parameters = new { } }), Encoding.UTF8, "application/json"));

    private static async Task<bool> SucceededAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("success").GetBoolean();

    private sealed class FactoryOver(FakeGateway gateway) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Port", "5000");
            builder.UseSetting("MyceliumUrl", "http://localhost");
            builder.UseSetting("Token", "test-token");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHttpClientFactory>();
                services.AddSingleton<IHttpClientFactory>(new PerCallHttpClientFactory(new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))));
                services.RemoveAll<IMyceliumGateway>();
                services.AddSingleton<IMyceliumGateway>(gateway);
            });
        }
    }
}
