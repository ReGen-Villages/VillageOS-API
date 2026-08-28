namespace vos.Service.Forage.Services;

// What one fetch came to. A call is resolved or it is not; an unresolved one always carries a
// reason, because a list that says only "not resolved" is the silently-short list this whole path
// exists to replace. Subject names the Thing the call was about where that is not the site itself —
// a portal answering for five of a site's assessments and not the sixth is reported per assessment,
// or the report would blame the source for the half it did resolve.
//
// SubjectId and Written are what the fetcher learned from the fetch response: which Thing the call
// was about, and the values the call put onto it. They are what the run resolves discovered words
// from (#6809) — the response rather than a read-back, because an observation is applied by the
// drainer after the write returns.
//
// SubjectId and SourceId together identify which call an outcome came from, where Source and Subject
// only name it: a run records the answer on the coverage of that subject by that source, and two
// Things may share a name.
public sealed record SourceOutcome(
    string Source,
    bool Resolved,
    string? Reason,
    string? Subject = null,
    Guid? SubjectId = null,
    IReadOnlyDictionary<string, string>? Written = null,
    Guid? SourceId = null);

// Everything one run did. Both halves are reported: what resolved is the answer, what did not is
// the gap the analysis has to be able to report against. The site is not among them — a report is
// handed straight back to the caller that named the site, and carrying its own copy only invited a
// reader to trust the copy.
public sealed record DiscoveryReport(
    IReadOnlyList<SourceOutcome> Resolved,
    IReadOnlyList<SourceOutcome> Unresolved);
