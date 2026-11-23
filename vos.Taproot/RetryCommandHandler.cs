using System.Net;

namespace vos.Taproot;

public class RetryCommandHandler
{
    private readonly string _arg;
    private readonly TextWriter _writer;
    private readonly MyceliumClient _mycelium;

    public RetryCommandHandler(string arg, TextWriter writer, MyceliumClient mycelium)
    {
        _arg = arg;
        _writer = writer;
        _mycelium = mycelium;
    }

    public async Task ExecuteAsync()
    {
        var parts = _arg.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !parts[0].Equals("call", StringComparison.OrdinalIgnoreCase))
        {
            ShowUsage();
            return;
        }

        if (!Guid.TryParse(parts[1], out var recordId))
        {
            _writer.WriteLine($"Error: '{parts[1]}' is not a record identifier. Nothing was sent.");
            return;
        }

        var reason = parts.Length > 2 ? parts[2].Trim() : null;
        try
        {
            CommandParser.WriteJsonOrText(_writer, await _mycelium.RetryCallAsync(recordId, reason));
        }
        catch (HttpRequestException refused) when (refused.StatusCode == HttpStatusCode.Conflict)
        {
            _writer.WriteLine("The call is no longer failed or blocked, so it was not retried.");
            _writer.WriteLine("The broker answered: " + OperatorMessage.For(refused));
        }
        catch (HttpRequestException refused) when (refused.StatusCode == HttpStatusCode.Forbidden)
        {
            _writer.WriteLine("This account may not change the model, so the call was not retried.");
            _writer.WriteLine("The broker answered: " + OperatorMessage.For(refused));
        }
        catch (Exception ex)
        {
            _writer.WriteLine("Error: " + OperatorMessage.For(ex));
        }
    }

    private void ShowUsage()
    {
        _writer.WriteLine("Usage: retry call <recordId> [reason]   - Send a failed or blocked service call again and print the broker's answer");
        _writer.WriteLine();
        _writer.WriteLine("<recordId> is the identifier of the relationship the call is recorded on.");
        _writer.WriteLine("The reason is the rest of the line; the broker keeps it in the record's history.");
    }
}
