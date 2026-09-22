namespace vos.Service.Forage.Services;

// One call a run makes to work something out rather than to record a reading, answered with the
// provider's own body. The registration it goes through carries no reshape expression, so the fetching
// service writes nothing and hands the body back — which is what a caller needs when the answer has to be
// chosen between rather than written down.
//
// Null is a call that was not answered. A run must never read a provider it could not reach as a provider
// that answered nothing: the first is retried, the second is settled.
public interface IEndpointBodyReader
{
    Task<string?> ReadAsync(
        string endpointName,
        IReadOnlyDictionary<string, string> addressParameters,
        CancellationToken cancellationToken);
}
