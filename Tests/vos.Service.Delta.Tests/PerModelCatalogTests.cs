using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using vos.Auth.Shared;
using Xunit;

namespace vos.Service.Delta.Tests;

// One Delta process answers every project, and a registration is written to the model of whoever
// called. These cover the catalog following the caller rather than the token Delta was launched with,
// and the edges that follow from provisioning being something a request does: doing it once per model
// however many requests arrive together, not doing it for a request that is about to be refused, and
// noticing when what was provisioned has since gone.
public class PerModelCatalogTests
{
    private const string Issuer = "VillageOS";
    private const string Audience = "VosClients";
    private const string SigningKey = "a-signing-key-long-enough-for-hmac-sha256";

    private static readonly Guid FirstModel = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondModel = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private const string TwoTemplateSeed = """
    {
      "things": [
        { "name": "Endpoint", "properties": { "url": "", "httpMethod": "GET" } },
        { "name": "EsriEndpoint", "properties": { "layer": "" } }
      ],
      "relationships": [ { "subject": "EsriEndpoint", "predicate": "is", "target": "Endpoint" } ]
    }
    """;

    [Fact]
    public async Task Handle_FromAModelHoldingNoTemplates_ProvisionsThemIntoThatModelAndRegisters()
    {
        var mycelium = new ModelPartitionedMycelium();
        await using var factory = FactoryOver(mycelium);
        await factory.InitializeAsync();
        using var client = ClientFor(factory, FirstModel);

        var response = await client.PostAsJsonAsync("/handle", RegistrationUnder("EsriEndpoint"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        mycelium.IdOf(FirstModel, "EsriEndpoint").Should().NotBeNull();
        mycelium.IdOf(FirstModel, "Endpoint").Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_FromASecondModel_ProvisionsThatModelItsOwnTemplates()
    {
        var mycelium = new ModelPartitionedMycelium();
        await using var factory = FactoryOver(mycelium);
        await factory.InitializeAsync();

        using (var first = ClientFor(factory, FirstModel))
            await first.PostAsJsonAsync("/handle", RegistrationUnder("EsriEndpoint"));

        using var second = ClientFor(factory, SecondModel);
        var response = await second.PostAsJsonAsync("/handle", RegistrationUnder("EsriEndpoint"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var inSecondModel = mycelium.IdOf(SecondModel, "EsriEndpoint");
        inSecondModel.Should().NotBeNull();
        inSecondModel!.Value.Should().NotBe(mycelium.IdOf(FirstModel, "EsriEndpoint")!.Value);
    }

    [Fact]
    public async Task Handle_FromASecondModel_WiresTheRegistrationToThatModelsTemplate()
    {
        var mycelium = new ModelPartitionedMycelium();
        await using var factory = FactoryOver(mycelium);
        await factory.InitializeAsync();

        using (var first = ClientFor(factory, FirstModel))
            await first.PostAsJsonAsync("/handle", RegistrationUnder("EsriEndpoint"));

        using var second = ClientFor(factory, SecondModel);
        await second.PostAsJsonAsync("/handle", RegistrationUnder("EsriEndpoint"));

        var wire = mycelium.RelationshipsCreated.Last(created => created.Model == SecondModel);
        wire.Target.Should().Be(mycelium.IdOf(SecondModel, "EsriEndpoint")!.Value);
        wire.Predicate.Should().Be(mycelium.IdOf(SecondModel, "is")!.Value);
    }

    [Fact]
    public async Task Handle_TwiceFromOneModel_CreatesItsTemplatesOnce()
    {
        var mycelium = new ModelPartitionedMycelium();
        await using var factory = FactoryOver(mycelium);
        await factory.InitializeAsync();
        using var client = ClientFor(factory, FirstModel);

        await client.PostAsJsonAsync("/handle", RegistrationUnder("EsriEndpoint"));
        await client.PostAsJsonAsync("/handle", RegistrationUnder("EsriEndpoint", name: "SecondEndpoint"));

        mycelium.ThingsCreated.Count(created => created is { Model: var m, Name: "EsriEndpoint" } && m == FirstModel)
            .Should().Be(1);
    }

    [Fact]
    public async Task Handle_TwiceFromOneModel_LooksNothingUpByNameTheSecondTime()
    {
        var mycelium = new ModelPartitionedMycelium();
        await using var factory = FactoryOver(mycelium);
        await factory.InitializeAsync();
        using var client = ClientFor(factory, FirstModel);

        await client.PostAsJsonAsync("/handle", RegistrationUnder("EsriEndpoint"));
        var lookupsAfterProvisioning = mycelium.NameLookups.Count;
        await client.PostAsJsonAsync("/handle", RegistrationUnder("EsriEndpoint", name: "SecondEndpoint"));

        mycelium.NameLookups.Count.Should().Be(lookupsAfterProvisioning);
    }

    [Fact]
    public async Task Handle_RefusedRegistration_ProvisionsNothingIntoTheCallersModel()
    {
        var mycelium = new ModelPartitionedMycelium();
        await using var factory = FactoryOver(mycelium);
        await factory.InitializeAsync();
        using var client = ClientFor(factory, FirstModel);

        var response = await client.PostAsJsonAsync("/handle", new
        {
            name = "MyEndpoint",
            properties = new { url = "https://api.example/x", httpMethod = "GET", notAllowed = "boom" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        mycelium.ThingsCreated.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_TwoCallsAtOnceFromAModelNobodyHasServed_CreatesItsTemplatesOnce()
    {
        var mycelium = new ModelPartitionedMycelium();
        await using var factory = FactoryOver(mycelium, answerConcurrently: true);
        await factory.InitializeAsync();
        using var first = ClientFor(factory, FirstModel);
        using var second = ClientFor(factory, FirstModel);

        await Task.WhenAll(
            first.PostAsJsonAsync("/handle", RegistrationUnder("EsriEndpoint")),
            second.PostAsJsonAsync("/handle", RegistrationUnder("EsriEndpoint", name: "SecondEndpoint")));

        mycelium.ThingsCreated.Count(created => created is { Model: var m, Name: "EsriEndpoint" } && m == FirstModel)
            .Should().Be(1);
    }

    [Fact]
    public async Task Handle_TemplateDeletedAfterProvisioning_ProvisionsAgainSoTheNextRegistrationSucceeds()
    {
        var mycelium = new ModelPartitionedMycelium();
        await using var factory = FactoryOver(mycelium);
        await factory.InitializeAsync();
        using var client = ClientFor(factory, FirstModel);

        await client.PostAsJsonAsync("/handle", RegistrationUnder("EsriEndpoint"));
        mycelium.DeleteThing(FirstModel, "EsriEndpoint");

        var refused = await client.PostAsJsonAsync("/handle", RegistrationUnder("EsriEndpoint", name: "SecondEndpoint"));
        var afterReprovisioning = await client.PostAsJsonAsync("/handle", RegistrationUnder("EsriEndpoint", name: "ThirdEndpoint"));

        refused.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        afterReprovisioning.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static DeltaWebApplicationFactory FactoryOver(
        ModelPartitionedMycelium mycelium, bool answerConcurrently = false) => new()
    {
        SeedJson = TwoTemplateSeed,
        SigningKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(SigningKey)),
        Issuer = Issuer,
        Audience = Audience,
        HandlerCallback = mycelium.Route,
        AnswerConcurrently = answerConcurrently
    };

    private static HttpClient ClientFor(DeltaWebApplicationFactory factory, Guid modelId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", BearerFor(modelId));
        return client;
    }

    private static string BearerFor(Guid modelId)
    {
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: new[] { new Claim(VosClaims.ModelId, modelId.ToString()) },
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static object RegistrationUnder(string template, string name = "MyEndpoint") => new
    {
        name,
        properties = new { url = "https://api.example/x", httpMethod = "GET" },
        relationships = new[] { new { subject = name, predicate = "is", target = template } }
    };
}
