/**
 * Why a service decided about one Thing, read from the marks the platform declares.
 *
 * A decision is a Thing. It reaches the subject it is about, what it chose, the rule it was decided
 * under, and the considerations that spoke for the choice or turned candidates away. A consideration
 * reaches its cause, and where that cause is a constraint the numbers are not written down: the
 * constraint names the properties instead, and the card reads them as they stood at the instant the
 * decision carries.
 *
 * Nothing here names a predicate, an archetype or a kind. What a model calls them is its own; what is
 * fixed is the flag each one carries.
 */
import type { ModelIndex } from '../../../api/dashboardApi';
import type { VosThing } from '../../../types/vos';

const IS_CONSTRAINT_ARCHETYPE = '__IsConstraintArchetype';

/** How the platform marks each predicate of the vocabulary, whatever this model calls them. */
const DECISION_FLAGS = {
  subject: '__IsDecisionSubjectPredicate',
  chosen: '__IsChosenPredicate',
  decidedUnder: '__IsDecidedUnderPredicate',
  refusal: '__IsRefusalPredicate',
  support: '__IsSupportPredicate',
  cause: '__IsConsiderationCausePredicate',
  cited: '__IsConsiderationEvidencePredicate',
  refusedCandidate: '__IsRefusedCandidatePredicate',
} as const;

type DecisionRole = keyof typeof DECISION_FLAGS;

const ROLE_FLAGS = Object.entries(DECISION_FLAGS) as [DecisionRole, string][];

const DECIDED_AT = 'decidedAt';
const SCORE = 'score';
const CANDIDATES_CONSIDERED = 'candidatesConsidered';
const CANDIDATES_REFUSED = 'candidatesRefused';
const MEASURED = 'measured';
const LIMIT = 'limit';
const MEASURE_SUBJECT_PROPERTY = 'measureSubjectProperty';
const MEASURE_CANDIDATE_PROPERTY = 'measureCandidateProperty';
const LIMIT_SUBJECT_PROPERTY = 'limitSubjectProperty';
const LIMIT_CANDIDATE_PROPERTY = 'limitCandidateProperty';
const UNIT = 'unit';

/** A Thing a decision names, carried by id so the card can open it and by name so it can say it. */
export interface NamedThing {
  id: string;
  name: string;
}

/** The four properties a constraint names and the unit they are compared in. A constraint naming none
 *  of them is a measure the model does not hold, and the consideration carries the number instead. */
export interface Constraint {
  measureSubjectProperty?: string;
  measureCandidateProperty?: string;
  limitSubjectProperty?: string;
  limitCandidateProperty?: string;
  unit?: string;
}

export interface Consideration {
  id: string;
  half: 'support' | 'refusal';
  cause?: NamedThing;
  /** The Thing the cause is about. It also stands in the candidate's place: a cause about a
   *  different Thing per candidate is written as one consideration per candidate, each citing its
   *  own, so a constraint's candidate side is read from what the consideration cites wherever it
   *  cites anything. */
  cited?: NamedThing;
  refusedCandidates: NamedThing[];
  /** Every candidate the refusal turned away, the ones past the naming bound included. */
  candidatesRefused?: number;
  measured?: number;
  limit?: number;
  constraint?: Constraint;
}

export interface DecisionExplanation {
  id: string;
  name: string;
  /** The kind the product declared below the marked archetype. */
  kindName?: string;
  decidedAt?: string;
  subject?: NamedThing;
  chose?: NamedThing;
  under?: NamedThing;
  score?: number;
  candidatesConsidered?: number;
  supports: Consideration[];
  refusals: Consideration[];
}

type DecisionWiring = Record<DecisionRole, Set<string>>;

/** Held against the index rather than rebuilt per card: the model cannot change without a new index,
 *  and several open cards ask the same question of it. */
const wiringByIndex = new WeakMap<ModelIndex, DecisionWiring>();

/**
 * The predicates owning each flag.
 *
 * Only the predicates that assert something are looked at, rather than every Thing in the model: one
 * asserting nothing reaches nothing, and a card open while a model runs would otherwise pay a walk
 * over the whole model on every flush.
 *
 * Read from what a predicate owns, never from what it inherited: a role flag is handed down to every
 * instance, which would make every decision look like a predicate. Several may own one flag — a model
 * is free to declare two words for turning a candidate away — and all of them count, because the
 * platform reads the mark rather than the word.
 */
function decisionWiring(modelIndex: ModelIndex): DecisionWiring {
  const remembered = wiringByIndex.get(modelIndex);
  if (remembered) return remembered;

  const wiring = Object.fromEntries(ROLE_FLAGS.map(([role]) => [role, new Set<string>()])) as DecisionWiring;
  for (const predicateId of modelIndex.relationshipsByPredicate.keys()) {
    // A relationship can name a predicate the read did not carry, because what a caller may read is
    // narrowed by the grants it holds while the relationships naming it are not.
    const predicate = modelIndex.byId.get(predicateId);
    if (!predicate) continue;
    for (const [role, flag] of ROLE_FLAGS) {
      if (predicate.Properties[flag] === true) wiring[role].add(predicateId);
    }
  }

  wiringByIndex.set(modelIndex, wiring);
  return wiring;
}

function named(id: string, modelIndex: ModelIndex): NamedThing {
  return { id, name: modelIndex.byId.get(id)?.Name ?? id };
}

function targetsOf(subjectId: string, predicateIds: Set<string>, modelIndex: ModelIndex): string[] {
  const targets: string[] = [];
  for (const predicateId of predicateIds) {
    for (const edge of modelIndex.relationshipsByPredicate.get(predicateId) ?? []) {
      if (edge.SubjectId === subjectId) targets.push(edge.TargetId);
    }
  }
  return targets;
}

function subjectsOf(targetId: string, predicateIds: Set<string>, modelIndex: ModelIndex): string[] {
  const subjects: string[] = [];
  for (const predicateId of predicateIds) {
    for (const edge of modelIndex.relationshipsByPredicate.get(predicateId) ?? []) {
      if (edge.TargetId === targetId) subjects.push(edge.SubjectId);
    }
  }
  return subjects;
}

/** Whether the Thing is of an archetype owning the flag, walking the `is`-chain upward. The walk
 *  starts above the Thing, because an archetype is not one of its own members: a Thing owning the
 *  flag is the declaration rather than something declared by it. */
function isOfArchetypeCarrying(thingId: string, flag: string, modelIndex: ModelIndex): boolean {
  const seen = new Set<string>([thingId]);
  const frontier = [...(modelIndex.isParents.get(thingId) ?? [])];
  while (frontier.length) {
    const current = frontier.pop()!;
    if (seen.has(current)) continue;
    seen.add(current);
    if (modelIndex.byId.get(current)?.Properties[flag] === true) return true;
    frontier.push(...(modelIndex.isParents.get(current) ?? []));
  }
  return false;
}

function numberOf(properties: Record<string, unknown>, name: string): number | undefined {
  const value = properties[name];
  return typeof value === 'number' ? value : undefined;
}

function textOf(properties: Record<string, unknown>, name: string): string | undefined {
  const value = properties[name];
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}

function constraintOf(cause: VosThing): Constraint {
  return {
    measureSubjectProperty: textOf(cause.Properties, MEASURE_SUBJECT_PROPERTY),
    measureCandidateProperty: textOf(cause.Properties, MEASURE_CANDIDATE_PROPERTY),
    limitSubjectProperty: textOf(cause.Properties, LIMIT_SUBJECT_PROPERTY),
    limitCandidateProperty: textOf(cause.Properties, LIMIT_CANDIDATE_PROPERTY),
    unit: textOf(cause.Properties, UNIT),
  };
}

function considerationOf(
  considerationId: string,
  half: 'support' | 'refusal',
  wiring: DecisionWiring,
  modelIndex: ModelIndex,
): Consideration {
  const properties = modelIndex.byId.get(considerationId)?.Properties ?? {};
  const causeId = targetsOf(considerationId, wiring.cause, modelIndex)[0];
  const citedId = targetsOf(considerationId, wiring.cited, modelIndex)[0];
  const cause = causeId ? modelIndex.byId.get(causeId) : undefined;

  return {
    id: considerationId,
    half,
    cause: causeId ? named(causeId, modelIndex) : undefined,
    cited: citedId ? named(citedId, modelIndex) : undefined,
    refusedCandidates: targetsOf(considerationId, wiring.refusedCandidate, modelIndex).map((id) =>
      named(id, modelIndex),
    ),
    candidatesRefused: numberOf(properties, CANDIDATES_REFUSED),
    measured: numberOf(properties, MEASURED),
    limit: numberOf(properties, LIMIT),
    constraint:
      cause && isOfArchetypeCarrying(cause.Id, IS_CONSTRAINT_ARCHETYPE, modelIndex)
        ? constraintOf(cause)
        : undefined,
  };
}

function explanationOf(decisionId: string, wiring: DecisionWiring, modelIndex: ModelIndex): DecisionExplanation {
  const decision = modelIndex.byId.get(decisionId);
  const properties = decision?.Properties ?? {};
  const subjectId = targetsOf(decisionId, wiring.subject, modelIndex)[0];
  const choseId = targetsOf(decisionId, wiring.chosen, modelIndex)[0];
  const underId = targetsOf(decisionId, wiring.decidedUnder, modelIndex)[0];
  const kindId = modelIndex.isParents.get(decisionId)?.[0];

  return {
    id: decisionId,
    name: decision?.Name ?? decisionId,
    kindName: kindId ? modelIndex.byId.get(kindId)?.Name : undefined,
    decidedAt: textOf(properties, DECIDED_AT),
    subject: subjectId ? named(subjectId, modelIndex) : undefined,
    chose: choseId ? named(choseId, modelIndex) : undefined,
    under: underId ? named(underId, modelIndex) : undefined,
    score: numberOf(properties, SCORE),
    candidatesConsidered: numberOf(properties, CANDIDATES_CONSIDERED),
    supports: targetsOf(decisionId, wiring.support, modelIndex).map((id) =>
      considerationOf(id, 'support', wiring, modelIndex),
    ),
    refusals: targetsOf(decisionId, wiring.refusal, modelIndex).map((id) =>
      considerationOf(id, 'refusal', wiring, modelIndex),
    ),
  };
}

/**
 * Every decision this Thing is the subject of, latest first, and the Thing itself where it is a
 * decision — a decision's own card opens on what it decided rather than saying nothing about it.
 *
 * Two decisions taken at the same instant are ordered by their identifiers, which is how the platform
 * settles the tie when it retires the oldest, so a reader and the retirement agree on which is older.
 */
export function decisionsOn(thingId: string, modelIndex: ModelIndex): DecisionExplanation[] {
  const wiring = decisionWiring(modelIndex);
  const ids = new Set(subjectsOf(thingId, wiring.subject, modelIndex));
  if (targetsOf(thingId, wiring.subject, modelIndex).length > 0) ids.add(thingId);

  return [...ids]
    .map((id) => explanationOf(id, wiring, modelIndex))
    .sort((a, b) => {
      const byInstant = (b.decidedAt ?? '').localeCompare(a.decidedAt ?? '');
      return byInstant !== 0 ? byInstant : a.id.localeCompare(b.id);
    });
}

/** One Thing to be read as it stood at one instant. */
export interface InstantRead {
  thingId: string;
  instant: string;
}

export function instantReadKey(thingId: string, instant: string): string {
  return `${thingId}@${instant}`;
}

/**
 * The candidate side of a constraint for one consideration: the Thing the consideration cites where
 * it cites one, the candidates a refusal names, or what the decision chose. A support has no
 * candidate of its own, because the decision names what it chose once.
 */
function candidatesOf(decision: DecisionExplanation, consideration: Consideration): NamedThing[] {
  if (consideration.cited) return [consideration.cited];
  if (consideration.half === 'refusal') return consideration.refusedCandidates;
  return decision.chose ? [decision.chose] : [];
}

function readsTheCandidate(constraint: Constraint): boolean {
  return Boolean(constraint.measureCandidateProperty || constraint.limitCandidateProperty);
}

function readsTheSubject(constraint: Constraint): boolean {
  return Boolean(constraint.measureSubjectProperty || constraint.limitSubjectProperty);
}

/**
 * Every Thing a decision's constraints name a property on, each against that decision's own instant.
 *
 * Both halves of the key are written once and never change — a decision cannot be edited, and the
 * instant it carries is in the past — so a read answered once stays the answer, and the card never
 * asks again.
 */
export function instantReadsFor(decisions: DecisionExplanation[]): InstantRead[] {
  const wanted = new Map<string, InstantRead>();

  for (const decision of decisions) {
    const instant = decision.decidedAt;
    if (!instant) continue;

    const want = (thing: NamedThing | undefined) => {
      if (thing) wanted.set(instantReadKey(thing.id, instant), { thingId: thing.id, instant });
    };

    for (const consideration of [...decision.supports, ...decision.refusals]) {
      const constraint = consideration.constraint;
      if (!constraint) continue;
      if (readsTheSubject(constraint)) want(decision.subject);
      if (readsTheCandidate(constraint)) candidatesOf(decision, consideration).forEach(want);
    }
  }

  return [...wanted.values()];
}

/**
 * One measure against one limit.
 *
 * `measure` and `limit` are null where the platform did not answer the property at that instant —
 * a property declared current-only, or a past the eviction has already reclaimed. An absent `limit`
 * is the other thing entirely: the constraint declared none, so the measure reads as a value rather
 * than as a comparison.
 */
export interface Comparison {
  candidate?: NamedThing;
  measure: number | null;
  limit?: number | null;
  unit?: string;
}

/** A Thing as the platform answered it for an instant, or null where it answered nothing. */
export type InstantReadings = Map<string, VosThing | null>;

/**
 * The value stored under a name at the instant — own first, then the overrides an instance holds for
 * a name its archetype declares, which is where a seeded value and a service's write both land.
 *
 * What the archetype itself holds is deliberately not resolved: the platform leaves a property out of
 * an instant it cannot answer, and reading today's default in its place would put a number on the
 * card that nothing measured then.
 */
function valueAtTheInstant(thing: VosThing | null | undefined, name: string): number | undefined {
  if (!thing) return undefined;

  const own = numberOf(thing.Properties, name);
  if (own !== undefined) return own;

  for (const set of Object.values(thing.InheritedOverrides ?? {})) {
    const overridden = numberOf(set.Properties, name);
    if (overridden !== undefined) return overridden;
  }
  return undefined;
}

/**
 * What a consideration compares: one comparison per candidate whose own values the constraint reads,
 * and a single one where it reads none, because then every candidate would be given the same numbers.
 */
export function comparisonsOf(
  decision: DecisionExplanation,
  consideration: Consideration,
  readings: InstantReadings,
): Comparison[] {
  const constraint = consideration.constraint;
  if (!constraint) {
    return consideration.measured === undefined
      ? []
      : [{ measure: consideration.measured, limit: consideration.limit }];
  }

  const instant = decision.decidedAt;
  const read = (thing: NamedThing | undefined, name: string | undefined): number | undefined | null => {
    if (!name) return undefined;
    if (!thing || !instant) return null;
    return valueAtTheInstant(readings.get(instantReadKey(thing.id, instant)), name) ?? null;
  };

  const against = (candidate: NamedThing | undefined): Comparison => {
    const parts = [
      read(decision.subject, constraint.measureSubjectProperty),
      read(candidate, constraint.measureCandidateProperty),
    ].filter((part): part is number | null => part !== undefined);

    const measure = parts.length
      ? parts.includes(null)
        ? null
        : parts.reduce<number>((total, part) => total + (part ?? 0), 0)
      : (consideration.measured ?? null);

    const declaredLimit = constraint.limitSubjectProperty
      ? read(decision.subject, constraint.limitSubjectProperty)
      : read(candidate, constraint.limitCandidateProperty);

    return {
      candidate,
      measure,
      limit: declaredLimit === undefined ? consideration.limit : declaredLimit,
      unit: constraint.unit,
    };
  };

  if (!readsTheCandidate(constraint)) return [against(undefined)];

  const candidates = candidatesOf(decision, consideration);
  return candidates.length ? candidates.map(against) : [against(undefined)];
}
