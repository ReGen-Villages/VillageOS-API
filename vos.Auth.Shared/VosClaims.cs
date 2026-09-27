namespace vos.Auth.Shared;

// Claim names read off a Mycelium-signed token. Mycelium mints the claim, so a spelling that
// drifts from the platform's own copy authenticates nothing, and does so only at runtime.
public static class VosClaims
{
    public const string ModelId = "vos:model_id";

    // Which kind of credential the token is: a person's sign-in, a service's, a stream's, an API key's.
    public const string TokenType = "vos:token_type";
}
