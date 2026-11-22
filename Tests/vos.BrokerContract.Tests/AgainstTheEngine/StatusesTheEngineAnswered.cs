using System.Collections.Concurrent;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

public sealed class StatusesTheEngineAnswered(HttpMessageHandler engine) : DelegatingHandler(engine)
{
    private readonly ConcurrentQueue<string> _answered = new();

    public bool RefusedForWantOfAPolicy => _answered.Any(body => body.Contains("403"));

    public void Forget() => _answered.Clear();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var answer = await base.SendAsync(request, cancellationToken);
        _answered.Enqueue(await answer.Content.ReadAsStringAsync(cancellationToken));
        return answer;
    }
}
