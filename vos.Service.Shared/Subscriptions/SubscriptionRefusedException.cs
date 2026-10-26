using System.Net;

namespace vos.Service.Shared.Subscriptions;

// Raised with the platform's own status and words, so a caller can pass a refusal on as given. It is
// an HttpRequestException so that a caller treating every failed call alike still catches it.
public sealed class SubscriptionRefusedException(HttpStatusCode statusCode, string body)
    : HttpRequestException($"the platform refused the subscription with {(int)statusCode}: {body}", null, statusCode)
{
    public string Body { get; } = body;
}
