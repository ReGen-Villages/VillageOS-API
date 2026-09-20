namespace vos.Taproot;

// The `--name=value` and `--flag` options a command takes after its subject.
public static class CommandOptions
{
    public static string? Value(IEnumerable<string> tokens, string option)
    {
        var prefix = option + "=";
        return tokens
            .Where(token => token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(token => token[prefix.Length..])
            .LastOrDefault();
    }

    public static int? Number(IEnumerable<string> tokens, string option) =>
        int.TryParse(Value(tokens, option), out var number) ? number : null;

    public static bool Has(IEnumerable<string> tokens, string option) =>
        tokens.Any(token => token.Equals(option, StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<string> Positional(IEnumerable<string> tokens) =>
        tokens.Where(token => !token.StartsWith("--"));
}
