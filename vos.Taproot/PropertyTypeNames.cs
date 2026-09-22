namespace vos.Taproot;

// The platform accepts only its own type names (vos.String, vos.Integer, ...). The console lets an
// operator type the short or CLR name instead; anything else is sent as typed, and the platform
// refuses it with a reason the operator sees.
public static class PropertyTypeNames
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["string"] = "vos.String",
        ["System.String"] = "vos.String",
        ["int"] = "vos.Integer",
        ["integer"] = "vos.Integer",
        ["System.Int32"] = "vos.Integer",
        ["long"] = "vos.LongInteger",
        ["System.Int64"] = "vos.LongInteger",
        ["double"] = "vos.Double",
        ["System.Double"] = "vos.Double",
        ["float"] = "vos.Float",
        ["System.Single"] = "vos.Float",
        ["decimal"] = "vos.Decimal",
        ["System.Decimal"] = "vos.Decimal",
        ["bool"] = "vos.Boolean",
        ["boolean"] = "vos.Boolean",
        ["System.Boolean"] = "vos.Boolean",
        ["datetime"] = "vos.DateTime",
        ["System.DateTime"] = "vos.DateTime",
        ["guid"] = "vos.Guid",
        ["System.Guid"] = "vos.Guid",
    };

    public static string Canonical(string typed) =>
        Aliases.TryGetValue(typed, out var canonical) ? canonical : typed;
}
