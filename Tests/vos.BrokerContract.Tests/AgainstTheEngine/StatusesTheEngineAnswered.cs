using System.Collections.Concurrent;
using System.Net;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// A refusal is read from the status, because an answer's text carries identifiers the engine makes at random.
public sealed class StatusesTheEngineAnswered(HttpMessageHandler engine) : DelegatingHandler(engine)
{
    private readonly ConcurrentQueue<HttpStatusCode> _answered = new();

    public bool RefusedForWantOfAPolicy => _answered.Contains(HttpStatusCode.Forbidden);

    public void Forget() => _answered.Clear();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var answer = await base.SendAsync(request, cancellationToken);
        _answered.Enqueue(answer.StatusCode);
        return answer;
    }
}
