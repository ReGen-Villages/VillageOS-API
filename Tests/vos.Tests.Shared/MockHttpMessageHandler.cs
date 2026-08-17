namespace vos.Tests.Shared;

public sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
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
        _handler = (request, _) => Task.FromResult(handler(request));
    }

    /// <summary>For a subject whose calls should be able to overlap. A handler that answers synchronously
    /// runs each call to completion on the caller's thread, so calls started together still finish one
    /// after another and a test cannot tell that shape from a caller that awaited them in turn.</summary>
    /// <remarks>Named rather than a second constructor: a lambda body can satisfy both delegate types, so
    /// the overload made every existing call site ambiguous.</remarks>
    public static MockHttpMessageHandler AnsweringAsynchronously(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) => new((request, _) => handler(request));

    /// <summary>For a subject that bounds how long a call may take. HttpClient applies its timeout by
    /// passing a token down to the handler and awaiting it, so a handler that ignores that token hangs
    /// rather than timing out — which is why a slow upstream could not be expressed here before.</summary>
    public static MockHttpMessageHandler ObservingCancellation(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) => new(handler);

    private MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        _handler = handler;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_requestsLock)
            _requests.Add(request);

        return await _handler(request, cancellationToken);
    }
}
