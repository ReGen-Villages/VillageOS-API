using System.Text.Json;

namespace vos.CLI;

/// <summary>
/// Shared parsing utilities for command handlers.
/// </summary>
public static class CommandParser
{
    /// <summary>
    /// Writes a JsonElement as indented JSON to the given writer.
    /// </summary>
    public static void WriteFormattedJson(TextWriter writer, JsonElement element)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        writer.WriteLine(JsonSerializer.Serialize(element, options));
    }

    /// <summary>
    /// Parses an input string into a subcommand and arguments.
    /// </summary>
    /// <param name="input">The raw input string to parse.</param>
    /// <param name="subcommand">The first word (subcommand).</param>
    /// <param name="args">Remaining words as arguments.</param>
    /// <returns>True if input contained at least one word, false otherwise.</returns>
    public static bool TryParseSubcommand(string input, out string subcommand, out string[] args)
    {
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            subcommand = "";
            args = Array.Empty<string>();
            return false;
        }

        subcommand = parts[0];
        args = parts.Skip(1).ToArray();
        return true;
    }
}
