import { describe, it, expect } from 'vitest';
import { resolveBinding } from '../api/dashboardApi';
import type { ResolveContext } from '../api/dashboardApi';
import { findingsFrom, type FindingsAnswer } from './answeredFindings';
import type { VosRelationship, VosThing } from '../types/vos';

/**
 * What the intake service answers, read as the resolver reads it. The four questions a loaded model
 * cannot answer are the whole reason this file exists, so each is asked here the way a widget asks it.
 */
const SITE = 'aaaaaaaa-0000-0000-0000-000000000001';
const STUDY = 'aaaaaaaa-0000-0000-0000-000000000002';
const STUDY_ARCHETYPE = 'aaaaaaaa-0000-0000-0000-000000000003';
const IS = 'aaaaaaaa-0000-0000-0000-000000000004';
const STUDIES = 'aaaaaaaa-0000-0000-0000-000000000005';

/** A Thing in the envelope the broker writes and the service passes on: a value wrapped with its type,
 *  and the write kind that says how it came to be one. */
function thing(
  Id: string,
  Name: string,
  Properties: Record<string, unknown> = {},
  States: string[] = [],
): VosThing & { States: string[] } {
  return { Id, Name, Properties, States };
}

function edge(SubjectId: string, PredicateId: string, TargetId: string): VosRelationship {
  return { Id: `${SubjectId}-${TargetId}`, Name: '', SubjectId, PredicateId, TargetId, Properties: {} };
}

function answered(over: Partial<FindingsAnswer> = {}): FindingsAnswer {
  return {
    spec: '{}',
    scopeId: SITE,
    things: [
      thing(IS, 'is'),
      thing(STUDIES, 'studies'),
      thing(STUDY_ARCHETYPE, 'SiteStudy'),
      thing(SITE, 'Willow Bend', {
        statedAreaHectares: { typeInfo: 'vos.Double', value: 24, writeKind: 'FactOnly' },
      }),
      thing(STUDY, 'Willow Bend Site Study', { pctOfConsumption: { typeInfo: 'vos.Double', value: 118 } }, [
        'EnergyNetPositive',
      ]),
    ],
    relationships: [edge(STUDY, IS, STUDY_ARCHETYPE), edge(STUDY, STUDIES, SITE)],
    ranges: {
      [STUDY]: {
        ThingId: STUDY,
        ThingName: 'Willow Bend Site Study',
        OwnRanges: [
          {
            Name: 'EnergyNetPositive',
            Criteria: 'pctOfConsumption >= 100',
            Comparisons: [{ PropertyName: 'pctOfConsumption', Operator: '>=', Value: 100 }],
          },
        ],
        InheritedRanges: [],
      } as unknown as FindingsAnswer['ranges'][string],
    },
    ...over,
  };
}

function contextFor(answer: FindingsAnswer): ResolveContext {
  const findings = findingsFrom(answer);
  return { idx: findings.index, scopeId: findings.scopeId, compareArchetype: 'Site', reads: findings.reads };
}

describe('the findings a submitter is answered with', () => {
  it('resolves a figure the site states, unwrapped from the envelope it travelled in', async () => {
    const value = await resolveBinding(
      { kind: 'property', thing: '$scope', property: 'statedAreaHectares' },
      contextFor(answered()),
    );

    expect(value).toBe(24);
  });

  // A verdict is the one binding on this page that asks both questions the model cannot answer for
  // itself: which Things hold the state, and what the range judging it compared.
  it('resolves a verdict from the states each Thing arrived carrying and the ranges beside them', async () => {
    const rows = (await resolveBinding(
      {
        kind: 'verdict',
        via: [{ predicate: 'studies', direction: 'in' }],
        states: [{ state: 'EnergyNetPositive', reads: 'the site makes what it uses' }],
      },
      contextFor(answered()),
    )) as Record<string, unknown>[];

    expect(rows).toHaveLength(1);
    expect(rows[0]).toMatchObject({
      state: 'EnergyNetPositive',
      reads: 'the site makes what it uses',
      property: 'pctOfConsumption',
      target: 100,
      value: 118,
    });
  });

  it('draws no verdict row for a state nothing in the answer holds', async () => {
    const rows = (await resolveBinding(
      {
        kind: 'verdict',
        via: [{ predicate: 'studies', direction: 'in' }],
        states: [{ state: 'EnergyShortOfTarget', reads: 'the site falls short' }],
      },
      contextFor(answered()),
    )) as Record<string, unknown>[];

    expect(rows).toEqual([]);
  });

  // Nothing is how a figure the analysis has not computed reads, and a question this page cannot ask is
  // not that — so the two it never asks refuse rather than resolving to nothing.
  it('refuses the two questions a submitter\'s page cannot ask', async () => {
    const { reads } = findingsFrom(answered());

    await expect(
      reads.aggregate({
        function: 'Sum',
        memberType: 'Site',
        timestampProperty: 'observedAt',
        windowSeconds: 60,
        bucketSeconds: 60,
      }),
    ).rejects.toThrow();
    await expect(reads.fromService('/api/endpoints/metrics', {})).rejects.toThrow();
  });

  it('answers with nothing for a Thing whose ranges the service could not read', async () => {
    const { reads } = findingsFrom(answered({ ranges: {} }));

    await expect(reads.thingRanges(STUDY)).resolves.toBeNull();
  });
});
