namespace vos.ManagedMicroservice.Tributary.Services;

// The outcome of an endpoint call, free of any HTTP-framework (IResult) type so it can be
// consumed by both the legacy /handle endpoint (which maps it back to the exact HTTP shapes it
// always returned) and the pipeline DAG node (Feature #5628). Exactly one of Error,
// Ingest, or Content is set.
public sealed class EndpointCallResult
{
    public EndpointCallError? Error { get; private init; }
    public IngestSummary? Ingest { get; private init; }
    public string? Content { get; private init; }
    public string? ContentType { get; private init; }

    public bool IsSuccess => Error is null;

    public static EndpointCallResult Failure(EndpointCallError error) => new() { Error = error };
    public static EndpointCallResult Ingested(IngestSummary ingest) => new() { Ingest = ingest };
    public static EndpointCallResult Body(string content, string? contentType) =>
        new() { Content = content, ContentType = contentType };
}

// A failure carrying the exact HTTP status the legacy endpoint returned, plus a human message
// for the node failure path.
public abstract record EndpointCallError(int StatusCode, string Message);

// A plain-JSON error body — the BadRequest/NotFound shape { error, … }.
public sealed record JsonError(int StatusCode, object Body, string Message) : EndpointCallError(StatusCode, Message);

// A ProblemDetails error (application/problem+json), as Results.Problem produced.
public sealed record ProblemError(int StatusCode, string Title, string Detail) : EndpointCallError(StatusCode, Detail);

// The summary returned when a response transform ingests readings as observations.
public sealed record IngestSummary(Guid EndpointThingId, int EntitiesTouched, int ObservationsSubmitted);
