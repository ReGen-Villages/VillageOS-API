using System.Runtime.CompilerServices;
using System.Text.Json;

namespace vos.Taproot.Tests;

internal static class BrokerAnswers
{
    public static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    public static async IAsyncEnumerable<ServerSentEvent> Events(params ServerSentEvent[] events)
    {
        foreach (var received in events)
        {
            await Task.Yield();
            yield return received;
        }
    }

    public static async IAsyncEnumerable<ServerSentEvent> EndlessAfter([EnumeratorCancellation] CancellationToken token, params ServerSentEvent[] events)
    {
        foreach (var received in events)
            yield return received;
        await Task.Delay(Timeout.Infinite, token);
    }
}
