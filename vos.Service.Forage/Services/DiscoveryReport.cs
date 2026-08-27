namespace vos.Service.Forage.Services;

// What one source's fetch came to. A source is resolved or it is not; an unresolved one always
// carries a reason, because a list that says only "not resolved" is the silently-short list this
// whole path exists to replace.
public sealed record SourceOutcome(string Source, bool Resolved, string? Reason);

// Everything one run did. Both halves are reported: what resolved is the answer, what did not is
// the gap the analysis has to be able to report against. The site is not among them — a report is
// handed straight back to the caller that named the site, and carrying its own copy only invited a
// reader to trust the copy.
public sealed record DiscoveryReport(
    IReadOnlyList<SourceOutcome> Resolved,
    IReadOnlyList<SourceOutcome> Unresolved);
