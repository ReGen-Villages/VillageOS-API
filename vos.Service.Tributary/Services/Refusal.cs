namespace vos.Service.Tributary.Services;

// What a check hands back when the endpoint's settings will not do. The words are read by two
// audiences — the caller, as the body's `error`, and the log, as the message — and they are held
// once here so that both are derived from the same string. Typed out twice at each check, the two
// drift the first time someone edits one and misses the other, and nothing notices.
//
// Detail is what rides alongside the words for the caller alone: the paths a key matched, the
// placeholders left unfilled, a compiler's own message.
public sealed record Refusal(int StatusCode, string Error, IReadOnlyDictionary<string, object?>? Detail = null)
{
    public static Refusal BadRequest(string error) => new(400, error);

    public EndpointCallResult ToResult()
    {
        var body = new Dictionary<string, object?> { ["error"] = Error };
        if (Detail != null)
            foreach (var (key, value) in Detail)
                body[key] = value;
        return EndpointCallResult.Failure(new JsonError(StatusCode, body, Error));
    }
}
