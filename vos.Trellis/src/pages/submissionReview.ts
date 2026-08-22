import type { EffectiveProperty, VosRelationship, VosThing } from '../types/vos';

/**
 * What has arrived in this model, and what a reviewer may do about it — the read behind the
 * submission review page.
 *
 * Nothing here names an archetype or a predicate. A submission is whatever asserts an edge through
 * the predicate the model marks as reaching a proposed site, and the dispositions are the Things
 * under the archetype the model marks as holding them. These are the same marks the `submissions`
 * commands read, and a test in this folder reads that handler's source so the page and the command
 * line cannot come to answer the same model differently.
 */

export const PROPOSED_SITE_PREDICATE_FLAG = '__IsProposedSitePredicate';
export const DISPOSITION_ARCHETYPE_FLAG = '__IsSubmissionDispositionArchetype';
export const DISPOSITION_PREDICATE_FLAG = '__IsSubmissionDispositionPredicate';

/** A disposition says what follows from the decision, and the period after which a submission goes
 *  is what makes one disposable. Rejecting relates a submission to whichever names a period, so a
 *  model may call that disposition anything. */
export const COLD_STORAGE_PERIOD_PROPERTY = 'daysBeforeColdStorage';

/** The platform's one canonical predicate, and the only predicate name a reader may hold: it is the
 *  platform's own vocabulary rather than any model's, and nothing marks it. */
const IS_PREDICATE_NAME = 'is';

/** Everything the page reads, taken together. Properties come from the server resolved effective
 *  rather than own: seed normalization moves a Thing's own values into its overrides, and a reader
 *  looking only at own properties finds a model full of Things and reads nothing off them. */
export interface ModelReading {
  things: readonly VosThing[];
  relationships: readonly VosRelationship[];
  properties: Readonly<Record<string, Record<string, EffectiveProperty>>>;
}

/** One submission as a reviewer needs to judge it: when it arrived, what it proposes, and what has
 *  been decided about it — or nothing, which is what waiting is. */
export interface Submission {
  id: string;
  name: string;
  submissionId?: string;
  submittedAt?: string;
  proposedSiteId: string;
  proposedSiteName?: string;
  disposition?: string;
}

export interface Disposition {
  id: string;
  name: string;
  /** True when this disposition names a period after which a submission goes. */
  disposable: boolean;
}

/** The predicate this model marks as reaching a proposed site. Nothing here is a submission without
 *  it, which is a different answer from a model that simply holds none. */
export function proposedSitePredicate(reading: ModelReading): string | null {
  return carrierOf(reading, PROPOSED_SITE_PREDICATE_FLAG);
}

/** The predicate a decision is written through. */
export function dispositionPredicate(reading: ModelReading): string | null {
  return carrierOf(reading, DISPOSITION_PREDICATE_FLAG);
}

export function submissionsIn(reading: ModelReading): Submission[] {
  const proposes = proposedSitePredicate(reading);
  if (!proposes) return [];

  const names = nameIndex(reading.things);
  const decided = decisions(reading, names);

  // One submission per edge: a submission is only a submission because it proposes a site, so the
  // walk that finds it is also the walk that says which site travels when it is promoted.
  return reading.relationships
    .filter((edge) => edge.PredicateId === proposes)
    .map((edge) => ({
      id: edge.SubjectId,
      name: names.get(edge.SubjectId) ?? edge.SubjectId,
      submissionId: valueOf(reading, edge.SubjectId, 'submissionId'),
      submittedAt: valueOf(reading, edge.SubjectId, 'submittedAt'),
      proposedSiteId: edge.TargetId,
      proposedSiteName: names.get(edge.TargetId),
      disposition: decided.get(edge.SubjectId),
    }));
}

/** What a rejection means in this model: the disposition naming a period after which a submission
 *  goes, whatever it is called. */
export function disposableDisposition(reading: ModelReading): Disposition | null {
  return dispositionsIn(reading).find((one) => one.disposable) ?? null;
}

/** What a promotion means in this model: the disposition naming no period, because a project is the
 *  record of where it came from and is not thrown away. */
export function keptDisposition(reading: ModelReading): Disposition | null {
  return dispositionsIn(reading).find((one) => !one.disposable) ?? null;
}

/** Every predicate this model actually asserts through, so a reviewer saying what travels with a
 *  site chooses from the model's own vocabulary instead of typing names at it. */
export function predicateNamesIn(reading: ModelReading): string[] {
  const names = nameIndex(reading.things);
  const asserted = new Set<string>();
  for (const edge of reading.relationships) {
    const name = names.get(edge.PredicateId);
    if (name) asserted.add(name);
  }
  return [...asserted].sort((left, right) => left.localeCompare(right));
}

/** Oldest first, so a reviewer working down the list works forward in time. A submission whose
 *  arrival was never recorded sorts before every recorded one rather than dropping out. */
export function byArrival(submissions: readonly Submission[]): Submission[] {
  return [...submissions].sort((left, right) => (left.submittedAt ?? '').localeCompare(right.submittedAt ?? ''));
}

/** The Things under the archetype the model marks as holding what a submission can be resolved to.
 *  Found by the mark, never by the archetype's name. */
function dispositionsIn(reading: ModelReading): Disposition[] {
  const archetype = carrierOf(reading, DISPOSITION_ARCHETYPE_FLAG);
  if (!archetype) return [];

  const names = nameIndex(reading.things);
  return reading.relationships
    .filter((edge) => edge.TargetId === archetype && names.get(edge.PredicateId) === IS_PREDICATE_NAME)
    .map((edge) => ({
      id: edge.SubjectId,
      name: names.get(edge.SubjectId) ?? edge.SubjectId,
      disposable: valueOf(reading, edge.SubjectId, COLD_STORAGE_PERIOD_PROPERTY) !== undefined,
    }));
}

/** The one Thing carrying a mark. More than one leaves a reader with two answers and no way to
 *  choose, so it answers with none rather than picking. */
function carrierOf(reading: ModelReading, flag: string): string | null {
  const carrying = Object.keys(reading.properties).filter((id) => flag in reading.properties[id]);
  return carrying.length === 1 ? carrying[0] : null;
}

function decisions(reading: ModelReading, names: Map<string, string>): Map<string, string> {
  const resolvedAs = dispositionPredicate(reading);
  const decided = new Map<string, string>();
  if (!resolvedAs) return decided;

  for (const edge of reading.relationships) {
    if (edge.PredicateId !== resolvedAs || decided.has(edge.SubjectId)) continue;
    const name = names.get(edge.TargetId);
    if (name) decided.set(edge.SubjectId, name);
  }
  return decided;
}

function nameIndex(things: readonly VosThing[]): Map<string, string> {
  return new Map(things.map((thing) => [thing.Id, thing.Name]));
}

/** A property as text, whatever it is written as, because everything here is displayed. */
function valueOf(reading: ModelReading, thingId: string, property: string): string | undefined {
  const held = reading.properties[thingId]?.[property];
  if (held === undefined || held === null) return undefined;
  const value = typeof held === 'object' && 'Value' in held ? held.Value : held;
  if (value === undefined || value === null) return undefined;
  return typeof value === 'string' ? value : String(value);
}
