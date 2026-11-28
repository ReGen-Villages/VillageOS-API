using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using vos.Service.Phloem.Execution;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Phloem.Tests;

public class StartUpRecoveryTests
{
    [Fact]
    public async Task Bug8746_TheRecoveryAtStartWaitsUntilTheBrokerHasTakenTheRegistration()
    {
        var asks = 0;
        var taken = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (drawing, pipelineId) = TestGraphs.DemoPipeline();
        var gateway = new FakeGateway(drawing.Build());
        var queued = Guid.NewGuid();
        await gateway.CreateRunAsync(queued, pipelineId, CancellationToken.None, queueNumber: 1);
        await using var phloem = new PhloemOver(gateway, request =>
            Interlocked.Increment(ref asks) == 1 ? Task.FromResult(StillStarting()) : taken.Task);

        phloem.StartServer();
        await Settle.UntilAsync(() => Volatile.Read(ref asks) == 2, "the orchestrator asks again after the broker's 503");
        await Settle.BeforeAssertingAbsenceAsync(TimeSpan.FromMilliseconds(300));
        gateway.RunStatusOf(queued).Should().Be(RunStatus.Queued, "the broker has not taken the registration yet");

        taken.SetResult(new HttpResponseMessage(HttpStatusCode.OK));
        await Settle.UntilAsync(() => gateway.RunStatusOf(queued) != RunStatus.Queued,
            "the recovery begins the queued run once the broker has taken the registration");
    }

    private static HttpResponseMessage StillStarting()
    {
        var refused = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        refused.Headers.Add("Retry-After", "0");
        return refused;
    }

    private sealed class PhloemOver(FakeGateway gateway, Func<HttpRequestMessage, Task<HttpResponseMessage>> answerRegistration)
        : WebApplicationFactory<Program>
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
                services.AddSingleton<IHttpClientFactory>(new PerCallHttpClientFactory(MockHttpMessageHandler.AnsweringAsynchronously(request =>
                    request.RequestUri!.AbsolutePath == "/api/mycelium/register"
                        ? answerRegistration(request)
                        : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))));
                services.RemoveAll<IMyceliumGateway>();
                services.AddSingleton<IMyceliumGateway>(gateway);
            });
        }
    }
}
