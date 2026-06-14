using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Xunit;

namespace vos.ManagedMicroservice.Delta.Tests;

// Tests for the /handle (and /register alias) endpoint in
// vos.ManagedMicroservice.Delta/Program.cs. Each test wires a per-scenario
// HandlerCallback on the factory so Mycelium calls all resolve through the same handler.
//
// Test naming convention: most tests target /handle. The /register alias delegates to the
// same HandleRegisterEndpointRequestAsync helper, so we cover it with a single shared-routing
// test rather than duplicating the entire decision tree.
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
        // Override the default seed with a model whose root thing has empty properties.
        await using var factory = new DeltaWebApplicationFactory
        {
            SeedJson = """{"things":[{"name":"Endpoint","properties":{}}],"relationships":[]}"""
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
    public async Task Handle_MissingHttpMethod_InheritsFromSeed_Returns200()
    {
        // Task #5467: httpMethod is inheritable. The default seed's Endpoint declares httpMethod=GET,
        // so a request omitting httpMethod registers successfully against the effective value.
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
                propertySets.Add(req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "");
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new
        {
            name = "MyEndpoint",
            properties = new { url = "https://x.example/" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // Inherited values are not materialized: only the user-supplied url is set on the thing.
        propertySets.Should().ContainSingle().Which.Should().Contain("\"name\":\"url\"");
        propertySets.Should().NotContain(s => s.Contains("\"name\":\"httpMethod\""));
    }

    [Fact]
    public async Task Handle_NoEffectiveHttpMethod_Returns400()
    {
        // Seed without an httpMethod anywhere in the chain -> nothing to inherit -> still 400.
        await using var factory = new DeltaWebApplicationFactory
        {
            SeedJson = """{"things":[{"name":"Endpoint","properties":{"url":""}}],"relationships":[]}"""
        };
        await factory.InitializeAsync();
        var isId = Guid.NewGuid();
        factory.HandlerCallback = req => RouteFindThing(req, isId, "is")
            ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new
        {
            name = "MyEndpoint",
            properties = new { url = "https://x.example/" }
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

    // ---------- Template resolution (provisioned at boot, Task #5468) ----------

    [Fact]
    public async Task Handle_TemplateNotProvisioned_Returns500()
    {
        // Boot-time provisioning (Task #5468) creates the template things; the handler only resolves
        // them, never creates them. If the nominated template is absent, registration fails fast —
        // the handler makes no thing-creation POST for the template.
        await using var factory = new DeltaWebApplicationFactory();
        await factory.InitializeAsync();
        var isId = Guid.NewGuid();
        var templatePosted = false;
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/things"
                && req.Method == HttpMethod.Get
                && req.RequestUri.Query.Contains("name=is"))
                return Json("{\"Id\":\"" + isId + "\",\"Name\":\"is\",\"Properties\":{}}");
            if (req.RequestUri.AbsolutePath == "/api/things" && req.Method == HttpMethod.Post)
                templatePosted = true;
            // Template lookup → null (not provisioned).
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", BasicValidBody());

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).Should().Contain("not provisioned");
        templatePosted.Should().BeFalse("the handler must not lazily create the template");
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
            // Template is pre-provisioned at boot (Task #5468); its lookup succeeds, so the only
            // POST here is the registered-thing creation → fail.
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

    // ---------- Template selection (Task #5467) ----------

    [Fact]
    public async Task Handle_UnknownTemplate_Returns400WithDescentMessage()
    {
        // A template not in the closed-set graph cannot descend from the root -> rejected, no mycelium call.
        await using var factory = new DeltaWebApplicationFactory();
        await factory.InitializeAsync();
        var isId = Guid.NewGuid();
        factory.HandlerCallback = req => RouteFindThing(req, isId, "is")
            ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new
        {
            name = "MyEndpoint",
            properties = new { url = "https://x.example/", httpMethod = "GET" },
            relationships = new[] { new { subject = "MyEndpoint", predicate = "is", target = "Ghost" } }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Ghost");
        body.Should().Contain("descend");
    }

    [Fact]
    public async Task Handle_MultipleIsRelationships_Returns400()
    {
        // A thing has at most one parent; two `is` rows are ambiguous and rejected before any mycelium call.
        await using var factory = new DeltaWebApplicationFactory();
        await factory.InitializeAsync();
        var isId = Guid.NewGuid();
        factory.HandlerCallback = req => RouteFindThing(req, isId, "is")
            ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new
        {
            name = "MyEndpoint",
            properties = new { url = "https://x.example/", httpMethod = "GET" },
            relationships = new[]
            {
                new { subject = "MyEndpoint", predicate = "is", target = "Endpoint" },
                new { subject = "MyEndpoint", predicate = "is", target = "Endpoint" }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("more than one 'is'");
    }

    [Fact]
    public async Task Handle_NominatedTemplate_LinksToThatTemplateAndInheritsMethod_Returns200()
    {
        // EsriEndpoint is Endpoint(root). Registering under EsriEndpoint must (a) allow EsriEndpoint's
        // own key (layer), (b) inherit httpMethod=GET from the root, (c) link `is` -> the EsriEndpoint
        // thing, not the root.
        await using var factory = new DeltaWebApplicationFactory
        {
            SeedJson = """
            {
              "things": [
                { "name": "Endpoint", "properties": { "url": "", "httpMethod": "GET", "responseTransform": "$" } },
                { "name": "EsriEndpoint", "properties": { "layer": "" } }
              ],
              "relationships": [ { "subject": "EsriEndpoint", "predicate": "is", "target": "Endpoint" } ]
            }
            """
        };
        await factory.InitializeAsync();
        var isId = Guid.NewGuid();
        var esriId = Guid.NewGuid();
        var registeredId = Guid.NewGuid();
        string? relationshipBody = null;
        var propertySets = new List<string>();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/things" && req.Method == HttpMethod.Get
                && req.RequestUri.Query.Contains("name=is"))
                return Json("{\"Id\":\"" + isId + "\",\"Name\":\"is\",\"Properties\":{}}");
            if (req.RequestUri.AbsolutePath == "/api/things" && req.Method == HttpMethod.Get
                && req.RequestUri.Query.Contains("name=EsriEndpoint"))
                return Json("{\"Id\":\"" + esriId + "\",\"Name\":\"EsriEndpoint\",\"Properties\":{}}");
            if (req.RequestUri.AbsolutePath == "/api/things" && req.Method == HttpMethod.Post)
                return Json("{\"Id\":\"" + registeredId + "\",\"Name\":\"MyEsri\",\"Properties\":{}}");
            if (req.RequestUri.AbsolutePath == "/api/relationships" && req.Method == HttpMethod.Post)
            {
                relationshipBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.Created);
            }
            if (req.RequestUri.AbsolutePath == $"/api/things/{registeredId}/properties" && req.Method == HttpMethod.Put)
            {
                propertySets.Add(req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "");
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new
        {
            name = "MyEsri",
            properties = new { url = "https://x.example/", layer = "3" },
            relationships = new[] { new { subject = "MyEsri", predicate = "is", target = "EsriEndpoint" } }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        relationshipBody.Should().NotBeNull();
        relationshipBody!.Should().Contain(esriId.ToString(), "registered thing must link `is` -> the nominated template");
        // EsriEndpoint's own key (layer) and the inherited-but-overridden url are user-supplied; httpMethod is inherited.
        propertySets.Should().HaveCount(2);
        propertySets.Should().Contain(s => s.Contains("\"name\":\"layer\""));
        propertySets.Should().NotContain(s => s.Contains("\"name\":\"httpMethod\""));
    }

    [Fact]
    public async Task Handle_TemplateDeclaringBaseCapabilityKeys_AcceptsRegistrationUsingThem()
    {
        // Cross-service consistency with Tributary's base request capabilities (Task #5469): once a
        // template declares headers/queryParams/requestContentType/timeout as structural keys, Delta's
        // AllowedKeys whitelist admits a registration that supplies them. No Delta code change is
        // needed — this is ordinary seed authoring, exactly like url/httpMethod/responseTransform.
        await using var factory = new DeltaWebApplicationFactory
        {
            SeedJson = """
            {
              "things": [
                { "name": "Endpoint", "properties": {
                    "url": "", "httpMethod": "GET",
                    "headers": "", "queryParams": "", "requestContentType": "", "timeout": ""
                } }
              ],
              "relationships": []
            }
            """
        };
        await factory.InitializeAsync();
        var isId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();
        var registeredId = Guid.NewGuid();
        var propertySets = new List<string>();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/things" && req.Method == HttpMethod.Get
                && req.RequestUri.Query.Contains("name=is"))
                return Json("{\"Id\":\"" + isId + "\",\"Name\":\"is\",\"Properties\":{}}");
            if (req.RequestUri.AbsolutePath == "/api/things" && req.Method == HttpMethod.Get
                && req.RequestUri.Query.Contains("name=Endpoint"))
                return Json("{\"Id\":\"" + endpointId + "\",\"Name\":\"Endpoint\",\"Properties\":{}}");
            if (req.RequestUri.AbsolutePath == "/api/things" && req.Method == HttpMethod.Post)
                return Json("{\"Id\":\"" + registeredId + "\",\"Name\":\"MyEndpoint\",\"Properties\":{}}");
            if (req.RequestUri.AbsolutePath == "/api/relationships" && req.Method == HttpMethod.Post)
                return new HttpResponseMessage(HttpStatusCode.Created);
            if (req.RequestUri.AbsolutePath == $"/api/things/{registeredId}/properties" && req.Method == HttpMethod.Put)
            {
                propertySets.Add(req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "");
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new
        {
            name = "MyEndpoint",
            properties = new Dictionary<string, object>
            {
                ["url"] = "https://api.example/x",
                ["httpMethod"] = "GET",
                ["headers"] = new { Authorization = "Bearer t" },
                ["queryParams"] = new { f = "json" },
                ["requestContentType"] = "application/json",
                ["timeout"] = 15
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the template declares these keys, so they are admissible");
        propertySets.Should().Contain(s => s.Contains("\"name\":\"headers\""));
        propertySets.Should().Contain(s => s.Contains("\"name\":\"queryParams\""));
        propertySets.Should().Contain(s => s.Contains("\"name\":\"timeout\""));
    }

    // ---------- Boot provisioning isolation (Task #5468) ----------

    [Fact]
    public async Task Boot_UnderTestingEnvironment_DoesNotProvisionTemplates()
    {
        // The boot-time TemplateCatalogProvisioner is gated on app.Environment != "Testing", so
        // building the host under the WebApplicationFactory must make zero mycelium calls at startup.
        // A regression that ran provisioning in tests would pollute every other test's request flow.
        await using var factory = new DeltaWebApplicationFactory();
        await factory.InitializeAsync();
        var myceliumCalls = 0;
        factory.HandlerCallback = _ =>
        {
            Interlocked.Increment(ref myceliumCalls);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        using var client = factory.CreateClient();
        await Task.Delay(100); // give any (erroneously-registered) startup hook a chance to fire

        myceliumCalls.Should().Be(0, "boot-time provisioning must not run under the Testing environment");
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
