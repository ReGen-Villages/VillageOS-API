using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
#if VOS_ENGINE_IS_STAGED
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using vos.Mycelium.Testing;
#endif
using vos.Service.Shared;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;
using vos.Tests.Shared;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// The real engine, started once for a test class, with a service's own client pointed at it.
//
// A service client reaches the broker over IHttpClientFactory and a URL, so nothing
// about it has to change to be tested this way: the factory hands out clients over the host's
// in-memory transport, and the service presents the administrator's bearer as the credential it was
// launched with. What is under test is the client the service ships, not a stand-in for it.
public sealed class TheEngine : IAsyncLifetime
{
    // The in-memory transport has no address of its own, and the clients build their URLs
    // by concatenation, so this is a base without a trailing slash rather than a host to reach.
    public const string Url = "http://localhost";

    public string AdminToken { get; private set; } = "";

    public IHttpClientFactory ClientFactory { get; private set; } = null!;

    // The host's own transport, for a case that watches what a service's client is sent.
    public HttpMessageHandler Transport { get; private set; } = null!;

    // Signed in as the administrator, for a test to arrange a model with and to read back
    // what a service wrote.
    public HttpClient Admin { get; private set; } = null!;

    // Read once at start, while the engine holds only the model it starts with, because a case may make more.
    private (Guid Id, string Name) _firstModel;

    internal TimeSpan? RetirementPassInterval { get; init; }

    internal string? SeedFile { get; init; }

#if !VOS_ENGINE_IS_STAGED
    public Task InitializeAsync() => throw new InvalidOperationException(
        "No engine is staged, so this case cannot run. Mark it [FactAgainstTheEngine], which skips it here.");

    public Task DisposeAsync() => Task.CompletedTask;

    private string SeedsDirectory => throw new InvalidOperationException("No engine is staged.");
#else
    private MyceliumTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = new MyceliumTestHost { SeedFile = SeedFile, RetirementPassInterval = RetirementPassInterval };
        await _host.InitializeAsync();

        var signingIn = _host.CreateClient();
        var signedIn = await signingIn.PostAsJsonAsync("/api/auth/login",
            new { Username = _host.AdminUsername, Password = _host.AdminPassword });
        signedIn.EnsureSuccessStatusCode();
        AdminToken = (await signedIn.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("token").GetString()!;

        Transport = _host.Server.CreateHandler();
        ClientFactory = new PerCallHttpClientFactory(Transport);
        Admin = _host.CreateClient();
        Admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        var first = (await Admin.GetFromJsonAsync<JsonElement>("/api/models")).EnumerateArray().Single();
        _firstModel = (Field(first, "id").GetGuid(), Field(first, "name").GetString()!);
    }

    public Task DisposeAsync()
    {
        Admin.Dispose();
        _host.Dispose();
        return Task.CompletedTask;
    }

    private string SeedsDirectory => _host.Services.GetRequiredService<IConfiguration>()["Seeds:Directory"]!;
#endif

    public async Task<HttpClient> EditorAsync()
    {
        var token = await TokenOfANewKeyAsync(new { Name = $"editor {Guid.NewGuid():N}", Role = EditorRole, ModelId = await TheModelIdAsync() });
        return Holding(token);
    }

    private const string EditorRole = "editor";

    // Templates sit below the seeds directory, which startup has already loaded, so the copy is read only when the model is made.
    public async Task<Guid> CreateModelFromAsync(string seedFile, string modelName)
    {
        var templates = Path.Combine(SeedsDirectory, "templates");
        Directory.CreateDirectory(templates);
        var template = $"{Guid.NewGuid():N}.seed.json";
        File.Copy(seedFile, Path.Combine(templates, template));

        var identifier = Guid.NewGuid();
        var made = await Admin.PostAsJsonAsync("/api/models", new { Name = modelName, Template = template, ModelId = identifier });
        made.IsSuccessStatusCode.Should().BeTrue(
            "the engine makes a model from the template, but it answered {0}: {1}", (int)made.StatusCode, await made.Content.ReadAsStringAsync());
        return identifier;
    }

    // A Thing, written the way the engine takes one. Tests arrange with this so that an
    // arrangement cannot be refused for the reason a case is looking for in the service.
    public async Task<Guid> DeclareAsync(
        string name, bool isArchetype = false, IReadOnlyDictionary<string, object?>? properties = null, Guid? identifier = null)
    {
        var id = identifier ?? Guid.NewGuid();
        var written = await Admin.PostAsJsonAsync("/api/things", new
        {
            Id = id,
            Name = name,
            IsArchetype = isArchetype,
            Properties = TypedProperties.Typed(properties),
        });

        written.IsSuccessStatusCode.Should().BeTrue(
            "the engine takes the Thing, but it answered {0}: {1}", (int)written.StatusCode, await written.Content.ReadAsStringAsync());
        return id;
    }

    // A relationship, written the way the engine takes one, for the same reason as a Thing.
    public async Task RelateAsync(Guid subjectId, Guid predicateId, Guid targetId)
    {
        var related = await Admin.PostAsJsonAsync("/api/relationships", new { subjectId, predicateId, targetId });
        related.EnsureSuccessStatusCode();
    }

    // The same, for a Thing the model may already hold. A host lives for a whole test class,
    // so an arrangement every case in it needs is written by whichever case runs first.
    public async Task<Guid> DeclareOnceAsync(
        string name, bool isArchetype = false, IReadOnlyDictionary<string, object?>? properties = null) =>
        await FindAsync(name) ?? await DeclareAsync(name, isArchetype, properties);

    // A role Thing is found by its identifier and named apart from its word, because a case may give the Thing it binds that same word.
    public async Task BindAsync(Role role, Guid boundId)
    {
        if (!await HoldsAsync(role.Id)) await DeclareAsync($"{role.Word} role", identifier: role.Id);
        await RelateAsync(role.Id, await DeclareOnceAsync(RoleBindings.BindsPredicateName), boundId);
    }

    // A host lives for a whole test class, so the first case to bind a role binds it for every case after.
    public async Task BindOnceAsync(Role role, Guid boundId)
    {
        if (!await HoldsAsync(role.Id)) await BindAsync(role, boundId);
    }

    public async Task<Guid> PlayingAsync(
        Role role, string name, bool isArchetype = false, IReadOnlyDictionary<string, object?>? properties = null)
    {
        var played = await DeclareOnceAsync(name, isArchetype, properties);
        await BindOnceAsync(role, played);
        return played;
    }

    private async Task<bool> HoldsAsync(Guid identifier)
    {
        var read = await Admin.GetAsync($"/api/things/{identifier}");
        if (read.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        read.EnsureSuccessStatusCode();
        return true;
    }

    // The Thing of that name, or null. Names are not unique in a model, and the route
    // refuses an ambiguous one rather than picking — which is a failure worth surfacing here too,
    // because an arrangement that made two is an arrangement that is wrong.
    public async Task<Guid?> FindAsync(string name)
    {
        var found = await Admin.GetAsync($"/api/things?name={Uri.EscapeDataString(name)}");
        if (found.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        found.EnsureSuccessStatusCode();

        return (await found.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("Id").GetGuid();
    }

    // A launched service's own token is minted by the broker and no route issues one, so a case that must not hold the
    // administrator's bearer takes a service key's token, which carries the same role and names its service the same way.
    public async Task<string> ServiceTokenAsync(string serviceName)
    {
        var modelId = await TheModelIdAsync();
        var isPredicate = await DeclareOnceAsync("is");
        var serviceKind = await DeclareOnceAsync("Service", isArchetype: true);
        await BindOnceAsync(ServiceKind, serviceKind);
        var service = await DeclareAsync($"{serviceName} service {Guid.NewGuid():N}");
        await RelateAsync(service, isPredicate, serviceKind);

        return await TokenOfANewKeyAsync(new { Name = $"{serviceName} {Guid.NewGuid():N}", Role = "service", ModelId = modelId, ServiceId = service });
    }

    private HttpClient Holding(string token)
    {
        var client = new HttpClient(Transport, disposeHandler: false) { BaseAddress = Admin.BaseAddress };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<Guid> TheModelIdAsync() => (await TheModelAsync()).Id;

    public Task<(Guid Id, string Name)> TheModelAsync() => Task.FromResult(_firstModel);

    private async Task<string> TokenOfANewKeyAsync(object key)
    {
        var issued = await Admin.PostAsJsonAsync("/api/auth/keys", key);
        issued.EnsureSuccessStatusCode();
        using var exchange = new HttpRequestMessage(HttpMethod.Post, "/api/auth/token");
        exchange.Headers.Add("X-API-Key", Field(await issued.Content.ReadFromJsonAsync<JsonElement>(), "rawKey").GetString());
        using var anonymous = new HttpClient(Transport, disposeHandler: false) { BaseAddress = Admin.BaseAddress };
        var exchanged = await anonymous.SendAsync(exchange);
        exchanged.EnsureSuccessStatusCode();
        return Field(await exchanged.Content.ReadFromJsonAsync<JsonElement>(), "token").GetString()!;
    }

    private static readonly Role ServiceKind = new("service", RoleIdentity.Of("service"));

    private static JsonElement Field(JsonElement answer, string name) =>
        answer.EnumerateObject().First(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase)).Value;

    public async Task<Guid> TheOnlyThingNamedAsync(string name) =>
        await FindAsync(name) ?? throw new InvalidOperationException($"The model holds no Thing named '{name}'.");

    public async Task<JsonElement> ReadAsync(Guid identifier) =>
        await Admin.GetFromJsonAsync<JsonElement>($"/api/things/{identifier}");

    // The own value of one property, or null where the Thing carries the property with
    // nothing in it. A property the Thing does not carry at all throws, because the difference
    // between "declared empty" and "never written" is what several of these cases are about.
    public async Task<string?> ValueOfAsync(Guid identifier, string property)
    {
        var value = (await ReadAsync(identifier)).GetProperty("Properties").GetProperty(property)
            .GetProperty("value");
        return value.ValueKind == JsonValueKind.Null ? null : value.ToString();
    }
}
