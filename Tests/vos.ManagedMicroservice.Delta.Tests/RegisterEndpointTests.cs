using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Xunit;

namespace vos.ManagedMicroservice.Delta.Tests;

// Tests for the /handle (and /register alias) endpoint in
// vos.ManagedMicroservice.Delta/Program.cs. Each test wires a per-scenario
// HandlerCallback on the factory so the broker calls all resolve through the same handler.
//
// Test naming convention: most tests target /handle. The /register alias delegates to the
// same HandleRegisterEndpointRequestAsync helper, so we cover it with a single shared-routing
// test rather than duplicating the entire decision tree.
[Collection(nameof(DeltaEnvVarCollection))]
public class RegisterEndpointTests
{
    // ---------- Request validation ----------

    [Fact]
    public async Task Handle_MissingName_Returns400()
    {
        await using var factory = new DeltaWebApplicationFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { name = "", properties = new { url = "x" } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("non-empty thing name");
    }

    [Fact]
    public async Task Handle_MissingProperties_Returns400()
    {
        await using var factory = new DeltaWebApplicationFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { name = "EP", properties = (object?)null });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("must include properties");
    }

    [Fact]
    public async Task Handle_IsPredicateMissing_Returns500()
    {
        await using var factory = new DeltaWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            // GET /api/things?name=is → empty (null) thing
            if (req.RequestUri!.AbsolutePath == "/api/things" && req.RequestUri.Query.Contains("name=is"))
                return Json("null");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", BasicValidBody());

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).Should().Contain("'is' predicate");
    }

    [Fact]
    public async Task Handle_SeedHasNoAllowedKeys_Returns500()
    {
        // Override the default seed.json with one whose properties are empty.
        await using var factory = new DeltaWebApplicationFactory
        {
            SeedJson = """{"name":"Endpoint","properties":{}}"""
        };
        await factory.InitializeAsync();
        var isId = Guid.NewGuid();
        factory.HandlerCallback = req => RouteFindThing(req, isId, "is")
            ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", BasicValidBody());

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Endpoint seed does not define");
    }

    [Fact]
    public async Task Handle_UnsupportedProperties_Returns400WithNames()
    {
        await using var factory = new DeltaWebApplicationFactory();
        await factory.InitializeAsync();
        var isId = Guid.NewGuid();
        factory.HandlerCallback = req => RouteFindThing(req, isId, "is")
            ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new
        {
            name = "MyEndpoint",
            properties = new { url = "https://x", httpMethod = "GET", notAllowed = "boom", alsoNotAllowed = 1 }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("unsupported properties");
        body.Should().Contain("notAllowed");
        body.Should().Contain("alsoNotAllowed");
    }

    [Fact]
    public async Task Handle_MissingUrl_Returns400()
    {
        await using var factory = new DeltaWebApplicationFactory();
        await factory.InitializeAsync();
        var isId = Guid.NewGuid();
        factory.HandlerCallback = req => RouteFindThing(req, isId, "is")
            ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new
        {
            name = "MyEndpoint",
            properties = new { httpMethod = "GET" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("url must be a non-empty string");
    }

    [Fact]
    public async Task Handle_MissingHttpMethod_Returns400()
    {
        await using var factory = new DeltaWebApplicationFactory();
        await factory.InitializeAsync();
        var isId = Guid.NewGuid();
        factory.HandlerCallback = req => RouteFindThing(req, isId, "is")
            ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new
        {
            name = "MyEndpoint",
            properties = new { url = "https://x" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("httpMethod must be a non-empty string");
    }

    [Fact]
    public async Task Handle_InvalidUri_Returns400()
    {
        await using var factory = new DeltaWebApplicationFactory();
        await factory.InitializeAsync();
        var isId = Guid.NewGuid();
        factory.HandlerCallback = req => RouteFindThing(req, isId, "is")
            ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new
        {
            name = "MyEndpoint",
            properties = new { url = "not-a-url", httpMethod = "GET" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Invalid endpoint url");
    }

    [Fact]
    public async Task Handle_UnsupportedHttpMethod_Returns400()
    {
        await using var factory = new DeltaWebApplicationFactory();
        await factory.InitializeAsync();
        var isId = Guid.NewGuid();
        factory.HandlerCallback = req => RouteFindThing(req, isId, "is")
            ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new
        {
            name = "MyEndpoint",
            properties = new { url = "https://x", httpMethod = "BREW" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Unsupported httpMethod");
    }

    // ---------- Default-endpoint resolution ----------

    [Fact]
    public async Task Handle_DefaultEndpointMissingAndCreateFails_Returns500()
    {
        await using var factory = new DeltaWebApplicationFactory();
        await factory.InitializeAsync();
        var isId = Guid.NewGuid();
        factory.HandlerCallback = req =>
        {
            // is-predicate found
            if (req.RequestUri!.AbsolutePath == "/api/things"
                && req.Method == HttpMethod.Get
                && req.RequestUri.Query.Contains("name=is"))
                return Json("{\"Id\":\"" + isId + "\",\"Name\":\"is\",\"Properties\":{}}");
            // Endpoint default lookup → null
            if (req.RequestUri.AbsolutePath == "/api/things"
                && req.Method == HttpMethod.Get
                && req.RequestUri.Query.Contains("name=Endpoint"))
                return Json("null");
            // Default Endpoint creation → fails (BadRequest)
            if (req.RequestUri.AbsolutePath == "/api/things" && req.Method == HttpMethod.Post)
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("nope", Encoding.UTF8, "text/plain")
                };
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", BasicValidBody());

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).Should().Contain("default Endpoint thing");
    }

    [Fact]
    public async Task Handle_RegisteredThingCreationFails_Returns500()
    {
        await using var factory = new DeltaWebApplicationFactory();
        await factory.InitializeAsync();
        var isId = Guid.NewGuid();
        var defaultId = Guid.NewGuid();
        var creationCount = 0;
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/things" && req.Method == HttpMethod.Get
                && req.RequestUri.Query.Contains("name=is"))
                return Json("{\"Id\":\"" + isId + "\",\"Name\":\"is\",\"Properties\":{}}");
            if (req.RequestUri.AbsolutePath == "/api/things" && req.Method == HttpMethod.Get
                && req.RequestUri.Query.Contains("name=Endpoint"))
                return Json("{\"Id\":\"" + defaultId + "\",\"Name\":\"Endpoint\",\"Properties\":{}}");
            // First POST = default-endpoint creation (NOT called here because lookup succeeds).
            // Real first POST = registered-thing creation → fail.
            if (req.RequestUri.AbsolutePath == "/api/things" && req.Method == HttpMethod.Post)
            {
                creationCount++;
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("rejected", Encoding.UTF8, "text/plain")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", BasicValidBody());

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Failed to create registered endpoint");
        creationCount.Should().Be(1);
    }

    // ---------- Relationship creation + compensation ----------

    [Fact]
    public async Task Handle_RelationshipCreationFails_CompensatesAndReturns500()
    {
        await using var factory = new DeltaWebApplicationFactory();
        await factory.InitializeAsync();
        var isId = Guid.NewGuid();
        var defaultId = Guid.NewGuid();
        var registeredId = Guid.NewGuid();
        var deleted = false;
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/things" && req.Method == HttpMethod.Get
                && req.RequestUri.Query.Contains("name=is"))
                return Json("{\"Id\":\"" + isId + "\",\"Name\":\"is\",\"Properties\":{}}");
            if (req.RequestUri.AbsolutePath == "/api/things" && req.Method == HttpMethod.Get
                && req.RequestUri.Query.Contains("name=Endpoint"))
                return Json("{\"Id\":\"" + defaultId + "\",\"Name\":\"Endpoint\",\"Properties\":{}}");
            // Registered-thing creation succeeds
            if (req.RequestUri.AbsolutePath == "/api/things" && req.Method == HttpMethod.Post)
                return Json("{\"Id\":\"" + registeredId + "\",\"Name\":\"MyEndpoint\",\"Properties\":{}}");
            // Relationship creation fails
            if (req.RequestUri.AbsolutePath == "/api/relationships" && req.Method == HttpMethod.Post)
                return new HttpResponseMessage(HttpStatusCode.Conflict);
            // CompensateAsync → delete the registered thing
            if (req.RequestUri.AbsolutePath == $"/api/things/{registeredId}" && req.Method == HttpMethod.Delete)
            {
                deleted = true;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", BasicValidBody());

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).Should().Contain("'is' relationship");
        deleted.Should().BeTrue("compensation must delete the orphaned registered thing");
    }

    [Fact]
    public async Task Handle_PropertySetFails_CompensatesAndReturns500()
    {
        await using var factory = new DeltaWebApplicationFactory();
        await factory.InitializeAsync();
        var isId = Guid.NewGuid();
        var defaultId = Guid.NewGuid();
        var registeredId = Guid.NewGuid();
        var deleted = false;
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
            // First SetThingProperty PUT → fail
            if (req.RequestUri.AbsolutePath == $"/api/things/{registeredId}/properties" && req.Method == HttpMethod.Put)
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            if (req.RequestUri.AbsolutePath == $"/api/things/{registeredId}" && req.Method == HttpMethod.Delete)
            {
                deleted = true;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", BasicValidBody());

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Failed to set property");
        deleted.Should().BeTrue();
    }

    // ---------- Happy path ----------

    [Fact]
    public async Task Handle_HappyPath_CreatesEverythingAndReturns200()
    {
        await using var factory = new DeltaWebApplicationFactory();
        await factory.InitializeAsync();
        var isId = Guid.NewGuid();
        var defaultId = Guid.NewGuid();
        var registeredId = Guid.NewGuid();
        var propertySets = new List<string>();
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
            {
                var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
                propertySets.Add(body);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", BasicValidBody());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"success\":true");
        body.Should().Contain(registeredId.ToString());
        body.Should().Contain(defaultId.ToString());
        body.Should().Contain(isId.ToString());
        propertySets.Should().HaveCount(2, "one PUT per user-supplied property in BasicValidBody");
        propertySets.Should().Contain(s => s.Contains("\"name\":\"url\""));
        propertySets.Should().Contain(s => s.Contains("\"name\":\"httpMethod\""));
    }

    [Fact]
    public async Task Handle_DefaultEndpointMissingButCreatedSucceeds_Returns200()
    {
        // First Endpoint lookup is null → fall through to CreateThingAsync seeded with
        // endpointSeed.Properties → that returns the new default → then continue.
        await using var factory = new DeltaWebApplicationFactory();
        await factory.InitializeAsync();
        var isId = Guid.NewGuid();
        var defaultId = Guid.NewGuid();
        var registeredId = Guid.NewGuid();
        var createCount = 0;
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/things" && req.Method == HttpMethod.Get
                && req.RequestUri.Query.Contains("name=is"))
                return Json("{\"Id\":\"" + isId + "\",\"Name\":\"is\",\"Properties\":{}}");
            if (req.RequestUri.AbsolutePath == "/api/things" && req.Method == HttpMethod.Get
                && req.RequestUri.Query.Contains("name=Endpoint"))
                return Json("null");  // default not found, must create
            if (req.RequestUri.AbsolutePath == "/api/things" && req.Method == HttpMethod.Post)
            {
                createCount++;
                // First POST = default Endpoint creation, second = registered thing.
                var id = createCount == 1 ? defaultId : registeredId;
                var name = createCount == 1 ? "Endpoint" : "MyEndpoint";
                return Json("{\"Id\":\"" + id + "\",\"Name\":\"" + name + "\",\"Properties\":{}}");
            }
            if (req.RequestUri.AbsolutePath == "/api/relationships" && req.Method == HttpMethod.Post)
                return new HttpResponseMessage(HttpStatusCode.Created);
            if (req.RequestUri.AbsolutePath == $"/api/things/{registeredId}/properties" && req.Method == HttpMethod.Put)
                return new HttpResponseMessage(HttpStatusCode.OK);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", BasicValidBody());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        createCount.Should().Be(2, "one POST for default Endpoint + one POST for the registered thing");
    }

    // ---------- /register alias ----------

    [Fact]
    public async Task Register_AliasUsesSameLogic_ReturnsSameShape()
    {
        // /register is registered second in Program.cs but delegates to the same helper.
        // One test is enough to pin the alias is wired up; the full decision tree is
        // already exercised via /handle.
        await using var factory = new DeltaWebApplicationFactory();
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
                return Json("{\"Id\":\"" + registeredId + "\",\"Name\":\"MyEndpoint\",\"Properties\":{}}");
            if (req.RequestUri.AbsolutePath == "/api/relationships" && req.Method == HttpMethod.Post)
                return new HttpResponseMessage(HttpStatusCode.Created);
            if (req.RequestUri.AbsolutePath == $"/api/things/{registeredId}/properties" && req.Method == HttpMethod.Put)
                return new HttpResponseMessage(HttpStatusCode.OK);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/register", BasicValidBody());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"success\":true");
    }

    // ---------- /health and /shutdown ----------

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        await using var factory = new DeltaWebApplicationFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"status\":\"Healthy\"");
        body.Should().Contain("\"service\":\"Delta\"");
    }

    [Fact]
    public async Task Shutdown_Returns200WithMessage()
    {
        await using var factory = new DeltaWebApplicationFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/shutdown", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Shutting down Delta");
    }

    // ---------- Helpers ----------

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage? RouteFindThing(HttpRequestMessage req, Guid id, string name)
    {
        if (req.Method == HttpMethod.Get
            && req.RequestUri!.AbsolutePath == "/api/things"
            && req.RequestUri.Query.Contains($"name={name}"))
            return Json("{\"Id\":\"" + id + "\",\"Name\":\"" + name + "\",\"Properties\":{}}");
        return null;
    }

    private static object BasicValidBody() => new
    {
        name = "MyEndpoint",
        properties = new { url = "https://api.example/x", httpMethod = "GET" }
    };
}
