namespace vos.Tests.Shared;

// Hands out a fresh client per call, the way the real factory does. Tests whose subject makes more
// than one call need this: a client's timeout cannot be set again once a request is in flight, so a
// single shared client fails the second call rather than the code under test being at fault.
public sealed class PerCallHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;

    public PerCallHttpClientFactory(HttpMessageHandler handler)
    {
        _handler = handler;
    }

    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}
