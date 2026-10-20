using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using vos.Service.Shared;

namespace vos.Service.Delta.Tests;

// A Mycelium holding one model per bearer. Its subscription and property reads follow the real broker's rules
// for the parts Delta uses, so a wrong selector or a wrong reading of the answer fails here as it would there.
public sealed class EndpointModelStandIn
{
    public static readonly Guid EndpointRoleId = new("d5902341-bbcb-5917-9456-7109e76ec2b8");

    private readonly Dictionary<Guid, Model> _models = new();
    private readonly Lock _gate = new();

    public List<(Guid Model, string Name)> ThingsCreated { get; } = new();
    public List<(Guid Model, Guid Subject, Guid Predicate, Guid Target)> RelationshipsCreated { get; } = new();
    public List<(Guid Model, Guid Thing, string Name)> PropertiesSet { get; } = new();
    public List<(Guid Model, Guid Thing)> ThingsDeleted { get; } = new();
    public List<Guid> SubscriptionsOpened { get; } = new();
    public List<Guid> SubscriptionsClosed { get; } = new();

    public bool RefuseThingCreate { get; set; }
    public bool RefuseRelationshipCreate { get; set; }
    public bool RefusePropertySet { get; set; }
    public bool RefuseThingDelete { get; set; }
    public bool RefuseSubscription { get; set; }

    public Guid Declare(Guid model, string name, bool isArchetype = false, IReadOnlyDictionary<string, string?>? properties = null)
    {
        lock (_gate)
        {
            var thing = new Thing(Guid.NewGuid(), name, isArchetype,
                new Dictionary<string, string?>(properties ?? new Dictionary<string, string?>(), StringComparer.Ordinal));
            ModelFor(model).Things[thing.Id] = thing;
            return thing.Id;
        }
    }

    public void Relate(Guid model, Guid subject, string predicateName, Guid target)
    {
        lock (_gate)
        {
            var held = ModelFor(model);
            held.Relationships.Add(new Relationship(Guid.NewGuid(), subject, PredicateNamed(held, predicateName), target));
        }
    }

    public void SetProperty(Guid model, Guid thing, string name, string? value)
    {
        lock (_gate)
            ModelFor(model).Things[thing].Properties[name] = value;
    }

    public void BindEndpointRole(Guid model, Guid root)
    {
        lock (_gate)
        {
            var held = ModelFor(model);
            held.Things[EndpointRoleId] = new Thing(EndpointRoleId, "endpoint role", false, new(StringComparer.Ordinal));
        }
        Relate(model, EndpointRoleId, "binds", root);
    }

    // The child narrows the method and declares a key of its own.
    public (Guid Root, Guid Child) SeedTree(Guid model, string childName = "EsriEndpoint")
    {
        var root = Declare(model, "Endpoint", isArchetype: true,
            new Dictionary<string, string?> { ["url"] = "", ["httpMethod"] = "GET", ["responseTransform"] = "" });
        var child = Declare(model, childName, isArchetype: true,
            new Dictionary<string, string?> { ["layer"] = "", ["httpMethod"] = "POST" });
        Relate(model, child, "is", root);
        BindEndpointRole(model, root);
        return (root, child);
    }

    public Guid IsPredicate(Guid model)
    {
        lock (_gate)
            return PredicateNamed(ModelFor(model), "is");
    }

    public Guid? TargetOfIs(Guid model, Guid subject)
    {
        lock (_gate)
        {
            var held = ModelFor(model);
            var isPredicate = PredicateNamed(held, "is");
            return held.Relationships.FirstOrDefault(edge => edge.Subject == subject && edge.Predicate == isPredicate)?.Target;
        }
    }

    public Guid? IdOf(Guid model, string name)
    {
        lock (_gate)
            return ModelFor(model).Things.Values.FirstOrDefault(thing => thing.Name == name)?.Id;
    }

    public HttpResponseMessage Route(HttpRequestMessage request)
    {
        var model = ModelOf(request);
        var path = request.RequestUri!.AbsolutePath;
        lock (_gate)
        {
            var held = ModelFor(model);

            if (request.Method == HttpMethod.Post && path == "/api/subscriptions")
                return RefuseSubscription ? Status(HttpStatusCode.InternalServerError) : Subscribe(held, Body(request));

            if (request.Method == HttpMethod.Delete && path.StartsWith("/api/subscriptions/", StringComparison.Ordinal))
            {
                SubscriptionsClosed.Add(Guid.Parse(path["/api/subscriptions/".Length..]));
                return Status(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Get && path.StartsWith("/api/things/", StringComparison.Ordinal)
                && path.EndsWith("/properties", StringComparison.Ordinal))
                return Resolved(held, Guid.Parse(path.Split('/')[3]));

            if (request.Method == HttpMethod.Post && path == "/api/things")
            {
                if (RefuseThingCreate) return Status(HttpStatusCode.InternalServerError);
                var name = Field(Body(request), "name")!.GetValue<string>();
                var thing = new Thing(Guid.NewGuid(), name, false, new(StringComparer.Ordinal));
                held.Things[thing.Id] = thing;
                ThingsCreated.Add((model, name));
                return Json(new JsonObject { ["Id"] = thing.Id, ["Name"] = name, ["Properties"] = new JsonObject() });
            }

            if (request.Method == HttpMethod.Post && path == "/api/relationships")
            {
                var body = Body(request);
                var subject = Guid.Parse(Field(body, "subjectId")!.GetValue<string>());
                var predicate = Guid.Parse(Field(body, "predicateId")!.GetValue<string>());
                var target = Guid.Parse(Field(body, "targetId")!.GetValue<string>());
                if (RefuseRelationshipCreate || !held.Things.ContainsKey(target))
                    return Status(HttpStatusCode.NotFound);
                held.Relationships.Add(new Relationship(Guid.NewGuid(), subject, predicate, target));
                RelationshipsCreated.Add((model, subject, predicate, target));
                return Status(HttpStatusCode.Created);
            }

            if (request.Method == HttpMethod.Put && path.EndsWith("/properties", StringComparison.Ordinal))
            {
                if (RefusePropertySet) return Status(HttpStatusCode.InternalServerError);
                var thing = Guid.Parse(path.Split('/')[3]);
                var body = Body(request);
                var name = Field(body, "name")!.GetValue<string>();
                held.Things[thing].Properties[name] = Field(body, "value")?.ToString();
                PropertiesSet.Add((model, thing, name));
                return Status(HttpStatusCode.OK);
            }

            if (request.Method == HttpMethod.Delete && path.StartsWith("/api/things/", StringComparison.Ordinal))
            {
                var thing = Guid.Parse(path.Split('/')[3]);
                ThingsDeleted.Add((model, thing));
                if (RefuseThingDelete) return Status(HttpStatusCode.InternalServerError);
                held.Things.Remove(thing);
                return Status(HttpStatusCode.NoContent);
            }
        }

        return Status(HttpStatusCode.NotFound);
    }

    private HttpResponseMessage Subscribe(Model held, JsonObject selector)
    {
        var selected = new HashSet<Guid>();
        foreach (var id in Strings(selector, "ids"))
            if (held.Things.ContainsKey(Guid.Parse(id))) selected.Add(Guid.Parse(id));
        foreach (var name in Strings(selector, "names"))
            foreach (var thing in held.Things.Values.Where(thing => thing.Name == name))
                selected.Add(thing.Id);

        foreach (var rule in Objects(selector, "traverse"))
        {
            var predicate = Field(rule, "predicate")!.GetValue<string>();
            var depth = Field(rule, "depth")?.GetValue<int>() ?? 1;
            for (var step = 0; step < depth; step++)
                foreach (var edge in Outgoing(held, selected, predicate).ToList())
                    selected.Add(edge.Target);
        }

        if (Field(selector, "includeIsAncestors")?.GetValue<bool>() ?? true)
            while (Outgoing(held, selected, "is").Select(edge => edge.Target).Where(selected.Add).ToList().Count > 0) { }

        var relationships = new List<Relationship>();
        if (Field(selector, "includeRelationships")?.GetValue<bool>() ?? true)
        {
            var rules = Objects(selector, "relationships").ToList();
            relationships = rules.Count == 0
                ? held.Relationships.Where(edge => selected.Contains(edge.Subject) || selected.Contains(edge.Target)).ToList()
                : rules.SelectMany(rule => Outgoing(held, selected, Field(rule, "predicate")!.GetValue<string>())).Distinct().ToList();
        }

        var subscription = Guid.NewGuid();
        SubscriptionsOpened.Add(subscription);
        var things = new JsonArray();
        foreach (var id in selected)
        {
            var thing = held.Things[id];
            var properties = new JsonObject();
            foreach (var (name, value) in thing.Properties)
                properties[name] = new JsonObject { ["value"] = value, ["typeInfo"] = "vos.String" };
            things.Add(new JsonObject
            {
                ["id"] = thing.Id, ["name"] = thing.Name, ["isArchetype"] = thing.IsArchetype,
                ["properties"] = properties, ["states"] = new JsonArray(),
            });
        }
        var edges = new JsonArray();
        foreach (var edge in relationships)
            edges.Add(new JsonObject
            {
                ["id"] = edge.Id, ["subjectId"] = edge.Subject, ["predicateId"] = edge.Predicate,
                ["targetId"] = edge.Target, ["properties"] = new JsonObject(), ["states"] = new JsonArray(),
            });

        return Json(new JsonObject
        {
            ["subscriptionId"] = subscription,
            ["watermark"] = 0,
            ["snapshot"] = new JsonObject { ["watermark"] = 0, ["things"] = things, ["relationships"] = edges },
        });
    }

    private static HttpResponseMessage Resolved(Model held, Guid id)
    {
        if (!held.Things.ContainsKey(id)) return Status(HttpStatusCode.NotFound);

        var isPredicate = PredicateNamed(held, "is");
        var chain = new List<Thing>();
        for (Guid? current = id; current is { } at && held.Things.TryGetValue(at, out var thing) && !chain.Contains(thing);
             current = held.Relationships.FirstOrDefault(edge => edge.Subject == at && edge.Predicate == isPredicate)?.Target)
            chain.Add(thing);

        // As the broker does: a value is named by the path of archetypes down to the one declaring it, and a
        // value a nearer Thing writes for that name is an override reported under the same path.
        var answer = new JsonObject();
        foreach (var name in chain.SelectMany(thing => thing.Properties.Keys).Distinct())
        {
            var declaredAt = chain.FindLastIndex(thing => thing.Properties.ContainsKey(name));
            var valueAt = chain.FindIndex(thing => thing.Properties.ContainsKey(name));
            var path = declaredAt == 0
                ? name
                : string.Join('.', chain.Skip(1).Take(declaredAt).Select(thing => thing.Name)) + "." + name;
            answer[path] = new JsonObject
            {
                ["Value"] = chain[valueAt].Properties[name], ["Type"] = "vos.String",
                ["IsInherited"] = declaredAt != 0, ["InheritedFrom"] = declaredAt == 0 ? null : chain[declaredAt].Id.ToString(),
            };
        }
        return Json(answer);
    }

    private static IEnumerable<Relationship> Outgoing(Model held, HashSet<Guid> from, string predicateName)
    {
        var predicate = PredicateNamed(held, predicateName);
        return held.Relationships.Where(edge => edge.Predicate == predicate && from.Contains(edge.Subject));
    }

    // Every model answers for a predicate it is asked about, as a seeded model does for `is` and `binds`.
    private static Guid PredicateNamed(Model held, string name)
    {
        var found = held.Things.Values.FirstOrDefault(thing => thing.Name == name);
        if (found is not null) return found.Id;
        var predicate = new Thing(Guid.NewGuid(), name, false, new(StringComparer.Ordinal));
        held.Things[predicate.Id] = predicate;
        return predicate.Id;
    }

    private Model ModelFor(Guid model)
    {
        if (!_models.TryGetValue(model, out var held))
        {
            held = new Model();
            _models[model] = held;
            PredicateNamed(held, "is");
        }
        return held;
    }

    private static Guid ModelOf(HttpRequestMessage request) =>
        ModelScopedBearer.Read(request.Headers.Authorization?.Parameter)?.ModelId ?? Guid.Empty;

    private static JsonObject Body(HttpRequestMessage request) =>
        JsonNode.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult())!.AsObject();

    // Delta's outbound bodies are not all cased alike, and which casing arrives is a serialiser default.
    private static JsonNode? Field(JsonObject body, string name) =>
        body.FirstOrDefault(entry => string.Equals(entry.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    private static IEnumerable<string> Strings(JsonObject body, string name) =>
        Field(body, name) is JsonArray values ? values.Select(value => value!.GetValue<string>()) : [];

    private static IEnumerable<JsonObject> Objects(JsonObject body, string name) =>
        Field(body, name) is JsonArray values ? values.Select(value => value!.AsObject()) : [];

    private static HttpResponseMessage Json(JsonNode body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Status(HttpStatusCode status) => new(status);

    private sealed record Thing(Guid Id, string Name, bool IsArchetype, Dictionary<string, string?> Properties);

    private sealed record Relationship(Guid Id, Guid Subject, Guid Predicate, Guid Target);

    private sealed class Model
    {
        public Dictionary<Guid, Thing> Things { get; } = new();
        public List<Relationship> Relationships { get; } = new();
    }
}
