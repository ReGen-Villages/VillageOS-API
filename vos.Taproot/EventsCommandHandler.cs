namespace vos.Taproot;

// `events watch [--for=SECONDS]`: the model's event stream, one line per event, for a window.
public class EventsCommandHandler
{
    public const int DefaultWatchSeconds = 30;

    private readonly TextWriter _writer;
    private readonly string _arg;
    private readonly MyceliumClient _mycelium;
    private readonly Func<DateTime> _clock;

    public EventsCommandHandler(string arg, TextWriter writer, MyceliumClient mycelium, Func<DateTime>? clock = null)
    {
        _writer = writer;
        _arg = arg ?? "";
        _mycelium = mycelium;
        _clock = clock ?? (() => DateTime.Now);
    }

    public async Task ExecuteAsync()
    {
        var tokens = _arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0 || !tokens[0].Equals("watch", StringComparison.OrdinalIgnoreCase))
        {
            ShowUsage();
            return;
        }

        var seconds = CommandOptions.Number(tokens.Skip(1), "--for") ?? DefaultWatchSeconds;
        var received = 0;
        using var window = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        try
        {
            await foreach (var one in _mycelium.WatchEventsAsync(window.Token))
            {
                received++;
                _writer.WriteLine($"{_clock():HH:mm:ss}  {one.Name}  {one.Data}");
            }
        }
        catch (OperationCanceledException) when (window.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _writer.WriteLine("Error: " + OperatorMessage.For(ex));
            return;
        }

        _writer.WriteLine(received == 0
            ? $"Nothing happened in {seconds} s."
            : $"Watched {received} event(s) in {seconds} s.");
    }

    private void ShowUsage()
    {
        _writer.WriteLine("Usage: events watch [--for=SECONDS]   - Print each model event as it arrives, then return");
        _writer.WriteLine();
        _writer.WriteLine($"A watch ends after --for seconds (default {DefaultWatchSeconds}).");
    }
}
