using System.Globalization;
using System.Text.Json;

namespace vos.Service.Shared;

/// <summary>The numeric inputs a reactive handler reads off one study's effective properties, bound to the
/// service doing the reading so every refusal and every wait can name itself and the input.
///
/// <para>A handler reads several inputs, and the platform can now answer a property with no value at all — a
/// roll-up whose member type resolves to nothing withholds its number rather than reporting zero. A refusal
/// that says only "not numeric" leaves an operator to guess which of them it meant.</para>
///
/// <para>Shared rather than copied into each handler: this pair sat in two services, identical but for the
/// name in the message, which is how one gets fixed and the other is left as it was.</para></summary>
public readonly struct StudyInputs(JsonElement properties, string serviceName)
{
    /// <summary>One input's value, refused by name when the study does not carry it or it is not a number.
    ///
    /// <para>The route answers a study's own properties under their bare name and every inherited one
    /// under a key qualified by the set it came from — <c>SiteStudy.perCapitaConsumptionM3</c>. Since the
    /// shared archetype took over the assumptions, that is where nearly every input now lives, so a name
    /// is resolved against the last segment of a key as well as the whole of it. An own value wins, as it
    /// does in the model; two inherited ones under one leaf name are refused rather than guessed at.</para>
    /// </summary>
    public double Number(string name) => Carried(name) switch
    {
        null => throw new KeyNotFoundException($"{serviceName} input '{name}' is not on the study."),
        { } value when Withheld(value) => throw new InvalidOperationException(
            $"{serviceName} input '{name}' is on the study with no value. A roll-up withholds its number "
            + "when the type it reduces over names no Thing the model holds."),
        { } value => AsNumber(value, name),
    };

    /// <summary>Which of these names the study holds no number under — one it does not carry at all, and one
    /// carried with its number withheld. Both mean the same to a handler: the figure has not arrived.
    ///
    /// <para>A study a submission built describes land and a programme and nothing else, so a reservoir
    /// capacity or a panel area is absent and stays absent until a building model exists. That is a figure
    /// to wait for rather than a fault, which is why a handler asks this before it reads
    /// (<see cref="RecomputeAnswer{TOutputs}"/>).</para></summary>
    public IReadOnlyList<string> WaitingFor(IEnumerable<string> names)
    {
        var waiting = new List<string>();
        foreach (var name in names)
            if (Carried(name) is not { } value || Withheld(value))
                waiting.Add(name);

        return waiting;
    }

    private static bool Withheld(JsonElement value) =>
        value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;

    private JsonElement? Carried(string name)
    {
        var qualified = new List<JsonProperty>();
        foreach (var property in properties.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return Unwrapped(property.Value);

            var separator = property.Name.LastIndexOf('.');
            if (separator >= 0 && string.Equals(property.Name[(separator + 1)..], name, StringComparison.OrdinalIgnoreCase))
                qualified.Add(property);
        }

        if (qualified.Count > 1)
            throw new InvalidOperationException(
                $"{serviceName} input '{name}' is declared more than once on what the study inherits: "
                + $"{string.Join(", ", qualified.Select(property => property.Name))}.");

        return qualified.Count == 1 ? Unwrapped(qualified[0].Value) : null;
    }

    // The route returns each property as { "Value": <v>, ... } (case-insensitive key).
    private static JsonElement Unwrapped(JsonElement envelope)
    {
        if (envelope.ValueKind == JsonValueKind.Object)
            foreach (var field in envelope.EnumerateObject())
                if (string.Equals(field.Name, "Value", StringComparison.OrdinalIgnoreCase))
                    return field.Value;

        return envelope;
    }

    private double AsNumber(JsonElement value, string name) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => throw new InvalidOperationException($"{serviceName} input '{name}' is not numeric: {value.ValueKind}."),
    };
}
