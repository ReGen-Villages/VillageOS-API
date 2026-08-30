/// <reference types="node" />
import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import type { EffectiveProperty, VosRelationship, VosThing } from '../types/vos';
import {
  COLD_STORAGE_PERIOD_PROPERTY,
  DISPOSITION_ARCHETYPE_FLAG,
  DISPOSITION_PREDICATE_FLAG,
  PROPOSED_SITE_PREDICATE_FLAG,
  byArrival,
  disposableDisposition,
  dispositionPredicate,
  keptDisposition,
  predicateNamesIn,
  proposedSitePredicate,
  submissionsIn,
} from './submissionReview';
import type { ModelReading } from './modelVocabulary';

// Every name below is spelled differently from the shipped land-intake template, so a reader
// answering only to that spelling fails here rather than passing.
const thing = (Id: string, Name: string): VosThing => ({ Id, Name, Properties: {} });

/** A model's own declaration rather than anything submitted into it. */
const archetype = (Id: string, Name: string): VosThing => ({ Id, Name, Properties: {}, IsArchetype: true });

const edge = (Id: string, SubjectId: string, PredicateId: string, TargetId: string): VosRelationship => ({
  Id,
  SubjectId,
  PredicateId,
  TargetId,
  Properties: {},
});

const held = (Value: unknown): EffectiveProperty => ({ Value, Type: 'vos.String', IsInherited: false });

/** What a Thing gets from the archetype it `is`. A mark is an ordinary property on the archetype, so
 *  every term under it reads the mark too — which is what a real seeded model hands back. */
const inherited = (Value: unknown): EffectiveProperty => ({ Value, Type: 'vos.String', IsInherited: true });

const THINGS: VosThing[] = [
  thing('is', 'is'),
  thing('puts-forward', 'puts-forward'),
  thing('decided', 'decided'),
  thing('covers', 'covers'),
  thing('verdict', 'Verdict'),
  thing('binned', 'binned'),
  thing('taken-on', 'rejected'),
  thing('arrival-1', 'Meadow Lane arrival'),
  thing('arrival-2', 'Old Quarry arrival'),
  thing('meadow', 'Meadow Lane'),
  thing('quarry', 'Old Quarry'),
];

const EDGES: VosRelationship[] = [
  edge('e1', 'arrival-1', 'puts-forward', 'meadow'),
  edge('e2', 'arrival-2', 'puts-forward', 'quarry'),
  edge('e3', 'binned', 'is', 'verdict'),
  edge('e4', 'taken-on', 'is', 'verdict'),
  edge('e5', 'meadow', 'covers', 'quarry'),
];

const PROPERTIES: Record<string, Record<string, EffectiveProperty>> = {
  'puts-forward': { [PROPOSED_SITE_PREDICATE_FLAG]: held(true) },
  decided: { [DISPOSITION_PREDICATE_FLAG]: held(true) },
  verdict: { [DISPOSITION_ARCHETYPE_FLAG]: held(true) },
  binned: { [DISPOSITION_ARCHETYPE_FLAG]: inherited(true), [COLD_STORAGE_PERIOD_PROPERTY]: held(30) },
  'taken-on': { [DISPOSITION_ARCHETYPE_FLAG]: inherited(true) },
  'arrival-1': { submissionId: held('sub-0001'), submittedAt: held('2026-08-20T09:00:00Z') },
  'arrival-2': { submissionId: held('sub-0002'), submittedAt: held('2026-08-21T09:00:00Z') },
};

function reading(patch: Partial<ModelReading> = {}): ModelReading {
  return { things: THINGS, relationships: EDGES, properties: PROPERTIES, ...patch };
}

describe('what has arrived', () => {
  it('lists a submission with its arrival, what it proposes, and the identifier it came under', () => {
    expect(submissionsIn(reading())).toContainEqual({
      id: 'arrival-1',
      name: 'Meadow Lane arrival',
      submissionId: 'sub-0001',
      submittedAt: '2026-08-20T09:00:00Z',
      proposedSiteId: 'meadow',
      proposedSiteName: 'Meadow Lane',
      disposition: undefined,
    });
  });

  it('reads a decision off the predicate the model marks, not off the edge it looks like', () => {
    const decided = submissionsIn(
      reading({ relationships: [...EDGES, edge('e6', 'arrival-1', 'decided', 'binned')] }),
    );

    expect(decided.find((one) => one.id === 'arrival-1')?.disposition).toBe('binned');
    expect(decided.find((one) => one.id === 'arrival-2')?.disposition).toBeUndefined();
  });

  it('finds nothing where no predicate is marked as reaching a proposed site', () => {
    const unmarked = reading({ properties: { ...PROPERTIES, 'puts-forward': {} } });

    expect(proposedSitePredicate(unmarked)).toBeNull();
    expect(submissionsIn(unmarked)).toEqual([]);
  });

  it('refuses to choose when two predicates carry the same mark', () => {
    const ambiguous = reading({
      properties: { ...PROPERTIES, covers: { [PROPOSED_SITE_PREDICATE_FLAG]: held(true) } },
    });

    expect(proposedSitePredicate(ambiguous)).toBeNull();
    expect(submissionsIn(ambiguous)).toEqual([]);
  });

  it('keeps a submission nothing else can be read about', () => {
    const unreadable = submissionsIn(reading({ properties: { 'puts-forward': PROPERTIES['puts-forward'] } }));

    expect(unreadable).toHaveLength(2);
    expect(unreadable[0]).toMatchObject({ name: 'Meadow Lane arrival', submittedAt: undefined });
  });

  it('falls back to the identifier of a submission the model no longer names', () => {
    const unnamed = submissionsIn(reading({ things: THINGS.filter((one) => one.Id !== 'arrival-1') }));

    expect(unnamed.find((one) => one.id === 'arrival-1')?.name).toBe('arrival-1');
  });

  it('keeps a submission whose proposed site is not in the model', () => {
    const dangling = submissionsIn(
      reading({ relationships: [...EDGES, edge('e7', 'arrival-1', 'puts-forward', 'gone')] }),
    );

    expect(dangling.find((one) => one.proposedSiteId === 'gone')).toMatchObject({
      name: 'Meadow Lane arrival',
      proposedSiteName: undefined,
    });
  });

  it('reads a property a model declares but never gives a value to as absent', () => {
    const declared = reading({
      properties: { ...PROPERTIES, 'arrival-1': { submittedAt: { ...held(null) } } },
    });

    expect(submissionsIn(declared).find((one) => one.id === 'arrival-1')?.submittedAt).toBeUndefined();
  });

  // A template declares its proposed-site predicate by relating the two archetypes, so that edge is
  // asserted through the same predicate every real submission is. Listed, it offers a reviewer a
  // Reject and a Promote over the model's own declaration.
  it('leaves out the declaration the model makes of what a submission is', () => {
    const declaring = reading({
      things: [...THINGS, archetype('arrival', 'Arrival'), archetype('place', 'Place')],
      relationships: [...EDGES, edge('e8', 'arrival', 'puts-forward', 'place')],
    });

    expect(submissionsIn(declaring).map((one) => one.id)).toEqual(['arrival-1', 'arrival-2']);
  });

  // Two archetypes declaring one name is the case the model itself refuses to answer, so the page has
  // to refuse too rather than show whichever the broker serialised first.
  it('refuses a name inherited from two archetypes rather than choosing between them', () => {
    const ambiguous = reading({
      properties: {
        ...PROPERTIES,
        'arrival-1': {
          'Arrival.submittedAt': held('2026-08-20T09:00:00Z'),
          'Intake.submittedAt': held('2026-08-21T11:00:00Z'),
        },
      },
    });

    expect(() => submissionsIn(ambiguous)).toThrow(
      /Arrival\.submittedAt[\s\S]*Intake\.submittedAt/,
    );
  });

  // What a Thing holds for a name its archetype declares comes back keyed by that archetype, so a
  // reader looking up the bare name finds nothing and every arrival reads as never recorded.
  it('reads a value held under the name the archetype declares it by', () => {
    const qualified = reading({
      properties: {
        ...PROPERTIES,
        'arrival-1': {
          'Arrival.submissionId': held('sub-0001'),
          'Arrival.submittedAt': held('2026-08-20T09:00:00Z'),
        },
      },
    });

    expect(submissionsIn(qualified).find((one) => one.id === 'arrival-1')).toMatchObject({
      submissionId: 'sub-0001',
      submittedAt: '2026-08-20T09:00:00Z',
    });
  });

  it('shows a property written as a number, because everything here is displayed', () => {
    const numbered = reading({
      properties: { ...PROPERTIES, 'arrival-1': { submissionId: held(17) } },
    });

    expect(numbered.properties['arrival-1'].submissionId.Value).toBe(17);
    expect(submissionsIn(numbered).find((one) => one.id === 'arrival-1')?.submissionId).toBe('17');
  });

  it('reads a decision pointing at a Thing the model no longer holds as no decision at all', () => {
    const dangling = submissionsIn(
      reading({ relationships: [...EDGES, edge('e10', 'arrival-1', 'decided', 'gone')] }),
    );

    expect(dangling.find((one) => one.id === 'arrival-1')?.disposition).toBeUndefined();
  });

  it('names the predicate a decision is written through', () => {
    expect(dispositionPredicate(reading())).toBe('decided');
  });
});

describe('what a decision means here', () => {
  it('takes the disposable one from the period it names, not from what it is called', () => {
    expect(disposableDisposition(reading())).toEqual({ id: 'binned', name: 'binned', disposable: true });
  });

  it('takes the kept one from naming no period', () => {
    expect(keptDisposition(reading())).toEqual({ id: 'taken-on', name: 'rejected', disposable: false });
  });

  it('reads only the Things declared under the marked archetype', () => {
    const strayEdge = reading({ relationships: [...EDGES, edge('e8', 'meadow', 'covers', 'verdict')] });

    expect(disposableDisposition(strayEdge)?.id).toBe('binned');
    expect(keptDisposition(strayEdge)?.id).toBe('taken-on');
  });

  it('falls back to the identifier of a disposition the model no longer names', () => {
    const unnamed = reading({ things: THINGS.filter((one) => one.Id !== 'taken-on') });

    expect(keptDisposition(unnamed)).toEqual({ id: 'taken-on', name: 'taken-on', disposable: false });
  });

  it('has nothing to offer where no archetype is marked as holding dispositions', () => {
    const unmarked = reading({ properties: { ...PROPERTIES, verdict: {} } });

    expect(disposableDisposition(unmarked)).toBeNull();
    expect(keptDisposition(unmarked)).toBeNull();
  });
});

describe('a mark is owned, never inherited', () => {
  it('finds the archetype even though every term under it reads the mark too', () => {
    expect(disposableDisposition(reading())?.id).toBe('binned');
    expect(keptDisposition(reading())?.id).toBe('taken-on');
  });

  it('answers with none where two Things own the mark, which is genuinely ambiguous', () => {
    const twoOwners = {
      ...PROPERTIES,
      covers: { [DISPOSITION_ARCHETYPE_FLAG]: held(true) },
    };

    expect(disposableDisposition(reading({ properties: twoOwners }))).toBeNull();
  });

  it('answers with none where the only carrier inherited the mark, so nothing declares it', () => {
    const noOwner = {
      ...PROPERTIES,
      verdict: { [DISPOSITION_ARCHETYPE_FLAG]: inherited(true) },
    };

    expect(disposableDisposition(reading({ properties: noOwner }))).toBeNull();
  });
});

describe('what a reviewer may say travels with a site', () => {
  it('offers every predicate the model asserts through, once each and in order', () => {
    expect(predicateNamesIn(reading())).toEqual(['covers', 'is', 'puts-forward']);
  });

  it('offers nothing for an edge whose predicate the model no longer holds', () => {
    const orphaned = reading({ relationships: [edge('e9', 'arrival-1', 'vanished', 'meadow')] });

    expect(predicateNamesIn(orphaned)).toEqual([]);
  });
});

describe('the order a reviewer works in', () => {
  it('puts the oldest arrival first and one never recorded ahead of both', () => {
    const ordered = byArrival([
      { id: 'b', name: 'b', proposedSiteId: 'x', submittedAt: '2026-08-21T09:00:00Z' },
      { id: 'a', name: 'a', proposedSiteId: 'x', submittedAt: '2026-08-20T09:00:00Z' },
      { id: 'c', name: 'c', proposedSiteId: 'x' },
    ]);

    expect(ordered.map((one) => one.id)).toEqual(['c', 'a', 'b']);
  });

  it('puts an unrecorded arrival first whichever side of a recorded one it starts on', () => {
    const ordered = byArrival([
      { id: 'a', name: 'a', proposedSiteId: 'x', submittedAt: '2026-08-20T09:00:00Z' },
      { id: 'c', name: 'c', proposedSiteId: 'x' },
    ]);

    expect(ordered.map((one) => one.id)).toEqual(['c', 'a']);
  });
});

describe('the vocabulary the command line reads', () => {
  const handler = readFileSync(
    join(dirname(fileURLToPath(import.meta.url)), '..', '..', '..', 'vos.Taproot', 'SubmissionsCommandHandler.cs'),
    'utf-8',
  );

  for (const name of [
    PROPOSED_SITE_PREDICATE_FLAG,
    DISPOSITION_ARCHETYPE_FLAG,
    DISPOSITION_PREDICATE_FLAG,
    COLD_STORAGE_PERIOD_PROPERTY,
  ]) {
    it(`${name} is the same name the submissions commands read`, () => {
      expect(handler).toContain(`"${name}"`);
    });
  }
});
