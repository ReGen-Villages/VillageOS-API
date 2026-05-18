// Targeted tests covering specific branches in vos.ManagedMicroservice.IntegrationRegistry
// that the per-class test files don't otherwise exercise. Each test names the gap it pins.

using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using vos.ManagedMicroservice.IntegrationRegistry.Configuration;
using Xunit;

namespace vos.ManagedMicroservice.IntegrationRegistry.Tests;

[Collection(nameof(IntegrationRegistryEnvVarCollection))]
public class CoverageGapTests
{
    // ---- CliArgs.UsageMessage ----

    [Fact]
    public void UsageMessage_MentionsEveryFlag()
    {
        var msg = CliArgs.UsageMessage;
        msg.Should().Contain("--port");
        msg.Should().Contain("--brokerUrl");
        msg.Should().Contain("--token");
        msg.Should().Contain("--signingKey");
        msg.Should().Contain("--issuer");
        msg.Should().Contain("--audience");
    }

    // ---- Program.cs TryGetPropertyValue case-insensitive fallback ----
    // properties dict has "URL" key; TryGetStringProperty("url", ...) → first TryGetValue
    // misses, falls through to the loop that does case-insensitive equality.

    [Fact]
    public async Task Handle_CaseInsensitivePropertyKey_HappyPath()
    {
        await using var factory = new IntegrationRegistryWebApplicationFactory();
        await factory.InitializeAsync();
        var isId = Guid.NewGuid();
        var defaultId = Guid.NewGuid();
        var registeredId = Guid.NewGuid();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/things" && req.Method == HttpMethod.Get
                && req.RequestUri.Query.Contains("name=is"))
                return Json("{\"Id\":\"" + isId + "\",\"Name\":\"is\",\"Properties\":{}}");
            if (req.RequestUri.AbsolutePath == "/api/things" && req.Method == HttpMethod.Get
                && req.RequestUri.Query.Contains("name=Endpoint"))
                return Json("{\"Id\":\"" + defaultId + "\",\"Name\":\"Endpoint\",\"Properties\":{}}");
            if (req.RequestUri.AbsolutePath == "/api/things" && req.Method == HttpMethod.Post)
                return Json("{\"Id\":\"" + registeredId + "\",\"Name\":\"EP\",\"Properties\":{}}");
            if (req.RequestUri.AbsolutePath == "/api/relationships" && req.Method == HttpMethod.Post)
                return new HttpResponseMessage(HttpStatusCode.Created);
            if (req.RequestUri.AbsolutePath == $"/api/things/{registeredId}/properties" && req.Method == HttpMethod.Put)
                return new HttpResponseMessage(HttpStatusCode.OK);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new
        {
            name = "EP",
            properties = new Dictionary<string, object>
            {
                ["URL"] = "https://x.example/",  // uppercase variant
                ["HTTPMethod"] = "GET"
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---- Program.cs CoerceToString JsonElement arms (Number / True / False / Null) ----
    // The url/httpMethod values aren't strings; CoerceToString routes them through the
    // matching ValueKind switch arm. All four assertions land on early-returns downstream
    // (invalid URL / non-empty checks) but the CoerceToString arms execute first.

    [Fact]
    public async Task Handle_NumericUrlValue_CoerceToStringNumberArm()
    {
        var response = await PostWithPropertyValue(new { url = 99, httpMethod = "GET" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Invalid endpoint url");
    }

    [Fact]
    public async Task Handle_BoolTrueMethodValue_CoerceToStringTrueArm()
    {
        var response = await PostWithPropertyValue(new { url = "https://x", httpMethod = true });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // "true" is non-empty but fails HttpMethodValidator
        (await response.Content.ReadAsStringAsync()).Should().Contain("Unsupported httpMethod");
    }

    [Fact]
    public async Task Handle_BoolFalseMethodValue_CoerceToStringFalseArm()
    {
        var response = await PostWithPropertyValue(new { url = "https://x", httpMethod = false });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Unsupported httpMethod");
    }

    [Fact]
    public async Task Handle_NullUrlValue_CoerceToStringNullArm()
    {
        var response = await PostWithPropertyValue(new { url = (string?)null, httpMethod = "GET" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("url must be a non-empty");
    }

    // ---- Program.cs LoadEndpointSeed: parse-failure catch + throw ----
    // Override the factory's SeedJson with garbage so JsonSerializer throws → catch fires.
    // The other candidate paths (CWD, ../../../) typically have no seed.json in the test
    // process, so the foreach falls through and the InvalidOperationException is thrown.

    [Fact]
    public async Task LoadEndpointSeed_MalformedJson_ThrowsAtStartup()
    {
        await using var factory = new MalformedSeedFactory();
        await factory.InitializeAsync();

        // CreateClient triggers host construction → LoadEndpointSeed → throws.
        var act = () => factory.CreateClient();

        act.Should().Throw<Exception>(); // InvalidOperationException wrapped by the host
    }

    // ---- Program.cs auth wireup ----
    // Boot with a signingKey + issuer + audience so AddBrokerTokenAuth + UseAuthentication +
    // UseAuthorization all execute. /health remains open; /handle, /register, /shutdown
    // require auth and 401 without a Bearer token.

    [Fact]
    public async Task BootWithSigningKey_HealthStillReturns200()
    {
        await using var factory = new AuthEnabledFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BootWithSigningKey_HandleWithoutBearer_Returns401()
    {
        await using var factory = new AuthEnabledFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { name = "X", properties = new { url = "x" } });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BootWithSigningKey_RegisterWithoutBearer_Returns401()
    {
        await using var factory = new AuthEnabledFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/register", new { name = "X", properties = new { url = "x" } });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- Program.cs CompensateAsync delete-failure log ----

    [Fact]
    public async Task Compensate_DeleteAlsoFails_LogsError()
    {
        // Forces SetThingProperty to fail (triggering CompensateAsync), AND makes the
        // DELETE in CompensateAsync also fail — pins the "Compensation failed" log line.
        await using var factory = new IntegrationRegistryWebApplicationFactory();
        await factory.InitializeAsync();
        var isId = Guid.NewGuid();
        var defaultId = Guid.NewGuid();
        var registeredId = Guid.NewGuid();
        var deleteAttempted = false;
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/things" && req.Method == HttpMethod.Get
                && req.RequestUri.Query.Contains("name=is"))
                return Json("{\"Id\":\"" + isId + "\",\"Name\":\"is\",\"Properties\":{}}");
            if (req.RequestUri.AbsolutePath == "/api/things" && req.Method == HttpMethod.Get
                && req.RequestUri.Query.Contains("name=Endpoint"))
                return Json("{\"Id\":\"" + defaultId + "\",\"Name\":\"Endpoint\",\"Properties\":{}}");
            if (req.RequestUri.AbsolutePath == "/api/things" && req.Method == HttpMethod.Post)
                return Json("{\"Id\":\"" + registeredId + "\",\"Name\":\"MyEndpoint\",\"Properties\":{}}");
            if (req.RequestUri.AbsolutePath == "/api/relationships" && req.Method == HttpMethod.Post)
                return new HttpResponseMessage(HttpStatusCode.Created);
            if (req.RequestUri.AbsolutePath == $"/api/things/{registeredId}/properties" && req.Method == HttpMethod.Put)
                return new HttpResponseMessage(HttpStatusCode.BadRequest);  // SetThingProperty fails → triggers Compensate
            if (req.RequestUri.AbsolutePath == $"/api/things/{registeredId}" && req.Method == HttpMethod.Delete)
            {
                deleteAttempted = true;
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);  // Compensate also fails → line 270 fires
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle",
            new { name = "MyEndpoint", properties = new { url = "https://x", httpMethod = "GET" } });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        deleteAttempted.Should().BeTrue("compensation must attempt delete even when it will fail");
    }

    [Fact]
    public async Task BootWithSigningKey_ShutdownWithoutBearer_Returns401()
    {
        await using var factory = new AuthEnabledFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        (await client.PostAsync("/shutdown", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---------- Helpers ----------

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private static async Task<HttpResponseMessage> PostWithPropertyValue(object properties)
    {
        var factory = new IntegrationRegistryWebApplicationFactory();
        await factory.InitializeAsync();
        var isId = Guid.NewGuid();
        var defaultId = Guid.NewGuid();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/things" && req.Method == HttpMethod.Get
                && req.RequestUri.Query.Contains("name=is"))
                return Json("{\"Id\":\"" + isId + "\",\"Name\":\"is\",\"Properties\":{}}");
            if (req.RequestUri.AbsolutePath == "/api/things" && req.Method == HttpMethod.Get
                && req.RequestUri.Query.Contains("name=Endpoint"))
                return Json("{\"Id\":\"" + defaultId + "\",\"Name\":\"Endpoint\",\"Properties\":{}}");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/handle", new { name = "EP", properties });
        await factory.DisposeAsync();
        return response;
    }

    private sealed class MalformedSeedFactory : IntegrationRegistryWebApplicationFactory
    {
        public MalformedSeedFactory()
        {
            SeedJson = "{ not valid json at all";
        }
    }

    private sealed class AuthEnabledFactory : IntegrationRegistryWebApplicationFactory
    {
        public new Task InitializeAsync()
        {
            base.InitializeAsync().GetAwaiter().GetResult();
            Environment.SetEnvironmentVariable("INTEGRATIONREGISTRY_SIGNING_KEY",
                Convert.ToBase64String(new byte[32]));
            Environment.SetEnvironmentVariable("INTEGRATIONREGISTRY_ISSUER", "VillageOS");
            Environment.SetEnvironmentVariable("INTEGRATIONREGISTRY_AUDIENCE", "VosClients");
            return Task.CompletedTask;
        }

        public new Task DisposeAsync()
        {
            Environment.SetEnvironmentVariable("INTEGRATIONREGISTRY_SIGNING_KEY", null);
            Environment.SetEnvironmentVariable("INTEGRATIONREGISTRY_ISSUER", null);
            Environment.SetEnvironmentVariable("INTEGRATIONREGISTRY_AUDIENCE", null);
            return base.DisposeAsync();
        }
    }
}
