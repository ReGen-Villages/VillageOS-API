using System.Runtime.CompilerServices;
using System.Text;

namespace vos.Service.Shared.Subscriptions;

public sealed record SseFrame(string? Id, string? EventType, string Data);

// Reads the server-sent events format. A read that fails part way through ends the events cleanly so
// the caller can reconnect; only the caller's cancellation is thrown.
public static class SseEventReader
{
    public static async IAsyncEnumerable<SseFrame> ReadAsync(
        Stream stream, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? id = null, eventType = null;
        var data = new StringBuilder();
        var hasData = false;

        while (!ct.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                yield break; // the connection dropped or closed, and the caller reconnects
            }

            if (line is null) yield break;

            if (line.Length == 0) // blank line delivers the accumulated frame
            {
                if (hasData)
                {
                    yield return new SseFrame(id, eventType, data.ToString());
                    id = null; eventType = null; data.Clear(); hasData = false;
                }
                continue;
            }

            if (line[0] == ':') continue; // a comment, which the broker sends to keep the connection open

            var (field, value) = SplitField(line);
            switch (field)
            {
                case "id": id = value; break;
                case "event": eventType = value; break;
                case "data":
                    if (hasData) data.Append('\n');
                    data.Append(value);
                    hasData = true;
                    break;
            }
        }
    }

    private static (string field, string value) SplitField(string line)
    {
        var colon = line.IndexOf(':');
        if (colon < 0) return (line, "");
        var field = line[..colon];
        var value = line[(colon + 1)..];
        if (value.StartsWith(' ')) value = value[1..]; // the format drops one space after the colon
        return (field, value);
    }
}
