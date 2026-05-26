using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using vos.ManagedMicroservice.Metabolism.Services;
using Xunit;

namespace vos.ManagedMicroservice.Metabolism.Tests;

/// <summary>
/// Pins the DI substitution seam (Task #5456). Endpoint handlers must resolve
/// <see cref="Services.Metabolism"/> from the container, so a test that swaps the
/// singleton with a stub via <c>services.RemoveAll&lt;Metabolism&gt;() + services.AddSingleton(stub)</c>
/// actually changes what the endpoint sees.
/// </summary>
public class DependencyInjectionTests
{
    private sealed class StubMetabolism : Services.Metabolism
    {
        public bool GetAllWasCalled { get; private set; }

        public StubMetabolism(BrokerClient brokerClient, ILogger<Services.Metabolism> logger)
            : base(brokerClient, logger, "consumes")
        {
        }

        public override IEnumerable<SimulationEntry> GetAll()
        {
            GetAllWasCalled = true;
            return base.GetAll();
        }
    }

    private static StubMetabolism BuildStub()
    {
        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        var brokerLogger = new Mock<ILogger<BrokerClient>>();
        var brokerClient = new BrokerClient(httpFactory.Object, brokerLogger.Object, "http://localhost:0", "consumes");
        var engineLogger = new Mock<ILogger<Services.Metabolism>>();
        return new StubMetabolism(brokerClient, engineLogger.Object);
    }

    [Fact]
    public async Task SimulationsEndpoint_ResolvesMetabolismFromContainer_HitsSubstitutedInstance()
    {
        var stub = BuildStub();

        await using var factory = new MetabolismWebApplicationFactory
        {
            ConfigureServices = services =>
            {
                services.RemoveAll<Services.Metabolism>();
                services.AddSingleton<Services.Metabolism>(stub);
            }
        };

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/simulations");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        stub.GetAllWasCalled.Should().BeTrue(
            "the /simulations endpoint must resolve Metabolism from DI, so a substituted singleton receives the call");
    }
}
