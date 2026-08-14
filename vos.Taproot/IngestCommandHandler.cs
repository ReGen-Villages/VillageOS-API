using System.Text.Json;

namespace vos.Taproot;

// `ingest <file.ifc> [--new] [--name=<model>] [--url=<xylem-url>]` uploads an IFC to the Xylem ingestion
// service, which parses it and applies the graph to the model — no local ingest toolchain needed. The
// service URL comes from --url or the VOS_INGEST_URL env var. --new replaces the model; the default merges
// (idempotent upsert by stable id).
public class IngestCommandHandler
{
    private readonly TextWriter _writer;
    private readonly string _arg;
    private readonly MyceliumClient _mycelium;

    public IngestCommandHandler(string arg, TextWriter writer, MyceliumClient mycelium)
    {
        _arg = arg ?? string.Empty;
        _writer = writer;
        _mycelium = mycelium;
    }

    public async Task ExecuteAsync()
    {
        var tokens = _arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var filePath = tokens.FirstOrDefault(t => !t.StartsWith("--"));
        if (string.IsNullOrEmpty(filePath)) { ShowUsage(); return; }

        string? Flag(string key) => tokens.FirstOrDefault(t => t.StartsWith(key))?.Substring(key.Length);
        bool Has(string flag) => tokens.Any(t => t.Equals(flag, StringComparison.OrdinalIgnoreCase));

        var url = Flag("--url=") ?? Environment.GetEnvironmentVariable("VOS_INGEST_URL");
        if (string.IsNullOrWhiteSpace(url))
        {
            _writer.WriteLine("Error: ingestion service URL required (--url=<xylem-url> or the VOS_INGEST_URL environment variable).");
            return;
        }

        try
        {
            if (!File.Exists(filePath))
            {
                _writer.WriteLine($"Error: File not found: {filePath}");
                return;
            }

            var name = Flag("--name=") ?? Path.GetFileNameWithoutExtension(filePath);
            var mode = Has("--new") ? "new-model" : "merge";

            var result = await _mycelium.IngestIfcAsync(url, filePath, name, mode);
            _writer.WriteLine(Format(result, filePath));
        }
        catch (Exception ex)
        {
            _writer.WriteLine($"Error: {OperatorMessage.For(ex)}");
        }
    }

    private static string Format(JsonElement result, string filePath)
    {
        var success = result.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
        if (!success)
        {
            var err = result.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String
                ? e.GetString()
                : "ingest failed";
            return $"Ingest failed: {err}";
        }

        int Count(string name) =>
            result.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : 0;

        return $"Ingested {filePath}: {Count("thingsCreated")} thing(s) created, " +
               $"{Count("thingsUpdated")} updated, {Count("relationshipsCreated")} relationship(s) created.";
    }

    private void ShowUsage()
    {
        _writer.WriteLine("Usage: ingest <file.ifc> [--new] [--name=<model>] [--url=<xylem-url>]");
        _writer.WriteLine("  Uploads an IFC to the Xylem ingestion service, which parses it and applies the graph to the model.");
        _writer.WriteLine("  --new replaces the model with a fresh one; the default merges (idempotent upsert).");
        _writer.WriteLine("  Service URL from --url or the VOS_INGEST_URL environment variable.");
    }
}
