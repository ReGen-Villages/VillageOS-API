using System.Globalization;

namespace vos.Taproot;

// One reading of a timestamp typed at the prompt, shared by every command that takes a window.
public static class Timestamps
{
    public const string ExpectedForm = "Use ISO 8601 format (e.g., 2026-01-15T12:30:00Z) or 'now'";

    public static bool TryParse(string input, out DateTime result) =>
        DateTime.TryParse(input, null, DateTimeStyles.RoundtripKind, out result);
}
