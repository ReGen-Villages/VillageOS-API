using Microsoft.AspNetCore.Hosting;
using vos.Tests.Shared;

namespace vos.Service.Tributary.Tests;

public class TributaryWebApplicationFactory : ServiceWebApplicationFactory<Program>
{
    // Null is the service's default. A cache test points it at a folder of its own so runs cannot see each other's files.
    public string? CacheDirectory { get; set; }

    protected override void AddSettings(IWebHostBuilder builder)
    {
        if (CacheDirectory != null) builder.UseSetting("CacheDirectory", CacheDirectory);
    }
}
