using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using vos.Service.Intake.Models;
using vos.Service.Shared;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Intake.Services;

public sealed class IntakeMyceliumClient(
    IHttpClientFactory httpClientFactory,
    ILogger<IntakeMyceliumClient> logger,
    string myceliumUrl,
    string? serviceToken,
    string? apiKey = null)
    : MyceliumClientBase(httpClientFactory, logger, myceliumUrl, serviceToken, apiKey: apiKey)
{
    private static readonly JsonSerializerOptions AsTheBrokerReadsIt = new(JsonSerializerDefaults.Web);

    /// <summary>A read of the model kept exactly as the broker wrote it, for one call.</summary>
    /// <remarks>
    /// Beside <see cref="ScopedRead"/> rather than through it, because that path reads a snapshot into
    /// <see cref="SnapshotDocument"/> — the fields a service was written to need. What a submitter is
    /// answered with is the broker's own envelope, so that it carries what draws each figure now and
    /// whatever the model starts declaring later; a typed read would silently drop the difference.
    /// <para>
    /// A release that fails must not lose a read that succeeded, and the broker reaps what a caller
    /// leaves behind — so the failure is logged where whoever runs the deployment reads it and the answer
    /// still goes back.
    /// </para>
    /// </remarks>
    public async Task<JsonDocument> ReadAsync(SubscriptionSelector selector, CancellationToken cancellation)
    {
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(30));
        var response = await client.PostAsJsonAsync(
            $"{MyceliumUrl}/api/subscriptions", selector, AsTheBrokerReadsIt, cancellation);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Reading the model failed ({(int)response.StatusCode} {response.StatusCode}).",
                null, response.StatusCode);

        var opened = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        try
        {
            if (opened.RootElement.TryGetProperty("subscriptionId", out var subscription)
                && subscription.TryGetGuid(out var identifier))
            {
                await ReleaseAsync(client, identifier, cancellation);
            }

            return JsonDocument.Parse(opened.RootElement.GetProperty("snapshot").GetRawText());
        }
        finally
        {
            opened.Dispose();
        }
    }

    /// <summary>One Thing's ranges, own and inherited, as the broker answers them. A verdict reads its
    /// target off the comparison a range makes, and a study's ranges sit on its archetype, so a reading
    /// of the Things alone cannot answer one.</summary>
    public async Task<JsonDocument?> ReadRangesAsync(Guid thingId, CancellationToken cancellation)
    {
        var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync($"{MyceliumUrl}/api/things/{thingId}/ranges", cancellation);

        // A verdict the model holds still reads without the target it names, so a Thing the broker will
        // not answer about leaves that row without its figure rather than refusing the whole page.
        if (!response.IsSuccessStatusCode)
        {
            Logger.LogWarning(
                "Intake could not read the ranges of {ThingId}: {Status}", thingId, (int)response.StatusCode);
            return null;
        }

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
    }

    /// <summary>One call through the broker's endpoint-forward route, answered with the body the
    /// forwarded service returned. Null is a call that was not answered — refused, failed, or out of
    /// time — logged by its status and never by its body, and the caller decides what standing that
    /// leaves the lookup in.</summary>
    public async Task<string?> CallEndpointAsync(
        string subdomain, object request, CancellationToken cancellation)
    {
        // The bound is this service's own: a provider that accepts the connection and then goes quiet
        // would otherwise hold a stranger's request open for as long as the forwarded service waits.
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        bounded.CancelAfter(TimeSpan.FromSeconds(20));

        try
        {
            var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(30));
            var response = await client.PostAsJsonAsync(
                $"{MyceliumUrl}/api/endpoints/{Uri.EscapeDataString(subdomain)}", request, bounded.Token);
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadAsStringAsync(bounded.Token);

            Logger.LogWarning("Intake's call through endpoint '{Subdomain}' was answered {Status}",
                subdomain, (int)response.StatusCode);
            return null;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            Logger.LogWarning("Intake's call through endpoint '{Subdomain}' ran out of time", subdomain);
            return null;
        }
        catch (HttpRequestException error)
        {
            Logger.LogWarning(error, "Intake's call through endpoint '{Subdomain}' failed", subdomain);
            return null;
        }
    }

    private async Task ReleaseAsync(HttpClient client, Guid subscriptionId, CancellationToken cancellation)
    {
        try
        {
            await client.DeleteAsync($"{MyceliumUrl}/api/subscriptions/{subscriptionId}", cancellation);
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Intake could not release the subscription {SubscriptionId}", subscriptionId);
        }
    }

    public async Task<Guid?> FindThingIdByNameAsync(string name, CancellationToken cancellation)
    {
        var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync($"{MyceliumUrl}/api/things?name={Uri.EscapeDataString(name)}", cancellation);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Looking up '{name}' failed ({(int)response.StatusCode} {response.StatusCode}).", null, response.StatusCode);

        var found = await response.Content.ReadFromJsonAsync<JsonElement>(cancellation);
        return found.ValueKind == JsonValueKind.Object
               && found.TryGetProperty("Id", out var identifier)
               && identifier.TryGetGuid(out var value)
            ? value
            : null;
    }

    /// <summary>Whether the model already holds the Thing under this identifier. Asked by identifier rather
    /// than by name because a name can answer with more than one Thing, and the caller derived this one.
    /// </summary>
    public async Task<bool> HoldsThingAsync(Guid identifier, CancellationToken cancellation)
    {
        var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync($"{MyceliumUrl}/api/things/{identifier}", cancellation);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return false;
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Looking up '{identifier}' failed ({(int)response.StatusCode} {response.StatusCode}).",
                null, response.StatusCode);

        return true;
    }

    public async Task ApplyFragmentAsync(ModelFragment fragment, CancellationToken cancellation)
    {
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(30));
        var document = JsonSerializer.Serialize(fragment);
        var response = await client.PostAsync(
            $"{MyceliumUrl}/api/model/fragment",
            new StringContent(document, Encoding.UTF8, "application/json"),
            cancellation);

        if (response.IsSuccessStatusCode)
            return;

        // The model's refusal names the Thing and the property it could not accept; a bare status code
        // throws that away and leaves the planner with nothing to correct.
        var refusal = await response.Content.ReadAsStringAsync(cancellation);
        throw new SubmissionError($"The model refused the submission: {refusal}");
    }
}
