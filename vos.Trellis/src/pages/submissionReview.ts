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

import type { VosThing } from '../types/vos';
import { IS_PREDICATE_NAME, ownCarrierOf, type ModelReading } from './modelVocabulary';

export const PROPOSED_SITE_PREDICATE_FLAG = '__IsProposedSitePredicate';
export const DISPOSITION_ARCHETYPE_FLAG = '__IsSubmissionDispositionArchetype';
export const DISPOSITION_PREDICATE_FLAG = '__IsSubmissionDispositionPredicate';

/** A disposition says what follows from the decision, and the period after which a submission goes
 *  is what makes one disposable. Rejecting relates a submission to whichever names a period, so a
 *  model may call that disposition anything. */
export const COLD_STORAGE_PERIOD_PROPERTY = 'daysBeforeColdStorage';

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
  return ownCarrierOf(reading, PROPOSED_SITE_PREDICATE_FLAG);
}

/** The predicate a decision is written through. */
export function dispositionPredicate(reading: ModelReading): string | null {
  return ownCarrierOf(reading, DISPOSITION_PREDICATE_FLAG);
}

export function submissionsIn(reading: ModelReading): Submission[] {
  const proposes = proposedSitePredicate(reading);
  if (!proposes) return [];

  const names = nameIndex(reading.things);
  const decided = decisions(reading, names);
  const declarations = new Set(
    reading.things.filter((thing) => thing.IsArchetype).map((thing) => thing.Id),
  );

  // One submission per edge: a submission is only a submission because it proposes a site, so the
  // walk that finds it is also the walk that says which site travels when it is promoted. A model
  // declares that predicate by relating its own archetypes, and that edge is asserted through the
  // same predicate — listed, it offers a reviewer a decision over the declaration itself.
  return reading.relationships
    .filter((edge) => edge.PredicateId === proposes && !declarations.has(edge.SubjectId))
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
  const archetype = ownCarrierOf(reading, DISPOSITION_ARCHETYPE_FLAG);
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

function nameIndex(things: readonly VosThing[]): Map<string, string> {
  return new Map(things.map((thing) => [thing.Id, thing.Name]));
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

/** A property as text, whatever it is written as, because everything here is displayed. A property
 *  a model declares but never gives a value to reads as absent, which is what it is.
 *
 *  A Thing's own value is keyed by the bare name, but a value it holds for a name its archetype
 *  declares comes back keyed by that archetype — `Submission.submittedAt` rather than `submittedAt`.
 *  Both are the same property to a reader, so the name is matched after its declaring prefix.
 *
 *  A Thing cannot own a name and inherit the same one, so at most one key matches — except where the
 *  name is inherited from more than one archetype. The model answers a bare read of that with an
 *  ambiguity and asks for the full path; this page has no path to give, so it says which paths it
 *  found rather than showing a reviewer a value the model itself declines to choose. */
function valueOf(reading: ModelReading, thingId: string, property: string): string | undefined {
  const held = reading.properties[thingId] ?? {};
  const keys = Object.keys(held).filter((name) => name.split('.').pop() === property);
  if (keys.length > 1) {
    throw new Error(
      `'${property}' is inherited from more than one archetype on ${thingId}, so reading it by that ` +
        `name alone says nothing: ${keys.join(', ')}. Read it by its full path.`,
    );
  }

  const value = keys.length === 0 ? undefined : held[keys[0]]?.Value;
  if (value === undefined || value === null) return undefined;
  return typeof value === 'string' ? value : String(value);
}
