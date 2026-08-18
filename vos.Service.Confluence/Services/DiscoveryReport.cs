namespace vos.Service.Confluence.Services;

// What one source's fetch came to. A source is resolved or it is not; an unresolved one always
// carries a reason, because a list that says only "not resolved" is the silently-short list this
// whole path exists to replace.
public sealed record SourceOutcome(string Source, bool Resolved, string? Reason);

// Everything one run did. Both halves are reported: what resolved is the answer, what did not is
// the gap the analysis has to be able to report against.
public sealed record DiscoveryReport(
    Guid SiteId,
    IReadOnlyList<SourceOutcome> Resolved,
    IReadOnlyList<SourceOutcome> Unresolved);
