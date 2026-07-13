using System.Text.Json;
using Jsonata.Net.Native;

namespace vos.ManagedMicroservice.Shared;

// The single JSONata engine wiring for the platform (#5875): Tributary reshapes endpoint responses,
// Phloem reshapes values on a pipeline wire — both go through this one wrapper rather than each newing up the
// engine. A JSONata expression compiles once (the ctor throws on a syntax error) and is evaluated many times.
public sealed class JsonataTransform
{
    private readonly JsonataQuery _query;

    // Compile expression. Throws if it is not valid JSONata.
    public JsonataTransform(string expression) => _query = new JsonataQuery(expression);

    // Compile-check an expression without evaluating it — for pre-run validation. Returns null when
    // the expression is valid, otherwise the compiler's error message.
    public static string? Validate(string expression)
    {
        try
        {
            _ = new JsonataQuery(expression);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    // Evaluate against a JSON string, returning the result as a JSON string.
    public string Eval(string inputJson) => _query.Eval(inputJson);

    // Evaluate against a JSON value, returning the reshaped value. An empty/whitespace result (JSONata
    // "nothing") becomes a JSON null.
    public JsonElement Eval(JsonElement input)
    {
        var result = _query.Eval(input.GetRawText());
        if (string.IsNullOrWhiteSpace(result))
            return JsonSerializer.SerializeToElement((object?)null);
        using var document = JsonDocument.Parse(result);
        return document.RootElement.Clone();
    }
}
