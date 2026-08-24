using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace vos.Service.Shared.Subscriptions;

public interface ISubscriptionClient
{
    Task<SubscribeResult> SubscribeAsync(SubscriptionSelector selector, CancellationToken ct = default);
    Task<AddObjectsResult> AddObjectsAsync(Guid subscriptionId, SubscriptionSelector selector, CancellationToken ct = default);
    Task RemoveObjectsAsync(Guid subscriptionId, IEnumerable<Guid> objectIds, CancellationToken ct = default);
    Task UnsubscribeAsync(Guid subscriptionId, CancellationToken ct = default);
    IAsyncEnumerable<ModelChangeEvent> StreamAsync(Guid subscriptionId, long fromSequence, CancellationToken ct = default);

    /// <summary>Raised once the stream has re-established a dropped connection. Replay covers Facts, but a
    /// derived value (a roll-up recompute) is published live-only and is absent from the journal, so a
    /// subscriber computing from one must re-read after a gap rather than trust the resume.</summary>
    event Action? Reconnected;
}

// Subscribe once for a snapshot, then stream live changes over SSE, resuming via Last-Event-ID
// so no change is missed or double-applied across drops.
public sealed class SubscriptionClient : MyceliumClientBase, ISubscriptionClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(2);

    public event Action? Reconnected;

    public SubscriptionClient(
        IHttpClientFactory httpClientFactory, ILogger logger, string myceliumUrl, string? serviceToken = null,
        Func<Task<string?>>? tokenProvider = null, string? apiKey = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken, tokenProvider, apiKey)
    {
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscriptionSelector selector, CancellationToken ct = default)
    {
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(30));
        var response = await client.PostAsJsonAsync($"{MyceliumUrl}/api/subscriptions", selector, Json, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SubscribeResult>(Json, ct)
            ?? throw new InvalidOperationException("Mycelium returned an empty subscription response");
    }

    public async Task<AddObjectsResult> AddObjectsAsync(Guid subscriptionId, SubscriptionSelector selector, CancellationToken ct = default)
    {
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(30));
        var response = await client.PostAsJsonAsync($"{MyceliumUrl}/api/subscriptions/{subscriptionId}/objects", selector, Json, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AddObjectsResult>(Json, ct)
            ?? throw new InvalidOperationException("Mycelium returned an empty add-objects response");
    }

    public async Task RemoveObjectsAsync(Guid subscriptionId, IEnumerable<Guid> objectIds, CancellationToken ct = default)
    {
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10));
        var request = new HttpRequestMessage(HttpMethod.Delete, $"{MyceliumUrl}/api/subscriptions/{subscriptionId}/objects")
        {
            Content = JsonContent.Create(new { Ids = objectIds }, options: Json),
        };
        (await client.SendAsync(request, ct)).EnsureSuccessStatusCode();
    }

    public async Task UnsubscribeAsync(Guid subscriptionId, CancellationToken ct = default)
    {
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10));
        await client.DeleteAsync($"{MyceliumUrl}/api/subscriptions/{subscriptionId}", ct);
    }

    // Follow the subscription's live change stream, resuming after fromSequence
    // (typically the snapshot watermark). Yields each change in commit order exactly once,
    // reconnecting transparently on drops until ct is cancelled.
    public async IAsyncEnumerable<ModelChangeEvent> StreamAsync(
        Guid subscriptionId, long fromSequence, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var lastSequence = fromSequence;
        var everConnected = false;

        while (!ct.IsCancellationRequested)
        {
            var connection = await ConnectAsync(subscriptionId, lastSequence, ct);
            if (connection is null)
            {
                if (!await DelayReconnectAsync(ct)) yield break;
                continue;
            }

            if (everConnected) Reconnected?.Invoke();
            everConnected = true;

            using (connection.Response)
            {
                await foreach (var frame in SseEventReader.ReadAsync(connection.Stream, ct))
                {
                    var change = ParseChange(frame);
                    if (change is null || change.Sequence <= lastSequence) continue; // skip noise + replay overlap
                    lastSequence = change.Sequence;
                    yield return change;
                }
            }

            // Stream ended (server closed or network drop). Reconnect with Last-Event-ID.
            if (!await DelayReconnectAsync(ct)) yield break;
        }
    }

    private sealed record Connection(HttpResponseMessage Response, Stream Stream);

    private async Task<Connection?> ConnectAsync(Guid subscriptionId, long lastSequence, CancellationToken ct)
    {
        try
        {
            var client = await CreateAuthenticatedClientAsync(Timeout.InfiniteTimeSpan);
            var request = new HttpRequestMessage(HttpMethod.Get, $"{MyceliumUrl}/api/subscriptions/{subscriptionId}/stream");
            request.Headers.TryAddWithoutValidation("Accept", "text/event-stream");
            if (lastSequence > 0)
                request.Headers.TryAddWithoutValidation("Last-Event-ID", lastSequence.ToString());

            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync(ct);
            return new Connection(response, stream);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "SSE connect to subscription {SubscriptionId} failed; will retry", subscriptionId);
            return null;
        }
    }

    private static ModelChangeEvent? ParseChange(SseFrame frame)
    {
        if (string.IsNullOrEmpty(frame.Data)) return null;
        ModelChangeEvent? change;
        try { change = JsonSerializer.Deserialize<ModelChangeEvent>(frame.Data, Json); }
        catch (JsonException) { return null; }
        if (change is null) return null;
        return long.TryParse(frame.Id, out var seq) ? change with { Sequence = seq } : change;
    }

    // Delay before a reconnect; returns false if cancelled (caller should stop).
    private async Task<bool> DelayReconnectAsync(CancellationToken ct)
    {
        try { await Task.Delay(ReconnectDelay, ct); return !ct.IsCancellationRequested; }
        catch (OperationCanceledException) { return false; }
    }
}
