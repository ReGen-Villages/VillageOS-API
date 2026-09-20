using System.Text.Json;

namespace vos.Taproot;

// `logs tail`, `logs follow` and `logs download`: the Mycelium's own log by default, or a service
// daemon's when --service names one.
public class LogsCommandHandler
{
    public const int DefaultFollowSeconds = 30;

    private readonly TextWriter _writer;
    private readonly string _arg;
    private readonly MyceliumClient _mycelium;

    public LogsCommandHandler(string arg, TextWriter writer, MyceliumClient mycelium)
    {
        _writer = writer;
        _arg = arg ?? "";
        _mycelium = mycelium;
    }

    public async Task ExecuteAsync()
    {
        var tokens = _arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            ShowUsage();
            return;
        }

        var options = new LogOptions(tokens.Skip(1));
        try
        {
            switch (tokens[0].ToLowerInvariant())
            {
                case "tail":
                    await TailAsync(options);
                    break;
                case "follow":
                    await FollowAsync(options);
                    break;
                case "download":
                    await DownloadAsync(options);
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

    private async Task TailAsync(LogOptions options)
    {
        var answer = await _mycelium.GetLogTailAsync(options.Lines, options.Service);
        var file = answer.GetStringOrDefault("file", "");
        if (file.Length == 0)
        {
            _writer.WriteLine("No log file found.");
            return;
        }

        var lines = answer.TryGetProperty("lines", out var found) && found.ValueKind == JsonValueKind.Array
            ? found.EnumerateArray().Select(line => line.GetString() ?? "").ToList()
            : [];
        _writer.WriteLine($"{file} (last {lines.Count} lines):");
        foreach (var line in lines)
            _writer.WriteLine(line);
    }

    private async Task FollowAsync(LogOptions options)
    {
        var seconds = options.ForSeconds ?? DefaultFollowSeconds;
        using var window = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        try
        {
            await foreach (var received in _mycelium.FollowLogAsync(options.Lines, options.Service, window.Token))
            {
                if (received.Name == "log")
                    _writer.WriteLine(JsonSerializer.Deserialize<string>(received.Data));
            }
        }
        catch (OperationCanceledException) when (window.IsCancellationRequested)
        {
        }

        _writer.WriteLine($"Followed for {seconds} s.");
    }

    private async Task DownloadAsync(LogOptions options)
    {
        var download = await _mycelium.DownloadLogAsync(options.Service);
        var destination = options.File ?? download.FileName;
        await using (var content = download.Content)
        await using (var target = File.Create(destination))
        {
            await content.CopyToAsync(target);
        }

        _writer.WriteLine($"Log saved to {destination}");
    }

    private void ShowUsage()
    {
        _writer.WriteLine("Usage: logs tail [--lines=N] [--service=<name>]        - The last lines of the log");
        _writer.WriteLine("       logs follow [--for=SECONDS] [--service=<name>]  - Print lines as they are appended, then return");
        _writer.WriteLine("       logs download [--service=<name>] [file]         - Save the whole current log file");
        _writer.WriteLine();
        _writer.WriteLine("Without --service the Mycelium's own log is read; with it, the named service daemon's.");
        _writer.WriteLine($"A follow ends after --for seconds (default {DefaultFollowSeconds}).");
    }

    private sealed class LogOptions(IEnumerable<string> tokens)
    {
        public int? Lines { get; } = CommandOptions.Number(tokens, "--lines");
        public int? ForSeconds { get; } = CommandOptions.Number(tokens, "--for");
        public string? Service { get; } = CommandOptions.Value(tokens, "--service");
        public string? File { get; } = CommandOptions.Positional(tokens).FirstOrDefault();
    }
}
