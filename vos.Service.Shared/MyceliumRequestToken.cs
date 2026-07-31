using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace vos.Service.Shared;

// The model-scoped bearer from the current /handle request. A daemon shared by several models must
// call back into Mycelium with this token so it acts on the request's model, not the one that
// launched it. Ambient (AsyncLocal) so callbacks inherit it without threading it through every call.
public static class MyceliumRequestToken
{
    private static readonly AsyncLocal<string?> _current = new();

    public static string? Current => _current.Value;

    internal static void Set(string? token) => _current.Value = token;
}

public static class MyceliumRequestTokenExtensions
{
    public static IApplicationBuilder UseMyceliumRequestToken(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            MyceliumRequestToken.Set(Bearer(context.Request.Headers.Authorization.ToString()));
            await next();
        });

    private static string? Bearer(string header) =>
        header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : null;
}
