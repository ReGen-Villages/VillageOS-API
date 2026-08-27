using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using vos.Service.Shared;

namespace vos.Service.Delta.Tests;

/// <summary>A Mycelium that keeps each model's Things apart, the way the real one does. Every call is
/// answered from the model its bearer names, so a test can tell provisioning a model from finding what
/// another model already holds — which a stub with one set of Things cannot show at all.</summary>
public sealed class ModelPartitionedMycelium
{
    private readonly ConcurrentDictionary<Guid, Model> _models = new();

    public List<(Guid Model, string Name)> ThingsCreated { get; } = new();
    public List<(Guid Model, Guid Subject, Guid Predicate, Guid Target)> RelationshipsCreated { get; } = new();
    public List<(Guid Model, string Name)> NameLookups { get; } = new();

    private readonly HashSet<Guid> _deleted = new();
    private readonly Lock _recording = new();

    public Guid? IdOf(Guid modelId, string name) =>
        _models.TryGetValue(modelId, out var model) && model.Things.TryGetValue(name, out var id) ? id : null;

    /// <summary>Remove a Thing a caller may still be holding the id of — a template deleted out from
    /// under whoever provisioned it. The name stops resolving and relating to the id is refused.</summary>
    public void DeleteThing(Guid modelId, string name)
    {
        if (ModelFor(modelId).Things.TryRemove(name, out var id))
            _deleted.Add(id);
    }

    /// <summary>Every model answers for `is`: it is a model primitive, present before Delta arrives.</summary>
    private Model ModelFor(Guid modelId) => _models.GetOrAdd(modelId, _ =>
    {
        var model = new Model();
        model.Things["is"] = Guid.NewGuid();
        return model;
    });

    public HttpResponseMessage Route(HttpRequestMessage request)
    {
        var modelId = ModelOf(request);
        var model = ModelFor(modelId);
        var path = request.RequestUri!.AbsolutePath;

        if (CatalogEdgeRead.Answer(request, model.Relationships) is { } catalogEdges)
            return catalogEdges;

        if (request.Method == HttpMethod.Get && path == "/api/things")
        {
            var name = NameParameter(request);
            Record(() => NameLookups.Add((modelId, name ?? "")));
            return name != null && model.Things.TryGetValue(name, out var found)
                ? Thing(found, name)
                : Json("null");
        }

        if (request.Method == HttpMethod.Post && path == "/api/things")
        {
            var name = NameInBody(request);
            var id = Guid.NewGuid();
            model.Things[name] = id;
            Record(() => ThingsCreated.Add((modelId, name)));
            return Thing(id, name);
        }

        if (request.Method == HttpMethod.Post && path == "/api/relationships")
        {
            var (subject, predicate, target) = RelationshipInBody(request);
            if (_deleted.Contains(target))
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            model.Relationships.Add((subject, predicate, target));
            Record(() => RelationshipsCreated.Add((modelId, subject, predicate, target)));
            return Json("{}");
        }

        if (request.Method == HttpMethod.Put && path.EndsWith("/properties"))
            return Json("{}");

        if (request.Method == HttpMethod.Delete)
            return Json("{}");

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private void Record(Action record)
    {
        lock (_recording)
            record();
    }

    private static Guid ModelOf(HttpRequestMessage request) =>
        ModelScopedBearer.Read(request.Headers.Authorization?.Parameter)?.ModelId ?? Guid.Empty;

    private static string? NameParameter(HttpRequestMessage request)
    {
        foreach (var pair in request.RequestUri!.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var keyAndValue = pair.Split('=', 2);
            if (keyAndValue.Length == 2 && keyAndValue[0] == "name")
                return Uri.UnescapeDataString(keyAndValue[1]);
        }
        return null;
    }

    private static string NameInBody(HttpRequestMessage request) =>
        Field(JsonDocument.Parse(Body(request)).RootElement, "name").GetString() ?? "";

    private static (Guid Subject, Guid Predicate, Guid Target) RelationshipInBody(HttpRequestMessage request)
    {
        var body = JsonDocument.Parse(Body(request)).RootElement;
        return (Field(body, "subjectId").GetGuid(),
                Field(body, "predicateId").GetGuid(),
                Field(body, "targetId").GetGuid());
    }

    // Delta's outbound bodies are not all cased alike — a Thing-create serialises a DTO, a relationship
    // an anonymous type — and which casing arrives is a serialiser default, not part of the contract.
    private static JsonElement Field(JsonElement body, string name) =>
        body.EnumerateObject()
            .First(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            .Value;

    private static string Body(HttpRequestMessage request) =>
        request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();

    private static HttpResponseMessage Thing(Guid id, string name) =>
        Json("{\"Id\":\"" + id + "\",\"Name\":\"" + name + "\",\"Properties\":{}}");

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Model
    {
        public ConcurrentDictionary<string, Guid> Things { get; } = new(StringComparer.OrdinalIgnoreCase);

        public ConcurrentBag<(Guid Subject, Guid Predicate, Guid Target)> Relationships { get; } = new();
    }
}
