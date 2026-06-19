using System.Runtime.CompilerServices;
using System.Text;

namespace vos.ManagedMicroservice.Shared.Subscriptions;

/// <summary>One parsed Server-Sent Events frame.</summary>
public sealed record SseFrame(string? Id, string? EventType, string Data);

/// <summary>
/// Minimal SSE wire parser (W3C event-stream): accumulates <c>id:</c>, <c>event:</c> and
/// (possibly multi-line) <c>data:</c> fields until a blank line dispatches the frame.
/// Comment lines (<c>:</c> heartbeats) are ignored. A mid-stream read failure ends the
/// enumeration cleanly so the caller can reconnect; only caller cancellation propagates.
/// </summary>
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
                yield break; // network drop / closed stream — let the caller reconnect
            }

            if (line is null) yield break; // EOF

            if (line.Length == 0) // blank line dispatches the accumulated frame
            {
                if (hasData)
                {
                    yield return new SseFrame(id, eventType, data.ToString());
                    id = null; eventType = null; data.Clear(); hasData = false;
                }
                continue;
            }

            if (line[0] == ':') continue; // comment / heartbeat

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
                // other fields (e.g. "retry") are ignored
            }
        }
    }

    private static (string field, string value) SplitField(string line)
    {
        var colon = line.IndexOf(':');
        if (colon < 0) return (line, ""); // field with no value
        var field = line[..colon];
        var value = line[(colon + 1)..];
        if (value.StartsWith(' ')) value = value[1..]; // a single leading space after the colon is stripped
        return (field, value);
    }
}
