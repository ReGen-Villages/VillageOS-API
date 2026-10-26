/// <reference types="node" />
import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import type { EffectiveProperty, VosRelationship, VosThing } from '../types/vos';
import {
  COLD_STORAGE_PERIOD_PROPERTY,
  DISPOSITION_ARCHETYPE_ROLE,
  DISPOSITION_PREDICATE_ROLE,
  PROPOSED_SITE_ROLE,
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

/** The role Things under the identifiers every model declares them by, and `binds`, which a role reaches
 *  the Thing playing it along. */
const THINGS: VosThing[] = [
  thing('is', 'is'),
  thing('binds', 'binds'),
  thing(PROPOSED_SITE_ROLE, 'proposedSite'),
  thing(DISPOSITION_PREDICATE_ROLE, 'resolvedAs'),
  thing(DISPOSITION_ARCHETYPE_ROLE, 'submissionDisposition'),
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

const BINDINGS: VosRelationship[] = [
  edge('b1', PROPOSED_SITE_ROLE, 'binds', 'puts-forward'),
  edge('b2', DISPOSITION_PREDICATE_ROLE, 'binds', 'decided'),
  edge('b3', DISPOSITION_ARCHETYPE_ROLE, 'binds', 'verdict'),
];

const EDGES: VosRelationship[] = [
  ...BINDINGS,
  edge('e1', 'arrival-1', 'puts-forward', 'meadow'),
  edge('e2', 'arrival-2', 'puts-forward', 'quarry'),
  edge('e3', 'binned', 'is', 'verdict'),
  edge('e4', 'taken-on', 'is', 'verdict'),
  edge('e5', 'meadow', 'covers', 'quarry'),
];

const PROPERTIES: Record<string, Record<string, EffectiveProperty>> = {
  binned: { [COLD_STORAGE_PERIOD_PROPERTY]: held(30) },
  'arrival-1': { submissionId: held('sub-0001'), submittedAt: held('2026-08-20T09:00:00Z') },
  'arrival-2': { submissionId: held('sub-0002'), submittedAt: held('2026-08-21T09:00:00Z') },
};

const without = (binding: string): VosRelationship[] => EDGES.filter((one) => one.Id !== binding);

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

  it('reads a decision off the predicate the model binds, not off the edge it looks like', () => {
    const decided = submissionsIn(
      reading({ relationships: [...EDGES, edge('e6', 'arrival-1', 'decided', 'binned')] }),
    );

    expect(decided.find((one) => one.id === 'arrival-1')?.disposition).toBe('binned');
    expect(decided.find((one) => one.id === 'arrival-2')?.disposition).toBeUndefined();
  });

  it('finds nothing where no predicate is bound to the role of reaching a proposed site', () => {
    const unbound = reading({ relationships: without('b1') });

    expect(proposedSitePredicate(unbound)).toBeNull();
    expect(submissionsIn(unbound)).toEqual([]);
  });

  it('refuses to choose when the role binds two predicates', () => {
    const ambiguous = reading({
      relationships: [...EDGES, edge('b4', PROPOSED_SITE_ROLE, 'binds', 'covers')],
    });

    expect(proposedSitePredicate(ambiguous)).toBeNull();
    expect(submissionsIn(ambiguous)).toEqual([]);
  });

  it('keeps a submission nothing else can be read about', () => {
    const unreadable = submissionsIn(reading({ properties: {} }));

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

  // A template declares its proposed-site predicate by relating the two archetypes, so that relationship is
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

    expect(() => submissionsIn(ambiguous)).toThrow(/Arrival\.submittedAt/);
    expect(() => submissionsIn(ambiguous)).toThrow(/Intake\.submittedAt/);
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

  it('reads only the Things declared under the archetype the role binds', () => {
    const strayEdge = reading({ relationships: [...EDGES, edge('e8', 'meadow', 'covers', 'verdict')] });

    expect(disposableDisposition(strayEdge)?.id).toBe('binned');
    expect(keptDisposition(strayEdge)?.id).toBe('taken-on');
  });

  it('falls back to the identifier of a disposition the model no longer names', () => {
    const unnamed = reading({ things: THINGS.filter((one) => one.Id !== 'taken-on') });

    expect(keptDisposition(unnamed)).toEqual({ id: 'taken-on', name: 'taken-on', disposable: false });
  });

  it('has nothing to offer where no archetype is bound to the role of holding dispositions', () => {
    const unbound = reading({ relationships: without('b3') });

    expect(disposableDisposition(unbound)).toBeNull();
    expect(keptDisposition(unbound)).toBeNull();
  });
});

describe('a role binds one Thing', () => {
  it('finds the archetype whatever its members carry', () => {
    const carried = { ...PROPERTIES, binned: { ...PROPERTIES.binned, colour: held('grey') }, verdict: { colour: held('grey') } };

    expect(disposableDisposition(reading({ properties: carried }))?.id).toBe('binned');
    expect(keptDisposition(reading({ properties: carried }))?.id).toBe('taken-on');
  });

  it('answers with none where the role binds two Things, which is genuinely ambiguous', () => {
    const twoBindings = [...EDGES, edge('b4', DISPOSITION_ARCHETYPE_ROLE, 'binds', 'covers')];

    expect(disposableDisposition(reading({ relationships: twoBindings }))).toBeNull();
  });

  it('answers with none where the model holds no binds predicate to read a binding through', () => {
    const things = THINGS.filter((one) => one.Id !== 'binds');

    expect(disposableDisposition(reading({ things }))).toBeNull();
  });
});

describe('what a reviewer may say travels with a site', () => {
  it('offers every predicate the model asserts through, once each and in order', () => {
    expect(predicateNamesIn(reading())).toEqual(['binds', 'covers', 'is', 'puts-forward']);
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

  for (const identifier of [PROPOSED_SITE_ROLE, DISPOSITION_ARCHETYPE_ROLE, DISPOSITION_PREDICATE_ROLE]) {
    it(`the role ${identifier} is one the submissions commands read`, () => {
      expect(handler).toContain(`"${identifier}"`);
    });
  }

  it(`${COLD_STORAGE_PERIOD_PROPERTY} is the same name the submissions commands read`, () => {
    expect(handler).toContain(`"${COLD_STORAGE_PERIOD_PROPERTY}"`);
  });
});
