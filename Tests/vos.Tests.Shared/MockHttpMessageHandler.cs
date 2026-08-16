namespace vos.Tests.Shared;

public sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;
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
        : this(request => Task.FromResult(handler(request)))
    {
    }

    /// <summary>For a subject whose calls should be able to overlap. A handler that answers synchronously
    /// runs each call to completion on the caller's thread, so calls started together still finish one
    /// after another and a test cannot tell that shape from a caller that awaited them in turn.</summary>
    public MockHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    {
        _handler = handler;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_requestsLock)
            _requests.Add(request);

        return await _handler(request);
    }
}
