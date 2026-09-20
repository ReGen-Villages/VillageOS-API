using System.Runtime.CompilerServices;
using System.Text;

namespace vos.Taproot;

public readonly record struct ServerSentEvent(string Name, string Data);

// The platform streams log lines and model events in the server-sent events text form: `event:` names
// the event, one or more `data:` lines carry it, a blank line ends it, and a line starting with a
// colon is a comment the platform sends as a heartbeat.
public static class ServerSentEvents
{
    private const string DefaultName = "message";

    public static async IAsyncEnumerable<ServerSentEvent> ReadAsync(
        TextReader reader, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? name = null;
        var data = new StringBuilder();
        var hasData = false;

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length == 0)
            {
                if (hasData)
                    yield return new ServerSentEvent(name ?? DefaultName, data.ToString());
                name = null;
                data.Clear();
                hasData = false;
                continue;
            }

            if (line[0] == ':')
                continue;

            var (field, value) = Split(line);
            switch (field)
            {
                case "event":
                    name = value;
                    break;
                case "data":
                    if (hasData) data.Append('\n');
                    data.Append(value);
                    hasData = true;
                    break;
            }
        }
    }

    private static (string Field, string Value) Split(string line)
    {
        var colon = line.IndexOf(':');
        if (colon < 0) return (line, "");
        var value = line[(colon + 1)..];
        if (value.StartsWith(' ')) value = value[1..];
        return (line[..colon], value);
    }
}
