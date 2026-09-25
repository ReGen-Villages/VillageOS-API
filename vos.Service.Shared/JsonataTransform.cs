using System.Globalization;
using System.Text.Json;
using Jsonata.Net.Native;
using Jsonata.Net.Native.Json;

namespace vos.Service.Shared;

// The single JSONata engine wiring for the platform: Tributary reshapes endpoint responses,
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

    // Nothing is answered as no text: the engine writes it as the word "undefined", which is not JSON,
    // and every caller reads an empty answer as nothing.
    public string Eval(string inputJson, TimeProvider clock)
    {
        var result = _query.Eval(JToken.Parse(inputJson), EnvironmentReadingTheClock(clock));
        return result.Type == JTokenType.Undefined ? "" : result.ToIndentedString();
    }

    // Evaluate against a JSON value, returning the reshaped value. An empty/whitespace result (JSONata
    // "nothing") becomes a JSON null.
    public JsonElement Eval(JsonElement input, TimeProvider clock)
    {
        var result = Eval(input.GetRawText(), clock);
        if (string.IsNullOrWhiteSpace(result))
            return JsonSerializer.SerializeToElement((object?)null);
        using var document = JsonDocument.Parse(result);
        return document.RootElement.Clone();
    }

    // $now() and $millis() are where a transform reads a clock, and the engine answers both from the
    // process's own. An expression the model carries stamps values the model will hold, so both are
    // answered from the clock the caller stamps with — which in a simulated run stands years from this
    // machine's. Bound without arguments, so $now(picture) is refused rather than answered off the wrong
    // clock; a transform wanting a picture writes $fromMillis($millis(), picture).
    private static EvaluationEnvironment EnvironmentReadingTheClock(TimeProvider clock)
    {
        var environment = new EvaluationEnvironment();
        environment.BindFunction("now", () => clock.GetUtcNow().UtcDateTime
            .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
        environment.BindFunction("millis", () => clock.GetUtcNow().ToUnixTimeMilliseconds());
        return environment;
    }
}
