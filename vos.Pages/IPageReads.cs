namespace vos.Pages;

// What reading a page asks of the model. Questions and answers are the JSON Mycelium's own routes
// take and give, so whoever fills this forwards them and the reader is the only code that interprets them.
public interface IPageReads
{
    Task<SelectedThings> SelectAsync(string selectorJson, CancellationToken cancellation);

    Task<string> ThingsInStateAsync(string state, string? narrowingJson, CancellationToken cancellation);

    // Null where the ranges could not be read: a verdict the model holds still reads, without its target.
    Task<string?> ThingRangesAsync(string thingId, CancellationToken cancellation);

    Task<string> AggregateAsync(string queryJson, CancellationToken cancellation);

    Task<string> ReduceAsync(string queryJson, CancellationToken cancellation);

    Task<string> FromServiceAsync(string endpoint, string? bodyJson, CancellationToken cancellation);
}

public sealed record SelectedThings(string ThingsJson, string RelationshipsJson);
