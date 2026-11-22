using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using vos.PlatformClient;

namespace vos.Hypha.Registry;

public sealed class Caller(string role, IReadOnlyList<MyceliumClient> models)
{
    private readonly ConcurrentDictionary<Guid, Task<bool>> _bound = new();

    public string Role { get; } = role;

    public Task<bool> BindsAsync(Guid roleIdentifier) =>
        _bound.GetOrAdd(roleIdentifier, async identifier =>
        {
            foreach (var model in models)
                if (await model.GetThingBoundByAsync(identifier) is not null)
                    return true;
            return false;
        });
}

// A caller is read again whenever Mycelium issues a new token, so a changed role or a newly bound role
// is seen within one token's life.
public sealed class CallerSource(MyceliumClient own, ModelClients clients)
{
    private readonly ConcurrentDictionary<MyceliumClient, (string Token, Caller Caller)> _read = new();

    // A key's role is the same in every model it reaches, so a key that names no model is read through each of them.
    public async Task<Caller> ReadAsync()
    {
        try
        {
            return await ReadAsync(own);
        }
        catch (ModelNotNamedException refused)
        {
            var models = refused.Models.Select(model => clients.For(model.Id)).ToList();
            return Held(own, await models[0].GetTokenAsync(), models);
        }
    }

    public async Task<Caller> ReadAsync(MyceliumClient mycelium) =>
        Held(mycelium, await mycelium.GetTokenAsync(), [mycelium]);

    private Caller Held(MyceliumClient readFor, string token, IReadOnlyList<MyceliumClient> judgedBy) =>
        _read.AddOrUpdate(
            readFor,
            _ => (token, new Caller(RoleIn(token), judgedBy)),
            (_, read) => read.Token == token ? read : (token, new Caller(RoleIn(token), judgedBy))).Caller;

    internal static string RoleIn(string token)
    {
        var segments = token.Split('.');
        if (segments.Length != 3) return "";

        var payload = segments[1].Replace('-', '+').Replace('_', '/');
        payload += new string('=', (4 - payload.Length % 4) % 4);
        try
        {
            using var claims = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            return claims.RootElement.TryGetProperty(ClaimTypes.Role, out var role) && role.ValueKind == JsonValueKind.String
                ? role.GetString()!
                : "";
        }
        catch (Exception unreadable) when (unreadable is FormatException or JsonException)
        {
            return "";
        }
    }
}
