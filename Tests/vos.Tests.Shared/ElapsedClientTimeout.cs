namespace vos.Tests.Shared;

// What HttpClient throws when its own Timeout elapses: an OperationCanceledException no caller asked for.
public static class ElapsedClientTimeout
{
    public static TaskCanceledException New() =>
        new("The request was canceled due to the configured HttpClient.Timeout of 5 seconds elapsing.", new TimeoutException());
}
