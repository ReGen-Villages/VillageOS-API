namespace vos.Service.Shared;

/// <summary>What a recompute answers: the figures it worked out, or the inputs the study holds no number
/// under yet. Exactly one of the two is filled in.
///
/// <para>A dispatch that finds an input missing has not failed. A study a submission built carries land, a
/// boundary and programme shares, so a reservoir capacity or a panel area is absent until a building model
/// exists — and a handler that threw on one had the broker record <c>__DispatchState=Failed</c> and re-drive
/// it on every reconciliation for the life of the model, which reads in the log exactly like a service that
/// is broken. The service answers what it is waiting for instead, writes nothing, and computes when the
/// input lands and the watch on the study wakes it.</para></summary>
public readonly record struct RecomputeAnswer<TOutputs>(TOutputs? Outputs, IReadOnlyList<string> WaitingFor)
    where TOutputs : class;
