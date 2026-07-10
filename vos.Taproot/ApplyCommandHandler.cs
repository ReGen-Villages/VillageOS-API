using System.Text.Json;

namespace vos.Taproot;

// `apply <file.json>` upserts a fragment (a partial-model {Name, Things, Relationships} batch) into
// the live model via POST /api/model/fragment. Idempotent: re-applying the same fragment neither
// duplicates nor errors, and the server resolves lazy inheritance. Contrast `plant`, which replaces
// the whole model.
public class ApplyCommandHandler
{
    private readonly TextWriter _writer;
    private readonly string _arg;
    private readonly MyceliumClient _mycelium;

    public ApplyCommandHandler(string arg, TextWriter writer, MyceliumClient mycelium)
    {
        _arg = arg ?? string.Empty;
        _writer = writer;
        _mycelium = mycelium;
    }

    public async Task ExecuteAsync()
    {
        var filePath = _arg.Trim();
        if (string.IsNullOrEmpty(filePath))
        {
            ShowUsage();
            return;
        }

        try
        {
            if (!File.Exists(filePath))
            {
                _writer.WriteLine($"Error: File not found: {filePath}");
                return;
            }

            var fragmentJson = await File.ReadAllTextAsync(filePath);
            var result = await _mycelium.ApplyFragmentAsync(fragmentJson);
            _writer.WriteLine(FormatCounts(result, filePath));
        }
        catch (Exception ex)
        {
            _writer.WriteLine($"Error: {ex.Message}");
        }
    }

    private static string FormatCounts(JsonElement result, string filePath)
    {
        int Count(string name) =>
            result.ValueKind == JsonValueKind.Object
            && result.TryGetProperty(name, out var v)
            && v.TryGetInt32(out var n) ? n : 0;

        return $"Fragment applied from {filePath}: " +
               $"{Count("thingsCreated")} thing(s) created, {Count("thingsUpdated")} updated, " +
               $"{Count("relationshipsCreated")} relationship(s) created.";
    }

    private void ShowUsage()
    {
        _writer.WriteLine("Usage: apply <file.json>   - Upsert a fragment (Things + Relationships) into the live model");
        _writer.WriteLine("  File shape: { \"Name\", \"Things\": [ {Id, Name, Properties} ], \"Relationships\": [ {Name, Subject, Predicate, Target} ] }");
        _writer.WriteLine("  Idempotent: re-applying the same fragment neither duplicates nor errors.");
    }
}
