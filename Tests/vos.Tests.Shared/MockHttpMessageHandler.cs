namespace vos.Tests.Shared;

public sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
    private readonly List<HttpRequestMessage> _requests = new();
    private readonly Lock _requestsLock = new();

    // A subject that registers and withdraws on its own schedule can be in two calls at once.
    public IReadOnlyList<HttpRequestMessage> Requests
    {
        get
        {
            lock (_requestsLock)
                return _requests.ToArray();
        }
    }

    public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_requestsLock)
            _requests.Add(request);

        return Task.FromResult(_handler(request));
    }
}
