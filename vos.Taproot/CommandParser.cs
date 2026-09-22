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
        if (TryParseJson(answer, out var document))
        {
            using (document)
                WriteFormattedJson(writer, document.RootElement);
        }
        else
        {
            writer.WriteLine(answer);
        }
    }

    public static bool IsJson(string text)
    {
        if (!TryParseJson(text, out var document)) return false;
        document.Dispose();
        return true;
    }

    private static bool TryParseJson(string text, out JsonDocument document)
    {
        try
        {
            document = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            document = null!;
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
