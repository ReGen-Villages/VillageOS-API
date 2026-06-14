using System.Text.Json;

namespace vos.Taproot;

public class SeedCommandHandler
{
    private readonly TextWriter _writer;
    private readonly string _arg;
    private readonly MyceliumClient _mycelium;

    public SeedCommandHandler(string arg, TextWriter writer, MyceliumClient mycelium)
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
                    await ShowStatusAsync();
                    break;
                case "list":
                    await ListLibrarySeedsAsync();
                    break;
                case "load" when tok.Length >= 2:
                    await LoadSeedAsync(tok[1]);
                    break;
                case "save" when tok.Length >= 2:
                    await SaveSeedAsync(tok[1]);
                    break;
                case "reload":
                    await ReloadSeedsAsync();
                    break;
                default:
                    ShowUsage();
                    break;
            }
        }
        catch (Exception ex)
        {
            _writer.WriteLine("Error: " + ex.Message);
        }
    }

    private async Task ShowStatusAsync()
    {
        var status = await _mycelium.GetSeedStatusAsync();
        _writer.WriteLine(JsonSerializer.Serialize(status, new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task ListLibrarySeedsAsync()
    {
        var seeds = await _mycelium.ListLibrarySeedsAsync();
        if (seeds.ValueKind != JsonValueKind.Array || seeds.GetArrayLength() == 0)
        {
            _writer.WriteLine("No library seeds found.");
            return;
        }

        _writer.WriteLine("Library seeds:");
        foreach (var seed in seeds.EnumerateArray())
        {
            var name = seed.GetStringOrDefault("Name", seed.ToString());
            _writer.WriteLine($"  {name}");
        }
    }

    private async Task LoadSeedAsync(string name)
    {
        _writer.WriteLine($"Loading seed '{name}'...");
        var result = await _mycelium.LoadLibrarySeedAsync(name);
        _writer.WriteLine($"Seed '{name}' loaded.");
    }

    private async Task SaveSeedAsync(string name)
    {
        _writer.WriteLine($"Saving current model as seed '{name}'...");
        var result = await _mycelium.SaveLibrarySeedAsync(name);
        _writer.WriteLine($"Model saved as seed '{name}'.");
    }

    private async Task ReloadSeedsAsync()
    {
        _writer.WriteLine("Reloading seeds from disk...");
        var result = await _mycelium.ReloadSeedsAsync();
        _writer.WriteLine("Seeds reloaded.");
    }

    private void ShowUsage()
    {
        _writer.WriteLine("Usage:");
        _writer.WriteLine("  seed status               - Show seed loading status");
        _writer.WriteLine("  seed list                 - List available library seeds");
        _writer.WriteLine("  seed load <name>          - Load a library seed by name");
        _writer.WriteLine("  seed save <name>          - Save current model as a library seed");
        _writer.WriteLine("  seed reload               - Reload seeds from the seeds directory");
    }
}
