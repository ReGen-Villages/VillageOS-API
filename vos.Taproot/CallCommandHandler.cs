using System.Text.Json;

namespace vos.Taproot;

// `call endpoint <subdomain> <json>` and `call service <handler> <json>`: post a body through the
// Mycelium to an endpoint service or to a handler's daemon, and print what came back.
public class CallCommandHandler : CommandHandlerWithOutputOptions
{
    public CallCommandHandler(string arg, TextWriter writer, MyceliumClient mycelium, OutputOptions? options = null)
        : base(arg, writer, mycelium, options)
    {
    }

    public async Task ExecuteAsync()
    {
        var parts = (_arg ?? "").Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            ShowUsage();
            return;
        }

        var body = parts[2].Trim();
        if (!IsJson(body))
        {
            _writer.WriteLine("Error: the body is not JSON. Nothing was sent.");
            return;
        }

        try
        {
            switch (parts[0].ToLowerInvariant())
            {
                case "endpoint":
                    WriteAnswer(await _mycelium.PostToEndpointAsync(parts[1], body));
                    break;
                case "service":
                    await RequestServiceAsync(parts[1], body);
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

    private async Task RequestServiceAsync(string handler, string body)
    {
        var resolved = await _resolver.ResolveThingAsync(handler);
        Guid handlerId;
        if (resolved.IsSuccess)
            handlerId = resolved.Id;
        else if (!Guid.TryParse(handler, out handlerId))
        {
            _writer.WriteLine($"Error: {resolved.ErrorMessage}");
            return;
        }

        WriteAnswer(await _mycelium.RequestServiceAsync(handlerId, body));
    }

    private void WriteAnswer(string answer)
    {
        if (IsJson(answer))
            CommandParser.WriteFormattedJson(_writer, JsonDocument.Parse(answer).RootElement);
        else
            _writer.WriteLine(answer);
    }

    private static bool IsJson(string text)
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

    private void ShowUsage()
    {
        _writer.WriteLine("Usage: call endpoint <subdomain> <json>   - Post a body to an endpoint service and print its answer");
        _writer.WriteLine("       call service <handler> <json>     - Post a body to a handler's daemon, starting it if needed");
        _writer.WriteLine();
        _writer.WriteLine("The body is the rest of the line and must be JSON; it is sent as typed.");
        _writer.WriteLine("<handler> can be a GUID or a unique name.");
    }
}
