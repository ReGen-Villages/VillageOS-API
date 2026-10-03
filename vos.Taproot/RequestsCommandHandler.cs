using System.Net;
using System.Text.Json;

namespace vos.Taproot;

// `requests latest`, `requests follow`, `requests download` and `requests show`: the broker's log of the
// requests it passed to services, which is kept outside the model.
public class RequestsCommandHandler
{
    public const int DefaultFollowSeconds = 30;

    private const string RequestEvent = "request";
    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss'Z'";

    private readonly string _arg;
    private readonly TextWriter _writer;
    private readonly MyceliumClient _mycelium;
    private readonly NameResolver _resolver;

    public RequestsCommandHandler(string arg, TextWriter writer, MyceliumClient mycelium)
    {
        _arg = arg ?? "";
        _writer = writer;
        _mycelium = mycelium;
        _resolver = new NameResolver(mycelium);
    }

    public async Task ExecuteAsync()
    {
        var tokens = _arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            ShowUsage();
            return;
        }

        var arguments = tokens.Skip(1).ToArray();
        try
        {
            switch (tokens[0].ToLowerInvariant())
            {
                case "latest": await LatestAsync(arguments); break;
                case "follow": await FollowAsync(arguments); break;
                case "download": await DownloadAsync(arguments); break;
                case "show": await ShowAsync(arguments); break;
                default: ShowUsage(); break;
            }
        }
        catch (Exception ex)
        {
            _writer.WriteLine("Error: " + OperatorMessage.For(ex));
        }
    }

    private async Task LatestAsync(string[] arguments)
    {
        var (resolved, connection) = await TryResolveConnectionAsync(arguments);
        if (!resolved) return;

        var entries = await _mycelium.GetLatestRequestsAsync(CommandOptions.Number(arguments, "--limit"), connection);
        if (entries.GetArrayLength() == 0)
        {
            _writer.WriteLine("No requests in the last day.");
            return;
        }

        foreach (var entry in entries.EnumerateArray())
            _writer.WriteLine(Line(entry));
    }

    private async Task FollowAsync(string[] arguments)
    {
        var (resolved, connection) = await TryResolveConnectionAsync(arguments);
        if (!resolved) return;

        var seconds = CommandOptions.Number(arguments, "--for") ?? DefaultFollowSeconds;
        using var window = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        try
        {
            await foreach (var received in _mycelium.FollowRequestsAsync(null, connection, window.Token))
            {
                if (received.Name == RequestEvent)
                    _writer.WriteLine(Line(JsonDocument.Parse(received.Data).RootElement));
            }
        }
        catch (OperationCanceledException) when (window.IsCancellationRequested)
        {
        }

        _writer.WriteLine($"Followed for {seconds} s.");
    }

    private async Task DownloadAsync(string[] arguments)
    {
        var hour = CommandOptions.Value(arguments, "--hour");
        if (await _mycelium.DownloadRequestsAsync(hour) is not { } download)
        {
            _writer.WriteLine(hour is null ? "No requests were recorded this hour." : $"No requests were recorded in hour {hour}.");
            return;
        }

        var destination = CommandOptions.Positional(arguments).FirstOrDefault() ?? download.FileName;
        await using (var content = download.Content)
        await using (var target = File.Create(destination))
        {
            await content.CopyToAsync(target);
        }

        _writer.WriteLine($"Requests saved to {destination}");
    }

    private async Task ShowAsync(string[] arguments)
    {
        if (arguments.Length == 0 || !Guid.TryParse(arguments[0], out var id))
        {
            _writer.WriteLine("Usage: requests show <request-id>");
            return;
        }

        JsonElement? found;
        try
        {
            found = await _mycelium.GetRequestAsync(id);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Gone)
        {
            _writer.WriteLine($"Request {id} is no longer kept: it was made before the period the request log keeps.");
            return;
        }

        if (found is not { } entry)
        {
            _writer.WriteLine($"No request {id} is in the log you may read.");
            return;
        }

        _writer.WriteLine($"Request {id}");
        _writer.WriteLine($"  Time:          {TimeOf(entry)}");
        _writer.WriteLine($"  Connection:    {entry.GetStringOrDefault("ConnectionName", "")} ({entry.GetStringOrDefault("ConnectionId", "")})");
        WriteIfPresent(entry, "Caller", "Caller");
        WriteIfPresent(entry, "SubjectId", "Subject");
        WriteIfPresent(entry, "RelationshipId", "Relationship");
        var status = entry.GetIntOrDefault("Status");
        var answered = status == 0 ? "nothing answered" : status.ToString();
        _writer.WriteLine($"  Answered:      {answered} after {DurationOf(entry)} ms");
        var kept = entry.GetIntOrDefault("BodyKeptBytes");
        _writer.WriteLine($"  Body:          {entry.GetIntOrDefault("BodyBytes")} bytes, {(kept == 0 ? "none" : $"{kept} bytes")} kept");
    }

    private void WriteIfPresent(JsonElement entry, string property, string label)
    {
        if (entry.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
            _writer.WriteLine($"  {label + ":",-15}{value.GetString()}");
    }

    private async Task<(bool Resolved, Guid? Connection)> TryResolveConnectionAsync(string[] arguments)
    {
        if (CommandOptions.Value(arguments, "--connection") is not { } named) return (true, null);

        var resolved = await _resolver.ResolveThingAsync(named);
        if (resolved.IsSuccess) return (true, resolved.Id);

        _writer.WriteLine($"Error: {resolved.ErrorMessage}");
        return (false, null);
    }

    private static string Line(JsonElement entry) =>
        $"{TimeOf(entry)}  {entry.GetIntOrDefault("Status"),3}  {DurationOf(entry),5} ms  "
        + $"{entry.GetStringOrDefault("ConnectionName", "")}  {entry.GetStringOrDefault("Id", "")}";

    private static string TimeOf(JsonElement entry) =>
        entry.GetProperty("Time").GetDateTimeOffset().UtcDateTime.ToString(TimeFormat);

    private static long DurationOf(JsonElement entry) => entry.GetProperty("DurationMs").GetInt64();

    private void ShowUsage()
    {
        _writer.WriteLine("Usage: requests latest [--limit=N] [--connection=<connection>]   - The newest requests of the last day, oldest first");
        _writer.WriteLine("       requests follow [--for=SECONDS] [--connection=<connection>] - Print requests as they are recorded, then return");
        _writer.WriteLine("       requests download [--hour=yyyyMMddHH] [file]              - Save one hour's requests, this hour's by default");
        _writer.WriteLine("       requests show <request-id>                                - One request in full");
        _writer.WriteLine();
        _writer.WriteLine("<connection> can be a GUID or a unique name. A line reads: time, status (0 when nothing");
        _writer.WriteLine("answered), how long the service took, the connection, the request's identifier.");
        _writer.WriteLine($"A follow ends after --for seconds (default {DefaultFollowSeconds}).");
    }
}
