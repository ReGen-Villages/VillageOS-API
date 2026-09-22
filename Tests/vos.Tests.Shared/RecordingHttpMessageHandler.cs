namespace vos.Tests.Shared;

// What each request said, captured as text.
public sealed record RecordedRequest(HttpMethod Method, string Uri, string Body);

// Records what a subject sent — method, address and body — and answers each call from a
// supplied lambda.
//
// Distinct from MockHttpMessageHandler, which keeps the live
// HttpRequestMessage: a request's content is disposed with the request, so a body read
// after the call has finished is empty. Reading it here, while the call is still in flight, is what
// lets a test assert on the value a service wrote rather than only on the route it wrote to.
public sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    : HttpMessageHandler
{
    public readonly List<RecordedRequest> Requests = new();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!.ToString(), body));
        return responder(request);
    }
}
