using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using vos.Auth.Shared;
using vos.Service.Shared;

namespace vos.Service.Feedback;

public enum TokenHolderKind { Person, Service }

// Who a token the platform accepted belongs to. The model is named as the platform named it back, so
// a report cannot claim a model its token does not reach.
public sealed record TokenHolder(TokenHolderKind Kind, string Name, string? Role, string? ModelName);

public enum CallerVerdict { Accepted, Refused, Unreachable }

// Asks the platform whether it accepts a caller's token, by presenting it to a route every signed-in
// caller may read. The platform replaces its signing keys over time and hands the public half only to
// services it starts, so checking a signature here would stop working at the first replacement.
public sealed class PlatformCallers(IHttpClientFactory clients, string platformAddress)
{
    private const string SignedInRoute = "/api/models";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions AnswerFormat = new() { PropertyNameCaseInsensitive = true };

    private sealed record ModelEntry(Guid Id, string? Name);

    public async Task<(CallerVerdict Verdict, TokenHolder? Holder)> CheckAsync(string token, CancellationToken cancellation)
    {
        using var client = clients.CreateClient();
        client.Timeout = Patience;
        using var request = new HttpRequestMessage(HttpMethod.Get, platformAddress.TrimEnd('/') + SignedInRoute);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        ModelEntry[] models;
        try
        {
            using var response = await client.SendAsync(request, cancellation);
            if (!response.IsSuccessStatusCode)
                return ((int)response.StatusCode >= 500 ? CallerVerdict.Unreachable : CallerVerdict.Refused, null);
            models = await response.Content.ReadFromJsonAsync<ModelEntry[]>(AnswerFormat, cancellation) ?? [];
        }
        catch (Exception error) when (error is HttpRequestException or JsonException
                                      || (error is TaskCanceledException && !cancellation.IsCancellationRequested))
        {
            return (CallerVerdict.Unreachable, null);
        }

        return HolderOf(token, models) is { } holder
            ? (CallerVerdict.Accepted, holder)
            : (CallerVerdict.Refused, null);
    }

    // Only the two kinds that stand for somebody acting now. A stream token travels in an address and
    // lands in logs, and an API key's token names a key rather than a person.
    private static TokenHolder? HolderOf(string token, ModelEntry[] models)
    {
        if (JwtPayload.Read(token) is not { } payload) return null;

        var kind = Text(payload, VosClaims.TokenType) switch
        {
            "user" => TokenHolderKind.Person,
            "service" => TokenHolderKind.Service,
            _ => (TokenHolderKind?)null,
        };
        if (kind is null || Text(payload, ClaimTypes.Name) is not { Length: > 0 } name) return null;

        var modelName = Guid.TryParse(Text(payload, VosClaims.ModelId), out var modelId)
            ? models.FirstOrDefault(model => model.Id == modelId)?.Name
            : null;

        return new TokenHolder(kind.Value, name, Text(payload, ClaimTypes.Role), modelName);
    }

    private static string? Text(JsonElement payload, string claim) =>
        payload.TryGetProperty(claim, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
