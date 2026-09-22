using System.Text.Json;

namespace vos.Taproot;

public class MyceliumStatusCommandHandler
{
    private readonly TextWriter _writer;
    private readonly string _arg;
    private readonly MyceliumClient _mycelium;

    public MyceliumStatusCommandHandler(string arg, TextWriter writer, MyceliumClient mycelium)
    {
        _arg = arg;
        _writer = writer;
        _mycelium = mycelium;
    }

    public async Task ExecuteAsync()
    {
        try
        {
            var tok = (_arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tok.Length == 0) { ShowUsage(); return; }

            switch (tok[0])
            {
                case "status":
                    await ShowSeedStatusAsync();
                    break;
                case "endpoints":
                    await ListEndpointsAsync();
                    break;
                default:
                    ShowUsage();
                    break;
            }
        }
        catch (Exception ex)
        {
            _writer.WriteLine("Error: " + OperatorMessage.For(ex));
        }
    }

    private async Task ShowSeedStatusAsync()
    {
        var status = await _mycelium.GetSeedStatusAsync();
        _writer.WriteLine(JsonSerializer.Serialize(status, new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task ListEndpointsAsync()
    {
        var endpoints = await _mycelium.GetEndpointsAsync();
        if (endpoints.ValueKind != JsonValueKind.Array || endpoints.GetArrayLength() == 0)
        {
            _writer.WriteLine("No endpoint services registered.");
            return;
        }

        _writer.WriteLine("Endpoint services:");
        foreach (var ep in endpoints.EnumerateArray())
        {
            var subdomain = ep.GetStringOrDefault("Subdomain", "unknown");
            var url = ep.GetStringOrDefault("BaseUrl", "unknown");
            _writer.WriteLine($"  {subdomain} → {url}");
        }
    }

    private void ShowUsage()
    {
        _writer.WriteLine("Usage:");
        _writer.WriteLine("  mycelium status             - Show seed loading status");
        _writer.WriteLine("  mycelium endpoints          - List registered endpoint services");
    }
}
