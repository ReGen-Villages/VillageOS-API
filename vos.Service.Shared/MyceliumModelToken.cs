using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace vos.Service.Shared;

// The model-scoped bearer the work in hand is acting under. A daemon shared by several models must call
// back into Mycelium with this token so it acts on that work's model, not the one that launched it. Two
// things set it: a /handle request, from the bearer that arrived, and a recompute driven by a change
// subscription, from the durable token held for that subscription's model. Ambient (AsyncLocal) so
// callbacks inherit it without threading it through every call.
public static class MyceliumModelToken
{
    private static readonly AsyncLocal<string?> _current = new();

    public static string? Current => _current.Value;

    internal static void Set(string? token) => _current.Value = token;

    /// <summary>Run work that belongs to a model but arrived on no request, under that model's token.</summary>
    public static async Task ActingForAsync(string token, Func<Task> work)
    {
        var restore = _current.Value;
        _current.Value = token;
        try
        {
            await work();
        }
        finally
        {
            _current.Value = restore;
        }
    }
}

public static class MyceliumModelTokenExtensions
{
    public static IApplicationBuilder UseMyceliumModelToken(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            MyceliumModelToken.Set(Bearer(context.Request.Headers.Authorization.ToString()));
            await next();
        });

    private static string? Bearer(string header) =>
        header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : null;
}
