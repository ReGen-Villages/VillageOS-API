using System.Text.Json;

namespace vos.Taproot;

public class ModelCommandHandler
{
    private readonly TextWriter _writer;
    private readonly string _arg;
    private readonly MyceliumClient _mycelium;

    public ModelCommandHandler(string arg, TextWriter writer, MyceliumClient mycelium)
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
                case "list":
                    await ListModelsAsync();
                    break;
                case "switch" when tok.Length >= 2:
                    await SwitchModelAsync(tok[1]);
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

    private async Task ListModelsAsync()
    {
        var models = await _mycelium.ListModelsAsync();
        if (models.ValueKind != JsonValueKind.Array || models.GetArrayLength() == 0)
        {
            _writer.WriteLine("No models found.");
            return;
        }

        _writer.WriteLine("Available models:");
        foreach (var model in models.EnumerateArray())
        {
            var id = model.GetStringOrDefault("Id", "unknown");
            var name = model.GetStringOrDefault("Name", "unknown");
            _writer.WriteLine($"  {name} ({id})");
        }
    }

    private async Task SwitchModelAsync(string modelIdOrName)
    {
        if (!Guid.TryParse(modelIdOrName, out var modelId))
        {
            var models = await _mycelium.ListModelsAsync();
            var match = FindModelByName(models, modelIdOrName);
            if (match == null)
            {
                _writer.WriteLine($"Model not found: {modelIdOrName}");
                return;
            }
            modelId = match.Value;
        }

        var result = await _mycelium.SwitchModelAsync(modelId);
        _writer.WriteLine($"Switched to model {modelId}.");
    }

    private static Guid? FindModelByName(JsonElement models, string name)
    {
        if (models.ValueKind != JsonValueKind.Array) return null;
        foreach (var model in models.EnumerateArray())
        {
            var n = model.GetStringOrDefault("Name", "");
            if (n.Equals(name, StringComparison.OrdinalIgnoreCase)
                && Guid.TryParse(model.GetStringOrDefault("Id", ""), out var id))
                return id;
        }
        return null;
    }

    private void ShowUsage()
    {
        _writer.WriteLine("Usage:");
        _writer.WriteLine("  model list                - List available models");
        _writer.WriteLine("  model switch <id|name>    - Switch to a different model");
    }
}
