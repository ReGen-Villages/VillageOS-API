using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace vos.Service.Shared.Subscriptions;

public interface ISubscriptionClient
{
    Task<SubscribeResult> SubscribeAsync(SubscriptionSelector selector, CancellationToken ct = default);
    Task<SnapshotReadResult> ReadSnapshotAsync(SubscriptionSelector selector, CancellationToken ct = default);
    Task<AddObjectsResult> AddObjectsAsync(Guid subscriptionId, SubscriptionSelector selector, CancellationToken ct = default);
    Task RemoveObjectsAsync(Guid subscriptionId, IEnumerable<Guid> objectIds, CancellationToken ct = default);
    Task UnsubscribeAsync(Guid subscriptionId, CancellationToken ct = default);
    IAsyncEnumerable<ModelChangeEvent> StreamAsync(Guid subscriptionId, long fromSequence, CancellationToken ct = default);

    // Raised once a stream is open again after a gap, whether on the same subscription or on one opened because
    // the broker no longer held it. Replay covers Facts, but a derived value (a derived-property recompute) is
    // published live-only and is absent from the journal, so a subscriber computing from one must re-read after
    // a gap rather than trust the resume.
    event Action? Reconnected;
}

// Subscribe once for a snapshot, then stream live changes, resuming from the last event id seen so no
// change is missed or applied twice across a dropped connection.
public sealed class SubscriptionClient : MyceliumClientBase, ISubscriptionClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(2);

    public event Action? Reconnected;

    private bool _streamWasLost;

    public SubscriptionClient(
        IHttpClientFactory httpClientFactory, ILogger logger, string myceliumUrl, LaunchToken? launchToken = null,
        Func<Task<string?>>? tokenProvider = null, string? apiKey = null)
        : base(httpClientFactory, logger, myceliumUrl, launchToken, tokenProvider, apiKey)
    {
    }

    public Task<SubscribeResult> SubscribeAsync(SubscriptionSelector selector, CancellationToken ct = default) =>
        PostSelectorAsync<SubscribeResult>("subscriptions", "the subscription", selector, ct);

    // The broker registers nothing for this read, so a reader that wants one snapshot leaves nothing to check on later writes.
    public Task<SnapshotReadResult> ReadSnapshotAsync(SubscriptionSelector selector, CancellationToken ct = default) =>
        PostSelectorAsync<SnapshotReadResult>("snapshots", "the read", selector, ct);

    private async Task<T> PostSelectorAsync<T>(string route, string whatIsAsked, SubscriptionSelector selector, CancellationToken ct)
    {
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(30));
        var response = await client.PostAsJsonAsync($"{MyceliumUrl}/api/{route}", selector, Json, ct);
        if (!response.IsSuccessStatusCode)
            throw new SubscriptionRefusedException(response.StatusCode, await response.Content.ReadAsStringAsync(ct), whatIsAsked);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct)
            ?? throw new InvalidOperationException($"Mycelium answered {whatIsAsked} with nothing");
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
    // reconnecting transparently on drops until ct is cancelled or the broker no longer holds the subscription.
    public async IAsyncEnumerable<ModelChangeEvent> StreamAsync(
        Guid subscriptionId, long fromSequence, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var lastSequence = fromSequence;

        while (!ct.IsCancellationRequested)
        {
            var (connection, gone) = await ConnectAsync(subscriptionId, lastSequence, ct);
            if (gone)
            {
                _streamWasLost = true;
                // Waited before ending, so a broker that holds none of the subscriptions it opens is not asked in a tight loop.
                await DelayReconnectAsync(ct);
                yield break;
            }
            if (connection is null)
            {
                if (!await DelayReconnectAsync(ct)) yield break;
                continue;
            }

            if (_streamWasLost) Reconnected?.Invoke();

            using (connection.Response)
            {
                await foreach (var frame in SseEventReader.ReadAsync(connection.Stream, ct))
                {
                    var change = ParseChange(frame);
                    if (change is null) continue;
                    if (change.Sequence > 0)
                    {
                        if (change.Sequence <= lastSequence) continue; // a resumed stream can repeat what was already seen
                        lastSequence = change.Sequence;
                    }
                    yield return change; // an entry or departure carries no id and moves nothing; the change after it does
                }
            }

            _streamWasLost = true;
            // Stream ended (server closed or network drop). Reconnect with Last-Event-ID.
            if (!await DelayReconnectAsync(ct)) yield break;
        }
    }

    private sealed record Connection(HttpResponseMessage Response, Stream Stream);

    private async Task<(Connection? Connection, bool Gone)> ConnectAsync(Guid subscriptionId, long lastSequence, CancellationToken ct)
    {
        try
        {
            var client = await CreateAuthenticatedClientAsync(Timeout.InfiniteTimeSpan);
            var request = new HttpRequestMessage(HttpMethod.Get, $"{MyceliumUrl}/api/subscriptions/{subscriptionId}/stream");
            request.Headers.TryAddWithoutValidation("Accept", "text/event-stream");
            if (lastSequence > 0)
                request.Headers.TryAddWithoutValidation("Last-Event-ID", lastSequence.ToString());

            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                response.Dispose();
                Logger.LogWarning("The broker no longer holds subscription {SubscriptionId}, so its stream ends", subscriptionId);
                return (null, true);
            }
            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync(ct);
            return (new Connection(response, stream), false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return (null, false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "SSE connect to subscription {SubscriptionId} failed; will retry", subscriptionId);
            return (null, false);
        }
    }

    private static ModelChangeEvent? ParseChange(SseFrame frame)
    {
        if (string.IsNullOrEmpty(frame.Data) || string.IsNullOrEmpty(frame.EventType)) return null;
        ModelChangeEvent? change;
        try { change = JsonSerializer.Deserialize<ModelChangeEvent>(frame.Data, Json); }
        catch (JsonException) { return null; }
        if (change is null) return null;
        return change with
        {
            Kind = frame.EventType,
            Sequence = long.TryParse(frame.Id, out var sequence) ? sequence : change.Sequence,
        };
    }

    // Delay before a reconnect; returns false if cancelled (caller should stop).
    private async Task<bool> DelayReconnectAsync(CancellationToken ct)
    {
        try { await Task.Delay(ReconnectDelay, ct); return !ct.IsCancellationRequested; }
        catch (OperationCanceledException) { return false; }
    }
}
