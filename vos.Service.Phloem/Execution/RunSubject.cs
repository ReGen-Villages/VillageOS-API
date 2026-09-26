namespace vos.Service.Phloem.Execution;

// The Thing a run is about: the one whose entry into a watched state, or whose relationship, started it.
public sealed record RunSubject(Guid Id, string Name);
