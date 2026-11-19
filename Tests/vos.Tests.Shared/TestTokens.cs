using System.Text;
using System.Text.Json;

namespace vos.Tests.Shared;

// Unsigned tokens shaped like the ones Mycelium mints. Nothing here validates a signature — the
// service reads its own credential to learn which project it speaks for and when to replace it.
public static class TestTokens
{
    public static string For(Guid modelId, DateTimeOffset? expiresAt = null, string scope = "endpoint:test:*") =>
        Jwt(new Dictionary<string, object>
        {
            ["vos:model_id"] = modelId.ToString(),
            ["vos:scope"] = scope,
            ["vos:token_type"] = "service",
            ["exp"] = (expiresAt ?? DateTimeOffset.UtcNow.AddHours(24)).ToUnixTimeSeconds(),
        });

    public static string Jwt(object payload) =>
        $"{Segment("{\"alg\":\"HS256\"}")}.{Segment(JsonSerializer.Serialize(payload))}.signature";

    private static string Segment(string json) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
