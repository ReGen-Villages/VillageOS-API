using System.Net.Http.Headers;

namespace vos.PlatformClient;

// The token goes on each request as it leaves, so calls made at the same time share nothing they write.
internal sealed class TokenAttachingHandler(Func<Task<string>> token, Uri mycelium, HttpMessageHandler transport)
    : DelegatingHandler(transport)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization is null && IsForMycelium(request) && !request.Headers.Contains(MyceliumClient.ApiKeyHeader))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await token());

        return await base.SendAsync(request, cancellationToken);
    }

    private bool IsForMycelium(HttpRequestMessage request) =>
        request.RequestUri is { } asked
        && Uri.Compare(asked, mycelium, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;
}
