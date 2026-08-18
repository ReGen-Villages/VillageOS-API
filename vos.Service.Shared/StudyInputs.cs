using System.Globalization;
using System.Text.Json;

namespace vos.Service.Shared;

/// <summary>The numeric inputs a reactive handler reads off one study's effective properties, bound to the
/// service doing the reading so every refusal can name itself and the input.
///
/// <para>A handler reads several inputs, and the platform can now answer a property with no value at all — a
/// roll-up whose member type resolves to nothing withholds its number rather than reporting zero. A refusal
/// that says only "not numeric" leaves an operator to guess which of them it meant.</para>
///
/// <para>Shared rather than copied into each handler: this pair sat in two services, identical but for the
/// name in the message, which is how one gets fixed and the other is left as it was.</para></summary>
public readonly struct StudyInputs(JsonElement properties, string serviceName)
{
    /// <summary>One input's value, refused by name when the study does not carry it or it is not a number.</summary>
    public double Number(string name)
    {
        if (properties.ValueKind == JsonValueKind.Object)
            foreach (var property in properties.EnumerateObject())
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    return Extract(property.Value, name);

        throw new KeyNotFoundException($"{serviceName} input '{name}' is not on the study.");
    }

    // The route returns each property as { "Value": <v>, ... } (case-insensitive key).
    private double Extract(JsonElement envelope, string name)
    {
        var value = envelope;
        if (envelope.ValueKind == JsonValueKind.Object)
            foreach (var field in envelope.EnumerateObject())
                if (string.Equals(field.Name, "Value", StringComparison.OrdinalIgnoreCase)) { value = field.Value; break; }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            JsonValueKind.Null or JsonValueKind.Undefined => throw new InvalidOperationException(
                $"{serviceName} input '{name}' is on the study with no value. A roll-up withholds its number "
                + "when the type it reduces over names no Thing the model holds."),
            _ => throw new InvalidOperationException($"{serviceName} input '{name}' is not numeric: {value.ValueKind}."),
        };
    }
}
