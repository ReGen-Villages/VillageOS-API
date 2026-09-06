using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using vos.Mycelium.Testing;
using vos.Service.Shared;
using vos.Tests.Shared;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

/// <summary>
/// The real engine, started once for a test class, with a service's own client pointed at it.
///
/// A service client reaches the broker over <see cref="IHttpClientFactory"/> and a URL, so nothing
/// about it has to change to be tested this way: the factory hands out clients over the host's
/// in-memory transport, and the service presents the administrator's bearer as the credential it was
/// launched with. What is under test is the client the service ships, not a stand-in for it.
/// </summary>
public sealed class TheEngine : IAsyncLifetime
{
    /// <summary>The in-memory transport has no address of its own, and the clients build their URLs
    /// by concatenation, so this is a base without a trailing slash rather than a host to reach.</summary>
    public const string Url = "http://localhost";

    private MyceliumTestHost _host = null!;

    public string AdminToken { get; private set; } = "";

    public IHttpClientFactory ClientFactory { get; private set; } = null!;

    /// <summary>Signed in as the administrator, for a test to arrange a model with and to read back
    /// what a service wrote.</summary>
    public HttpClient Admin { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _host = new MyceliumTestHost();
        await _host.InitializeAsync();

        var signingIn = _host.CreateClient();
        var signedIn = await signingIn.PostAsJsonAsync("/api/auth/login",
            new { Username = _host.AdminUsername, Password = _host.AdminPassword });
        signedIn.EnsureSuccessStatusCode();
        AdminToken = (await signedIn.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("token").GetString()!;

        ClientFactory = new PerCallHttpClientFactory(_host.Server.CreateHandler());
        Admin = _host.CreateClient();
        Admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
    }

    public Task DisposeAsync()
    {
        Admin.Dispose();
        _host.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>A Thing, written the way the engine takes one. Tests arrange with this so that an
    /// arrangement cannot be refused for the reason a case is looking for in the service.</summary>
    public async Task<Guid> DeclareAsync(
        string name, bool isArchetype = false, IReadOnlyDictionary<string, object?>? properties = null)
    {
        var identifier = Guid.NewGuid();
        var written = await Admin.PostAsJsonAsync("/api/things", new
        {
            Id = identifier,
            Name = name,
            IsArchetype = isArchetype,
            Properties = TypedProperties.Typed(properties ?? new Dictionary<string, object?>()),
        });

        written.EnsureSuccessStatusCode();
        return identifier;
    }

    /// <summary>The same, for a Thing the model may already hold. A host lives for a whole test class,
    /// so an arrangement every case in it needs is written by whichever case runs first.</summary>
    public async Task<Guid> DeclareOnceAsync(
        string name, bool isArchetype = false, IReadOnlyDictionary<string, object?>? properties = null) =>
        await FindAsync(name) ?? await DeclareAsync(name, isArchetype, properties);

    /// <summary>The Thing of that name, or null. Names are not unique in a model, and the route
    /// refuses an ambiguous one rather than picking — which is a failure worth surfacing here too,
    /// because an arrangement that made two is an arrangement that is wrong.</summary>
    public async Task<Guid?> FindAsync(string name)
    {
        var found = await Admin.GetAsync($"/api/things?name={Uri.EscapeDataString(name)}");
        if (found.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        found.EnsureSuccessStatusCode();

        return (await found.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("Id").GetGuid();
    }

    public async Task<Guid> TheOnlyThingNamedAsync(string name) =>
        await FindAsync(name) ?? throw new InvalidOperationException($"The model holds no Thing named '{name}'.");

    public async Task<JsonElement> ReadAsync(Guid identifier) =>
        await Admin.GetFromJsonAsync<JsonElement>($"/api/things/{identifier}");

    /// <summary>The own value of one property, or null where the Thing carries the property with
    /// nothing in it. A property the Thing does not carry at all throws, because the difference
    /// between "declared empty" and "never written" is what several of these cases are about.</summary>
    public async Task<string?> ValueOfAsync(Guid identifier, string property)
    {
        var value = (await ReadAsync(identifier)).GetProperty("Properties").GetProperty(property)
            .GetProperty("value");
        return value.ValueKind == JsonValueKind.Null ? null : value.ToString();
    }
}
