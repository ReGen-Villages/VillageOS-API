using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using vos.Tests.Shared;

namespace vos.Service.Metabolism.Tests;

// One factory per test, because Metabolism's request counter and simulations are held per instance.
public class MetabolismWebApplicationFactory : ServiceWebApplicationFactory<Program>
{
    public MetabolismWebApplicationFactory()
    {
        MyceliumUrl = "http://localhost:1";
        Token = null;
    }

    // Applied after the production registrations land. Set before the first CreateClient().
    public Action<IServiceCollection>? ConfigureServices { get; set; }

    public Func<HttpRequestMessage, HttpResponseMessage> Mycelium
    {
        init => HandlerCallback = value;
    }

    protected override void AddSettings(IWebHostBuilder builder) => builder.UseSetting("Mode", "consumes");

    protected override void ReplaceServices(IServiceCollection services) => ConfigureServices?.Invoke(services);
}
