namespace vos.Service.Tributary.Services;

// The outcome of an endpoint call, free of any HTTP-framework (IResult) type so the calling
// service maps it to its own response shape. Exactly one of Error, Ingest, or Content is set.
public sealed class EndpointCallResult
{
    public EndpointCallError? Error { get; private init; }
    public IngestSummary? Ingest { get; private init; }
    public string? Content { get; private init; }
    public string? ContentType { get; private init; }

    // The status the provider answered with, so a refusal does not reach the caller as a 200.
    public int StatusCode { get; private init; } = 200;

    public static EndpointCallResult Failure(EndpointCallError error) => new() { Error = error };
    public static EndpointCallResult Ingested(IngestSummary ingest) => new() { Ingest = ingest };
    public static EndpointCallResult Body(string content, string? contentType, int statusCode = 200) =>
        new() { Content = content, ContentType = contentType, StatusCode = statusCode };
}

// A failure carrying the exact HTTP status the legacy endpoint returned, plus a human message
// for the node failure path.
public abstract record EndpointCallError(int StatusCode, string Message);

// A plain-JSON error body — the BadRequest/NotFound shape { error, … }.
public sealed record JsonError(int StatusCode, object Body, string Message) : EndpointCallError(StatusCode, Message);

// A ProblemDetails error (application/problem+json), as Results.Problem produced.
public sealed record ProblemError(int StatusCode, string Title, string Detail) : EndpointCallError(StatusCode, Detail);

// The summary returned when a response transform ingests readings as observations. Written carries
// what a subject-supplied call put onto its subject and is null on the bulk path — see
// ObservationIngestResult for why the two paths answer differently.
public sealed record IngestSummary(
    Guid EndpointThingId,
    int EntitiesTouched,
    int ObservationsSubmitted,
    IReadOnlyDictionary<string, object?>? Written = null);
