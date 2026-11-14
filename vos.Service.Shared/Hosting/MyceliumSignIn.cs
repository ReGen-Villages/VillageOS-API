using Microsoft.AspNetCore.Builder;
using Serilog;
using vos.Service.Auth;

namespace vos.Service.Shared.Hosting;

public static class MyceliumSignIn
{
    public static bool AddMyceliumTokenAuthWhenKeyed(
        this WebApplicationBuilder builder, string? verificationKey, string? issuer, string? audience)
    {
        if (string.IsNullOrEmpty(verificationKey)) return false;

        builder.AddMyceliumTokenAuth(verificationKey, issuer, audience);
        Log.Information("JWT authentication enabled for incoming mycelium requests (issuer={Issuer}, audience={Audience})",
            issuer, audience);
        return true;
    }
}
