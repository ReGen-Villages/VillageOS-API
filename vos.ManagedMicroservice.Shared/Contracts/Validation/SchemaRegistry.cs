using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using NJsonSchema;

[assembly: InternalsVisibleTo("vos.ManagedMicroservice.Shared.Contracts.Tests")]

namespace vos.ManagedMicroservice.Shared.Contracts.Validation;

/// <summary>Eagerly loads every embedded JSON Schema and exposes them by $id; treat as a singleton.</summary>
public sealed class SchemaRegistry
{
    private const string ResourcePrefix = "vos.ManagedMicroservice.Shared.Contracts.Schemas.";
    private const string ResourceSuffix = ".schema.json";

    private readonly Dictionary<string, JsonSchema> _byId;

    public SchemaRegistry()
        : this(LoadEmbeddedRawSchemas())
    {
    }

    internal SchemaRegistry(IEnumerable<(string ResourceName, string Json)> rawSchemas)
    {
        var loaded = new Dictionary<string, JsonSchema>(StringComparer.Ordinal);
        foreach (var (resourceName, json) in rawSchemas)
        {
            // NJsonSchema's JsonSchema.Id is inconsistent across drafts; read $id from the raw JSON instead.
            var id = ReadDollarId(json)
                ?? throw new InvalidOperationException(
                    $"Embedded schema '{resourceName}' has no top-level $id.");

            if (loaded.ContainsKey(id))
                throw new InvalidOperationException(
                    $"Duplicate $id '{id}' across embedded schemas (last seen in '{resourceName}').");

            loaded[id] = JsonSchema.FromJsonAsync(json).GetAwaiter().GetResult();
        }
        _byId = loaded;
    }

    public IReadOnlyCollection<string> Ids => _byId.Keys;

    public JsonSchema Get(string id)
    {
        if (_byId.TryGetValue(id, out var schema)) return schema;
        throw new KeyNotFoundException($"No contract schema registered with $id '{id}'.");
    }

    public JsonSchema Get<T>()
    {
        var attr = typeof(T).GetCustomAttribute<ContractSchemaAttribute>()
            ?? throw new InvalidOperationException(
                $"Type '{typeof(T).FullName}' is missing [ContractSchema(\"<id>\")].");
        return Get(attr.Id);
    }

    internal static string? ReadDollarId(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("$id", out var idProp) &&
               idProp.ValueKind == JsonValueKind.String
            ? idProp.GetString()
            : null;
    }

    private static List<(string ResourceName, string Json)> LoadEmbeddedRawSchemas()
    {
        var result = new List<(string, string)>();
        var asm = typeof(SchemaRegistry).Assembly;
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal) ||
                !name.EndsWith(ResourceSuffix, StringComparison.Ordinal))
                continue;

            using var stream = asm.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Embedded schema '{name}' could not be opened.");
            using var reader = new StreamReader(stream);
            result.Add((name, reader.ReadToEnd()));
        }
        return result;
    }
}
