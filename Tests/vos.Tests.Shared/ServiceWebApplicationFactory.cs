using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace vos.Tests.Shared;

// Launch settings go in as host settings, which the launch-setting reader falls back to when no flag is given.
public abstract class ServiceWebApplicationFactory<TProgram> : WebApplicationFactory<TProgram>, IAsyncLifetime
    where TProgram : class
{
    // Set before the first CreateClient.
    public Func<HttpRequestMessage, HttpResponseMessage> HandlerCallback { get; set; }
        = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

    public MockHttpMessageHandler? Handler { get; private set; }

    public string MyceliumUrl { get; init; } = "http://localhost";

    public string? Token { get; init; } = "test-token";

    // Null leaves the service checking no inbound token.
    public string? VerificationKey { get; set; }
    public string? Issuer { get; set; }
    public string? Audience { get; set; }

    // Through IAsyncLifetime so xUnit never stops the host with a blocking call, which deadlocks under the coverage collector on Windows.
    public Task InitializeAsync() => Task.CompletedTask;

    public new Task DisposeAsync() => base.DisposeAsync().AsTask();

    protected virtual void AddSettings(IWebHostBuilder builder)
    {
    }

    protected virtual void ReplaceServices(IServiceCollection services)
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Port", "5000");
        builder.UseSetting("MyceliumUrl", MyceliumUrl);
        if (Token != null) builder.UseSetting("Token", Token);
        if (VerificationKey != null) builder.UseSetting("VerificationKey", VerificationKey);
        if (Issuer != null) builder.UseSetting("Issuer", Issuer);
        if (Audience != null) builder.UseSetting("Audience", Audience);
        AddSettings(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHttpClientFactory>();
            Handler = new MockHttpMessageHandler(request => HandlerCallback(request));
            services.AddSingleton<IHttpClientFactory>(new PerCallHttpClientFactory(Handler));
            ReplaceServices(services);
        });
    }
}
