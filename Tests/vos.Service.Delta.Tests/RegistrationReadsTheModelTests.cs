using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using vos.Auth.Shared;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Delta.Tests;

public class RegistrationReadsTheModelTests
{
    private const string Issuer = "VillageOS";
    private const string Audience = "delta-handler";

    private static readonly MyceliumSigner Mycelium = new();

    private static readonly Guid FirstModel = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondModel = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task A_registration_under_a_template_the_model_holds_is_wired_to_that_template()
    {
        var model = new EndpointModelStandIn();
        var (_, child) = model.SeedTree(FirstModel);

        var response = await RegisterAsync(model, FirstModel, Registration("county-parcels", "EsriEndpoint",
            new { url = "https://gis.example/parcels", layer = "4" }));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var registered = model.IdOf(FirstModel, "county-parcels")!.Value;
        model.TargetOfIs(FirstModel, registered).Should().Be(child);
        model.ThingsCreated.Select(created => created.Name).Should().Equal("county-parcels");
    }

    [Fact]
    public async Task A_registration_stating_no_method_takes_the_one_its_template_narrows()
    {
        var model = new EndpointModelStandIn();
        var (root, _) = model.SeedTree(FirstModel);
        model.SetProperty(FirstModel, root, "httpMethod", "");

        var response = await RegisterAsync(model, FirstModel, Registration("county-parcels", "EsriEndpoint",
            new { url = "https://gis.example/parcels" }));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_registration_stating_a_key_no_template_above_it_declares_is_refused()
    {
        var model = new EndpointModelStandIn();
        model.SeedTree(FirstModel);

        var response = await RegisterAsync(model, FirstModel, Registration("county-parcels", "EsriEndpoint",
            new { url = "https://gis.example/parcels", colour = "red" }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("colour");
        model.ThingsCreated.Should().BeEmpty();
    }

    [Fact]
    public async Task A_template_added_to_a_live_model_is_used_by_the_next_registration()
    {
        var model = new EndpointModelStandIn();
        var (root, _) = model.SeedTree(FirstModel);
        await using var factory = FactoryOver(model);
        using var client = ClientFor(factory, FirstModel);
        (await client.PostAsJsonAsync("/handle", Registration("county-parcels", "EsriEndpoint",
            new { url = "https://gis.example/parcels" }))).StatusCode.Should().Be(HttpStatusCode.OK);

        var imagery = model.Declare(FirstModel, "ImageryEndpoint", isArchetype: true,
            new Dictionary<string, string?> { ["band"] = "" });
        model.Relate(FirstModel, imagery, "is", root);
        var response = await client.PostAsJsonAsync("/handle", Registration("infrared", "ImageryEndpoint",
            new { url = "https://imagery.example/tiles", band = "4" }));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        model.TargetOfIs(FirstModel, model.IdOf(FirstModel, "infrared")!.Value).Should().Be(imagery);
    }

    [Fact]
    public async Task A_key_added_to_a_live_template_is_admitted_by_the_next_registration()
    {
        var model = new EndpointModelStandIn();
        var (_, child) = model.SeedTree(FirstModel);
        await using var factory = FactoryOver(model);
        using var client = ClientFor(factory, FirstModel);
        (await client.PostAsJsonAsync("/handle", Registration("county-parcels", "EsriEndpoint",
            new { url = "https://gis.example/parcels" }))).StatusCode.Should().Be(HttpStatusCode.OK);

        model.SetProperty(FirstModel, child, "pageSize", "");
        var response = await client.PostAsJsonAsync("/handle", Registration("county-roads", "EsriEndpoint",
            new { url = "https://gis.example/roads", pageSize = "500" }));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_model_that_binds_nothing_to_the_endpoint_role_is_refused_naming_the_role()
    {
        var model = new EndpointModelStandIn();
        model.Declare(FirstModel, "Endpoint", isArchetype: true,
            new Dictionary<string, string?> { ["url"] = "", ["httpMethod"] = "GET" });

        var response = await RegisterAsync(model, FirstModel, Registration("county-parcels", template: null,
            new { url = "https://gis.example/parcels" }));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("endpoint role");
        model.ThingsCreated.Should().BeEmpty();
    }

    [Fact]
    public async Task A_template_that_does_not_descend_from_the_bound_root_is_refused()
    {
        var model = new EndpointModelStandIn();
        model.SeedTree(FirstModel);
        model.Declare(FirstModel, "Elsewhere", isArchetype: true, new Dictionary<string, string?> { ["url"] = "" });

        var response = await RegisterAsync(model, FirstModel, Registration("county-parcels", "Elsewhere",
            new { url = "https://gis.example/parcels" }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("descend");
        model.ThingsCreated.Should().BeEmpty();
    }

    [Fact]
    public async Task Another_registration_is_not_a_template_a_registration_can_descend_from()
    {
        var model = new EndpointModelStandIn();
        var (root, _) = model.SeedTree(FirstModel);
        var existing = model.Declare(FirstModel, "county-parcels", properties: new Dictionary<string, string?> { ["url"] = "https://gis.example/parcels" });
        model.Relate(FirstModel, existing, "is", root);

        var response = await RegisterAsync(model, FirstModel, Registration("county-roads", "county-parcels",
            new { url = "https://gis.example/roads" }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        model.ThingsCreated.Should().BeEmpty();
    }

    [Fact]
    public async Task A_template_name_two_templates_share_is_refused_naming_both()
    {
        var model = new EndpointModelStandIn();
        var (root, _) = model.SeedTree(FirstModel);
        var first = model.Declare(FirstModel, "Twin", isArchetype: true);
        var second = model.Declare(FirstModel, "Twin", isArchetype: true);
        model.Relate(FirstModel, first, "is", root);
        model.Relate(FirstModel, second, "is", root);

        var response = await RegisterAsync(model, FirstModel, Registration("county-parcels", "Twin",
            new { url = "https://gis.example/parcels" }));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(first.ToString()).And.Contain(second.ToString());
        model.ThingsCreated.Should().BeEmpty();
    }

    [Fact]
    public async Task A_registration_from_a_second_model_is_checked_against_that_models_tree()
    {
        var model = new EndpointModelStandIn();
        model.SeedTree(FirstModel);
        var secondRoot = model.Declare(SecondModel, "Endpoint", isArchetype: true,
            new Dictionary<string, string?> { ["url"] = "", ["httpMethod"] = "GET" });
        var secondChild = model.Declare(SecondModel, "EsriEndpoint", isArchetype: true,
            new Dictionary<string, string?> { ["band"] = "" });
        model.Relate(SecondModel, secondChild, "is", secondRoot);
        model.BindEndpointRole(SecondModel, secondRoot);
        await using var factory = FactoryOver(model);
        using var client = ClientFor(factory, SecondModel);

        var stillFirstModelsKey = await client.PostAsJsonAsync("/handle", Registration("county-parcels", "EsriEndpoint",
            new { url = "https://gis.example/parcels", layer = "4" }));
        var secondModelsKey = await client.PostAsJsonAsync("/handle", Registration("county-parcels", "EsriEndpoint",
            new { url = "https://gis.example/parcels", band = "4" }));

        stillFirstModelsKey.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        secondModelsKey.StatusCode.Should().Be(HttpStatusCode.OK, await secondModelsKey.Content.ReadAsStringAsync());
        model.RelationshipsCreated.Should().ContainSingle()
            .Which.Should().Be((SecondModel, model.IdOf(SecondModel, "county-parcels")!.Value,
                model.IsPredicate(SecondModel), secondChild));
    }

    [Fact]
    public async Task Reading_the_tree_releases_the_subscription_it_opened()
    {
        var model = new EndpointModelStandIn();
        model.SeedTree(FirstModel);

        await RegisterAsync(model, FirstModel, Registration("county-parcels", "EsriEndpoint",
            new { url = "https://gis.example/parcels" }));

        model.SubscriptionsOpened.Should().NotBeEmpty();
        model.SubscriptionsClosed.Should().BeEquivalentTo(model.SubscriptionsOpened);
    }

    [Fact]
    public async Task Delta_starts_with_no_template_file_beside_it()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Port", "5000");
            builder.UseSetting("MyceliumUrl", "http://localhost");
            builder.UseSetting("Token", "test-token");
        });
        File.Exists(Path.Combine(AppContext.BaseDirectory, "seed.json")).Should().BeFalse();

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<HttpResponseMessage> RegisterAsync(EndpointModelStandIn model, Guid modelId, object registration)
    {
        await using var factory = FactoryOver(model);
        using var client = ClientFor(factory, modelId);
        return await client.PostAsJsonAsync("/handle", registration);
    }

    private static DeltaWebApplicationFactory FactoryOver(EndpointModelStandIn model) => new()
    {
        VerificationKey = Mycelium.VerificationKey,
        Issuer = Issuer,
        Audience = Audience,
        HandlerCallback = model.Route,
    };

    private static HttpClient ClientFor(DeltaWebApplicationFactory factory, Guid modelId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            Mycelium.Token(Issuer, Audience, new Claim(VosClaims.ModelId, modelId.ToString())));
        return client;
    }

    private static object Registration(string name, string? template, object properties) => new
    {
        name,
        properties,
        relationships = template is null
            ? Array.Empty<object>()
            : new object[] { new { subject = name, predicate = "is", target = template } },
    };
}
