using System.Text.RegularExpressions;

namespace vos.Taproot;

// A command line as it is reported to the broker and on to the people the model names. Passwords are
// prompted for rather than typed, but a person can still type a key or a token as an option or as a
// property's value, and a message sent to a phone must never carry one.
public static partial class ReportedCommandLine
{
    public const string Mask = "****";

    [GeneratedRegex("password|secret|token|key", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveName();

    [GeneratedRegex("^(?<name>-{0,2}[^=\\s]+)=(?<value>.+)$")]
    private static partial Regex NamedValue();

    public static string For(string input)
    {
        var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (var index = 0; index < words.Length; index++)
            if (NamedValue().Match(words[index]) is { Success: true } named && SensitiveName().IsMatch(named.Groups["name"].Value))
                words[index] = $"{named.Groups["name"].Value}={Mask}";

        // set <thing> <property> <value...> and create property <thing> <property> <type> <value...>
        var (propertyAt, valueFrom) = words switch
        {
            [var verb, ..] when verb.Equals("set", StringComparison.OrdinalIgnoreCase) => (2, 3),
            [var verb, var noun, ..] when verb.Equals("create", StringComparison.OrdinalIgnoreCase)
                                          && noun.StartsWith("property", StringComparison.OrdinalIgnoreCase) => (3, 5),
            _ => (-1, -1),
        };

        if (propertyAt >= 0 && words.Length > valueFrom && SensitiveName().IsMatch(words[propertyAt]))
            words = [.. words[..valueFrom], Mask];

        return string.Join(' ', words);
    }
}
