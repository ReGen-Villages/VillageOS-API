using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using vos.MyceliumClient;

namespace vos.Hypha.Registry;

public sealed class Caller(string role, MyceliumClient mycelium)
{
    private readonly ConcurrentDictionary<Guid, Task<bool>> _bound = new();

    public string Role { get; } = role;

    public Task<bool> BindsAsync(Guid roleIdentifier) =>
        _bound.GetOrAdd(roleIdentifier, async identifier => await mycelium.GetThingBoundByAsync(identifier) is not null);
}

// A caller is read again whenever Mycelium issues a new token, so a changed role or a newly bound role
// is seen within one token's life.
public sealed class CallerSource(MyceliumClient mycelium)
{
    private readonly Lock _gate = new();
    private string? _token;
    private Caller? _caller;

    public async Task<Caller> ReadAsync()
    {
        var token = await mycelium.GetTokenAsync();
        lock (_gate)
        {
            if (token != _token || _caller is null)
            {
                _token = token;
                _caller = new Caller(RoleIn(token), mycelium);
            }

            return _caller;
        }
    }

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
