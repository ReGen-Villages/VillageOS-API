/**
 * Pure helpers for the generic entity-detail window. No React, no I/O — the hook
 * (useEntityDetail) fetches; these shape the results. Kept model-agnostic: predicate
 * names and property keys arrive via the {@link DetailSpec}, never hardcoded.
 */
import type { ModelIndex } from '../../../api/dashboardApi';
import type { DetailSpec } from '../../../types/dashboard';
import type { MutationDto, PropertyFact, ThingMutations } from '../../../types/vos';

/**
 * Ids of the Things involved in a root Thing — whatever the model links to it.
 * Breadth-first over the model's relationships, following the configured predicates/direction up
 * to `depth` hops. Cycle-guarded (a Thing is visited once). Excludes the root. Omitting
 * `involves.predicates` follows every predicate.
 */
export function collectInvolved(
  rootId: string,
  idx: ModelIndex,
  involves?: DetailSpec['involves'],
): string[] {
  const depth = involves?.depth ?? 2;
  const direction = involves?.direction ?? 'both';
  const predicateIds = involves?.predicates?.length
    ? new Set(
        involves.predicates
          .map((name) => idx.predicateNameToId.get(name))
          .filter((id): id is string => !!id),
      )
    : null;
  const followOut = direction !== 'in';
  const followIn = direction !== 'out';

  const involved = new Set<string>();
  const seen = new Set<string>([rootId]);
  let frontier = new Set<string>([rootId]);

  for (let hop = 0; hop < depth && frontier.size; hop++) {
    const next = new Set<string>();
    const visit = (from: string, to: string) => {
      if (!frontier.has(from) || seen.has(to)) return;
      seen.add(to);
      involved.add(to);
      next.add(to);
    };
    for (const rel of idx.relationships) {
      if (predicateIds && !predicateIds.has(rel.PredicateId)) continue;
      if (followOut) visit(rel.SubjectId, rel.TargetId);
      if (followIn) visit(rel.TargetId, rel.SubjectId);
    }
    frontier = next;
  }
  return [...involved];
}

/** A relationship among the involved subgraph, surfaced as a movement in the timeline. */
export interface MovementInput {
  predicate: string;
  subjectId: string;
  subjectName: string;
  targetId: string;
  targetName: string;
  /** ISO time of the earliest known change to the edge, or null when unknown. */
  time: string | null;
  sequence?: number;
}

export type TimelineKind = 'change' | 'movement';

export interface TimelineEvent {
  time: string | null;
  sequence: number;
  kind: TimelineKind;
  thingId: string;
  thingName: string;
  label: string;
  detail?: string;
  /** The service that wrote the value (the "sender" of the message), when known. */
  author?: string;
}

function displayValue(value: unknown): string {
  if (value === null || value === undefined || value === '') return '∅';
  return String(value);
}

/** Best-effort author of a property change: the Fact whose value matches, else the nearest in time. */
function attributeFact(
  mutation: MutationDto,
  facts: PropertyFact[] | undefined,
): { author?: string; sequence?: number } {
  if (!facts?.length) return {};
  const target = displayValue(mutation.NewValue);
  const byValue = facts.find((f) => f.kind === 'asserted' && displayValue(f.value) === target);
  if (byValue) return { author: byValue.author, sequence: byValue.sequenceNumber };
  const changeTime = new Date(mutation.Timestamp).getTime();
  let best: PropertyFact | undefined;
  let bestGap = Infinity;
  for (const fact of facts) {
    const gap = Math.abs(new Date(fact.committedAt).getTime() - changeTime);
    if (gap < bestGap) {
      bestGap = gap;
      best = fact;
    }
  }
  return best ? { author: best.author, sequence: best.sequenceNumber } : {};
}

/**
 * Merge property changes and relationship movements into one chronological timeline.
 * Property changes carry their writing service (author) from the Commit-Log Facts when available.
 * Ordering is by (time ascending, then commit sequence); events with no known time sort last.
 */
export function mergeTimeline(input: {
  mutations: ThingMutations[];
  movements: MovementInput[];
  factsByKey?: Map<string, PropertyFact[]>;
}): TimelineEvent[] {
  const events: TimelineEvent[] = [];

  for (const thing of input.mutations) {
    for (const mutation of thing.Mutations) {
      const key = `${thing.ObjectId}::${mutation.PropertyName}`;
      const { author, sequence } = attributeFact(mutation, input.factsByKey?.get(key));
      events.push({
        time: mutation.Timestamp,
        sequence: sequence ?? 0,
        kind: 'change',
        thingId: thing.ObjectId,
        thingName: thing.ObjectName,
        label: `${mutation.PropertyName}: ${displayValue(mutation.OldValue)} → ${displayValue(mutation.NewValue)}`,
        author,
      });
    }
  }

  for (const movement of input.movements) {
    events.push({
      time: movement.time,
      sequence: movement.sequence ?? 0,
      kind: 'movement',
      thingId: movement.subjectId,
      thingName: movement.subjectName,
      label: `${movement.predicate} → ${movement.targetName}`,
      detail: movement.subjectName,
    });
  }

  events.sort((a, b) => {
    if (a.time && b.time) {
      const delta = new Date(a.time).getTime() - new Date(b.time).getTime();
      if (delta !== 0) return delta;
      return a.sequence - b.sequence;
    }
    if (a.time) return -1;
    if (b.time) return 1;
    return a.sequence - b.sequence;
  });

  return events;
}
