namespace vos.ManagedMicroservice.Tributary.Services;

/// <summary>
/// The outcome of an endpoint call, free of any HTTP-framework (<c>IResult</c>) type so it can be
/// consumed by both the legacy <c>/handle</c> endpoint (which maps it back to the exact HTTP shapes it
/// always returned) and the pipeline DAG node (Feature #5628). Exactly one of <see cref="Error"/>,
/// <see cref="Ingest"/>, or <see cref="Content"/> is set.
/// </summary>
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

/// <summary>A failure carrying the exact HTTP status the legacy endpoint returned, plus a human message
/// for the node failure path.</summary>
public abstract record EndpointCallError(int StatusCode, string Message);

/// <summary>A plain-JSON error body — the <c>BadRequest</c>/<c>NotFound</c> shape <c>{ error, … }</c>.</summary>
public sealed record JsonError(int StatusCode, object Body, string Message) : EndpointCallError(StatusCode, Message);

/// <summary>A ProblemDetails error (<c>application/problem+json</c>), as <c>Results.Problem</c> produced.</summary>
public sealed record ProblemError(int StatusCode, string Title, string Detail) : EndpointCallError(StatusCode, Detail);

/// <summary>The summary returned when a response transform ingests readings as observations.</summary>
public sealed record IngestSummary(Guid EndpointThingId, int EntitiesTouched, int ObservationsSubmitted);
