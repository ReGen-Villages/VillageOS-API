using System.Text;
using System.Text.Json;

namespace vos.Tests.Shared;

/// <summary>Unsigned tokens shaped like the ones Mycelium mints. Nothing here validates a signature — the
/// service reads its own credential to learn which project it speaks for and when to replace it.</summary>
public static class TestTokens
{
    public static string For(Guid modelId, DateTimeOffset expiresAt, string scope = "endpoint:test:*") =>
        Jwt(new Dictionary<string, object>
        {
            ["vos:model_id"] = modelId.ToString(),
            ["vos:scope"] = scope,
            ["exp"] = expiresAt.ToUnixTimeSeconds(),
        });

    public static string For(Guid modelId) => For(modelId, DateTimeOffset.UtcNow.AddHours(24));

    public static string Jwt(object payload) =>
        $"{Segment("{\"alg\":\"HS256\"}")}.{Segment(JsonSerializer.Serialize(payload))}.signature";

    private static string Segment(string json) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
