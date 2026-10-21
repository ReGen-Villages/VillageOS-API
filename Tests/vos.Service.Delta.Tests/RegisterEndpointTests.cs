using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace vos.Service.Delta.Tests;

// The /register alias delegates to the same handler, so one test pins its wiring and the rest go through /handle.
public class RegisterEndpointTests
{
    private static readonly Guid Model = Guid.Empty;

    [Fact]
    public async Task Handle_MissingName_Returns400()
    {
        var (response, model) = await HandleAsync(new { properties = new { url = "https://x.example/" } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        model.SubscriptionsOpened.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_MissingProperties_Returns400()
    {
        var (response, _) = await HandleAsync(new { name = "MyEndpoint" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Handle_MultipleIsRelationships_Returns400BeforeReadingTheModel()
    {
        var (response, model) = await HandleAsync(new
        {
            name = "MyEndpoint",
            properties = new { url = "https://x.example/" },
            relationships = new[]
            {
                new { subject = "MyEndpoint", predicate = "is", target = "Endpoint" },
                new { subject = "MyEndpoint", predicate = "is", target = "Endpoint" },
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("more than one 'is'");
        model.SubscriptionsOpened.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_MissingUrl_Returns400BeforeReadingTheModel()
    {
        var (response, model) = await HandleAsync(new { name = "MyEndpoint", properties = new { httpMethod = "GET" } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        model.SubscriptionsOpened.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_InvalidUri_Returns400()
    {
        var (response, _) = await HandleAsync(new { name = "MyEndpoint", properties = new { url = "not a url" } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Invalid endpoint url");
    }

    [Fact]
    public async Task Handle_UnsupportedHttpMethod_Returns400()
    {
        var (response, _) = await HandleAsync(new
        {
            name = "MyEndpoint",
            properties = new { url = "https://x.example/", httpMethod = "TELEPORT" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Unsupported httpMethod");
    }

    [Fact]
    public async Task Handle_UnsupportedProperties_Returns400NamingEach()
    {
        var (response, model) = await HandleAsync(new
        {
            name = "MyEndpoint",
            properties = new { url = "https://x.example/", colour = "red", size = "large" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("colour").And.Contain("size");
        model.ThingsCreated.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NoMethodStated_TakesTheTemplates()
    {
        var (response, model) = await HandleAsync(new { name = "MyEndpoint", properties = new { url = "https://x.example/" } });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        model.PropertiesSet.Select(set => set.Name).Should().Equal("url");
    }

    [Fact]
    public async Task Handle_NoMethodStatedOrDeclared_Returns400()
    {
        var model = new EndpointModelStandIn();
        var root = model.Declare(Model, "Endpoint", isArchetype: true, new Dictionary<string, string?> { ["url"] = "" });
        model.BindEndpointRole(Model, root);

        var response = await HandleAsync(model, new { name = "MyEndpoint", properties = new { url = "https://x.example/" } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("httpMethod");
    }

    [Fact]
    public async Task Handle_TemplateDeclaringNoProperties_Returns500()
    {
        var model = new EndpointModelStandIn();
        model.BindEndpointRole(Model, model.Declare(Model, "Endpoint", isArchetype: true));

        var response = await HandleAsync(model, new { name = "MyEndpoint", properties = new { url = "https://x.example/" } });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        model.ThingsCreated.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_UnknownTemplate_Returns400WithDescentMessage()
    {
        var (response, model) = await HandleAsync(Under("Ghost"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Ghost").And.Contain("descend");
        model.ThingsCreated.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_TheModelCannotBeRead_Returns500AndWritesNothing()
    {
        var model = Seeded();
        model.RefuseSubscription = true;

        var response = await HandleAsync(model, Under("EsriEndpoint"));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        model.ThingsCreated.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_TheTemplatesPropertiesCannotBeRead_Returns500AndWritesNothing()
    {
        var model = Seeded();
        model.RefusePropertyRead = true;

        var response = await HandleAsync(model, Under("EsriEndpoint"));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).Should().Contain("EsriEndpoint");
        model.ThingsCreated.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_TheSubscriptionCannotBeReleased_StillRegisters()
    {
        var model = Seeded();
        model.DropSubscriptionRelease = true;

        var response = await HandleAsync(model, Under("EsriEndpoint"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Handle_RegisteredThingCreationFails_Returns500()
    {
        var model = Seeded();
        model.RefuseThingCreate = true;

        var response = await HandleAsync(model, Under("EsriEndpoint"));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        model.RelationshipsCreated.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_RelationshipCreationFails_CompensatesAndReturns500()
    {
        var model = Seeded();
        model.RefuseRelationshipCreate = true;

        var response = await HandleAsync(model, Under("EsriEndpoint"));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        model.ThingsDeleted.Should().ContainSingle();
        model.IdOf(Model, "MyEndpoint").Should().BeNull();
    }

    [Fact]
    public async Task Handle_PropertySetFails_CompensatesAndReturns500()
    {
        var model = Seeded();
        model.RefusePropertySet = true;

        var response = await HandleAsync(model, Under("EsriEndpoint"));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        model.ThingsDeleted.Should().ContainSingle();
        model.IdOf(Model, "MyEndpoint").Should().BeNull();
    }

    [Fact]
    public async Task Handle_HappyPath_ReportsWhatItWroteAndSetsOnlyTheSuppliedProperties()
    {
        var model = Seeded();

        var response = await HandleAsync(model, Under("EsriEndpoint", new { url = "https://x.example/", layer = "4" }));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"success\":true")
            .And.Contain(model.IdOf(Model, "MyEndpoint")!.Value.ToString())
            .And.Contain(model.IdOf(Model, "EsriEndpoint")!.Value.ToString())
            .And.Contain(model.IsPredicate(Model).ToString());
        model.PropertiesSet.Select(set => set.Name).Should().BeEquivalentTo("url", "layer");
    }

    [Fact]
    public async Task Handle_KeysInAnyCase_AreAdmittedAndRead()
    {
        var model = Seeded();

        var response = await HandleAsync(model, new
        {
            name = "MyEndpoint",
            properties = new Dictionary<string, object> { ["URL"] = "https://x.example/", ["HTTPMethod"] = "GET" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Register_AliasUsesSameLogic_ReturnsSameShape()
    {
        var model = Seeded();
        await using var factory = new DeltaWebApplicationFactory { HandlerCallback = model.Route };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/register", Under("EsriEndpoint"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"success\":true");
    }

    [Fact]
    public async Task Boot_BeforeAnyRegistration_TouchesNoModel()
    {
        // A startup call could only be aimed at the model Delta's launch token names, which is the one
        // project a Delta shared by every project must not favour.
        var myceliumCalls = 0;
        await using var factory = new DeltaWebApplicationFactory
        {
            HandlerCallback = _ =>
            {
                Interlocked.Increment(ref myceliumCalls);
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            },
        };
        using var client = factory.CreateClient();

        (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);

        myceliumCalls.Should().Be(0);
    }

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        await using var factory = new DeltaWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"status\":\"Healthy\"").And.Contain("\"service\":\"Delta\"");
    }

    [Fact]
    public async Task Shutdown_Returns200WithMessage()
    {
        await using var factory = new DeltaWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/shutdown", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Shutting down Delta");
    }

    private static EndpointModelStandIn Seeded()
    {
        var model = new EndpointModelStandIn();
        model.SeedTree(Model);
        return model;
    }

    private static async Task<(HttpResponseMessage Response, EndpointModelStandIn Model)> HandleAsync(object body)
    {
        var model = Seeded();
        return (await HandleAsync(model, body), model);
    }

    private static async Task<HttpResponseMessage> HandleAsync(EndpointModelStandIn model, object body)
    {
        await using var factory = new DeltaWebApplicationFactory { HandlerCallback = model.Route };
        using var client = factory.CreateClient();
        return await client.PostAsJsonAsync("/handle", body);
    }

    private static object Under(string template, object? properties = null) => new
    {
        name = "MyEndpoint",
        properties = properties ?? new { url = "https://x.example/" },
        relationships = new[] { new { subject = "MyEndpoint", predicate = "is", target = template } },
    };
}
