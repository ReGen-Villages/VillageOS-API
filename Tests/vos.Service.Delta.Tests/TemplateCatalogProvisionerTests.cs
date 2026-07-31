using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using vos.Service.Delta.Models;
using vos.Service.Delta.Services;
using vos.Service.Delta.Tests.Services;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Delta.Tests;

// Unit tests for TemplateCatalogProvisioner (Task #5468) — boot-time idempotent
// creation of the endpoint-template catalog and its is wiring. The provisioner is exercised
// against a real MyceliumClient over a MockHttpMessageHandler (the repo's
// mycelium-faking convention), with a small stateful MyceliumStub tracking created things
// and relationships so we can assert the exact mycelium writes.
public class TemplateCatalogProvisionerTests
{
    private const string RootOnlySeed = """
    {"things":[{"name":"Endpoint","properties":{"url":"","httpMethod":"GET"}}],"relationships":[]}
    """;

    private const string TwoLevelSeed = """
    {
      "things": [
        { "name": "Endpoint", "properties": { "url": "", "httpMethod": "GET" } },
        { "name": "EsriEndpoint", "properties": { "layer": "" } }
      ],
      "relationships": [ { "subject": "EsriEndpoint", "predicate": "is", "target": "Endpoint" } ]
    }
    """;

    private const string ThreeLevelSeed = """
    {
      "things": [
        { "name": "Endpoint", "properties": { "url": "", "httpMethod": "GET" } },
        { "name": "EsriEndpoint", "properties": { "layer": "" } },
        { "name": "CountyParcels", "properties": { "layer": "3" } }
      ],
      "relationships": [
        { "subject": "EsriEndpoint", "predicate": "is", "target": "Endpoint" },
        { "subject": "CountyParcels", "predicate": "is", "target": "EsriEndpoint" }
      ]
    }
    """;

    // Two siblings, deliberately listed child-before-the-other to prove ordering is robust.
    private const string SiblingSeed = """
    {
      "things": [
        { "name": "Endpoint", "properties": { "url": "" } },
        { "name": "B", "properties": {} },
        { "name": "A", "properties": {} }
      ],
      "relationships": [
        { "subject": "B", "predicate": "is", "target": "Endpoint" },
        { "subject": "A", "predicate": "is", "target": "Endpoint" }
      ]
    }
    """;

    // The `is` target is authored in a different case than the thing name to pin case-insensitive
    // parent resolution during wiring.
    private const string CaseVariationSeed = """
    {
      "things": [
        { "name": "Endpoint", "properties": { "url": "" } },
        { "name": "EsriEndpoint", "properties": { "layer": "" } }
      ],
      "relationships": [ { "subject": "EsriEndpoint", "predicate": "is", "target": "endpoint" } ]
    }
    """;

    [Fact]
    public async Task ProvisionAsync_FreshMycelium_CreatesEveryTemplateThingWithProperties()
    {
        var stub = new MyceliumStub();

        await Provisioner(stub, TwoLevelSeed).ProvisionAsync();

        stub.CreatedByName.Keys.Should().BeEquivalentTo(new[] { "Endpoint", "EsriEndpoint" });
        stub.ThingPostBodies.Should().Contain(b => b.Contains("httpMethod") && b.Contains("GET"),
            "the Endpoint template thing is created carrying its seed properties");
    }

    [Fact]
    public async Task ProvisionAsync_FreshMycelium_WiresChildToParentViaIs()
    {
        var stub = new MyceliumStub();

        await Provisioner(stub, TwoLevelSeed).ProvisionAsync();

        stub.Relationships.Should().ContainSingle();
        var rel = stub.Relationships[0];
        rel.Predicate.Should().Be(stub.IsId);
        rel.Subject.Should().Be(stub.CreatedByName["EsriEndpoint"]);
        rel.Target.Should().Be(stub.CreatedByName["Endpoint"]);
    }

    [Fact]
    public async Task ProvisionAsync_RootOnly_CreatesThingButNoRelationship()
    {
        var stub = new MyceliumStub();

        await Provisioner(stub, RootOnlySeed).ProvisionAsync();

        stub.CreatedByName.Keys.Should().BeEquivalentTo(new[] { "Endpoint" });
        stub.Relationships.Should().BeEmpty("the root template has no parent to wire to");
    }

    [Fact]
    public async Task ProvisionAsync_AllTemplatesAlreadyExist_CreatesNothingAndWiresNothing()
    {
        var stub = new MyceliumStub();
        stub.Preexist("Endpoint", Guid.NewGuid());
        stub.Preexist("EsriEndpoint", Guid.NewGuid());

        await Provisioner(stub, TwoLevelSeed).ProvisionAsync();

        stub.ThingPostCount.Should().Be(0, "find-or-create is idempotent across restarts");
        stub.Relationships.Should().BeEmpty("existing things are assumed already wired");
    }

    [Fact]
    public async Task ProvisionAsync_ParentExistsChildMissing_WiresNewChildToExistingParent()
    {
        var stub = new MyceliumStub();
        var endpointId = Guid.NewGuid();
        stub.Preexist("Endpoint", endpointId);

        await Provisioner(stub, TwoLevelSeed).ProvisionAsync();

        stub.CreatedByName.Keys.Should().BeEquivalentTo(new[] { "EsriEndpoint" });
        stub.Relationships.Should().ContainSingle();
        stub.Relationships[0].Subject.Should().Be(stub.CreatedByName["EsriEndpoint"]);
        stub.Relationships[0].Target.Should().Be(endpointId);
    }

    [Fact]
    public async Task ProvisionAsync_IsPredicateMissing_AbortsWithoutCreatingAnything()
    {
        var stub = new MyceliumStub { IsPredicatePresent = false };

        var act = async () => await Provisioner(stub, TwoLevelSeed).ProvisionAsync();

        await act.Should().NotThrowAsync();
        stub.ThingPostCount.Should().Be(0);
        stub.Relationships.Should().BeEmpty();
    }

    [Fact]
    public async Task ProvisionAsync_ThreeLevelChain_WiresEachToDirectParentOnly()
    {
        var stub = new MyceliumStub();

        await Provisioner(stub, ThreeLevelSeed).ProvisionAsync();

        stub.Relationships.Should().HaveCount(2);
        stub.Relationships.Should().Contain(r =>
            r.Subject == stub.CreatedByName["EsriEndpoint"] && r.Target == stub.CreatedByName["Endpoint"]);
        stub.Relationships.Should().Contain(r =>
            r.Subject == stub.CreatedByName["CountyParcels"] && r.Target == stub.CreatedByName["EsriEndpoint"]);
        stub.Relationships.Should().NotContain(r =>
            r.Subject == stub.CreatedByName["CountyParcels"] && r.Target == stub.CreatedByName["Endpoint"]);
    }

    [Fact]
    public async Task ProvisionAsync_SiblingsAtSameDepth_AllCreatedAndWiredRegardlessOfSeedOrder()
    {
        var stub = new MyceliumStub();

        await Provisioner(stub, SiblingSeed).ProvisionAsync();

        stub.CreatedByName.Keys.Should().BeEquivalentTo(new[] { "Endpoint", "A", "B" });
        stub.Relationships.Should().HaveCount(2);
        stub.Relationships.Should().Contain(r => r.Subject == stub.CreatedByName["A"] && r.Target == stub.CreatedByName["Endpoint"]);
        stub.Relationships.Should().Contain(r => r.Subject == stub.CreatedByName["B"] && r.Target == stub.CreatedByName["Endpoint"]);
    }

    [Fact]
    public async Task ProvisionAsync_ParentNameCaseVariation_WiresViaCaseInsensitiveLookup()
    {
        var stub = new MyceliumStub();

        await Provisioner(stub, CaseVariationSeed).ProvisionAsync();

        stub.Relationships.Should().ContainSingle();
        stub.Relationships[0].Subject.Should().Be(stub.CreatedByName["EsriEndpoint"]);
        stub.Relationships[0].Target.Should().Be(stub.CreatedByName["Endpoint"]);
    }

    [Fact]
    public async Task ProvisionAsync_TemplateCreateFails_SkipsItAndDoesNotWireDescendants()
    {
        var stub = new MyceliumStub { FailCreateName = "Endpoint" };

        await Provisioner(stub, TwoLevelSeed).ProvisionAsync();

        stub.ThingPostCount.Should().Be(2, "both creates are attempted; the loop continues past a failure");
        stub.CreatedByName.Keys.Should().BeEquivalentTo(new[] { "EsriEndpoint" });
        stub.Relationships.Should().BeEmpty("the child cannot be wired to a parent that failed to create");
    }

    // ---------- Harness ----------

    private static TemplateCatalogProvisioner Provisioner(MyceliumStub stub, string seedJson) =>
        new(MyceliumClientOver(stub.Handler()),
            new InMemoryEndpointSeedProvider(seedJson).LoadGraph(),
            Substitute.For<ILogger<TemplateCatalogProvisioner>>());

    private static MyceliumClient MyceliumClientOver(MockHttpMessageHandler handler) =>
        new(new PerCallFactory(handler), Substitute.For<ILogger<MyceliumClient>>(), "http://localhost", "test-token");

    private sealed class PerCallFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public PerCallFactory(HttpMessageHandler handler) { _handler = handler; }
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    // Stateful fake of Mycelium's thing/relationship endpoints used by the provisioner.
    private sealed class MyceliumStub
    {
        public Guid IsId { get; } = Guid.NewGuid();
        public bool IsPredicatePresent { get; init; } = true;
        public string? FailCreateName { get; init; }

        private readonly Dictionary<string, Guid> _preexisting = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Guid> CreatedByName { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> ThingPostBodies { get; } = new();
        public List<(Guid Subject, Guid Predicate, Guid Target)> Relationships { get; } = new();
        public int ThingPostCount => ThingPostBodies.Count;

        public void Preexist(string name, Guid id) => _preexisting[name] = id;

        public MockHttpMessageHandler Handler() => new(Route);

        private HttpResponseMessage Route(HttpRequestMessage req)
        {
            var path = req.RequestUri!.AbsolutePath;

            if (req.Method == HttpMethod.Get && path == "/api/things")
            {
                var name = NameParam(req);
                if (string.Equals(name, "is", StringComparison.OrdinalIgnoreCase))
                    return IsPredicatePresent ? Thing(IsId, "is") : Json("null");
                if (name != null && _preexisting.TryGetValue(name, out var id))
                    return Thing(id, name);
                return Json("null");
            }

            if (req.Method == HttpMethod.Post && path == "/api/things")
            {
                var body = Body(req);
                ThingPostBodies.Add(body);
                var name = StringProp(body, "name") ?? string.Empty;
                if (FailCreateName != null && string.Equals(name, FailCreateName, StringComparison.OrdinalIgnoreCase))
                    return new HttpResponseMessage(HttpStatusCode.BadRequest);
                var newId = Guid.NewGuid();
                CreatedByName[name] = newId;
                return Thing(newId, name);
            }

            if (req.Method == HttpMethod.Post && path == "/api/relationships")
            {
                var body = Body(req);
                Relationships.Add((GuidProp(body, "subjectId"), GuidProp(body, "predicateId"), GuidProp(body, "targetId")));
                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static string Body(HttpRequestMessage req) =>
            req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;

        private static string? NameParam(HttpRequestMessage req)
        {
            foreach (var pair in req.RequestUri!.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                if (kv.Length == 2 && kv[0] == "name")
                    return Uri.UnescapeDataString(kv[1]);
            }
            return null;
        }

        private static string? StringProp(string json, string prop)
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() : null;
        }

        private static Guid GuidProp(string json, string prop)
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(prop, out var v) && Guid.TryParse(v.GetString(), out var g)
                ? g : Guid.Empty;
        }

        private static HttpResponseMessage Thing(Guid id, string name) =>
            Json($"{{\"Id\":\"{id}\",\"Name\":\"{name}\",\"Properties\":{{}}}}");

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
