using System.Text.Json;

namespace vos.Taproot;

public static class CommandParser
{
    public static void WriteFormattedJson(TextWriter writer, JsonElement element)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        writer.WriteLine(JsonSerializer.Serialize(element, options));
    }

    public static void WriteJsonOrText(TextWriter writer, string answer)
    {
        if (IsJson(answer))
            WriteFormattedJson(writer, JsonDocument.Parse(answer).RootElement);
        else
            writer.WriteLine(answer);
    }

    public static bool IsJson(string text)
    {
        try
        {
            JsonDocument.Parse(text).Dispose();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

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
