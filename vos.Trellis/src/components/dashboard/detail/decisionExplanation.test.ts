import { describe, it, expect } from 'vitest';
import { buildModelIndex } from '../../../api/dashboardApi';
import type { VosRelationship, VosThing } from '../../../types/vos';
import {
  comparisonsOf,
  decisionsOn,
  instantReadKey,
  instantReadsFor,
  type InstantReadings,
} from './decisionExplanation';

function thing(Id: string, Name: string, Properties: Record<string, unknown> = {}, IsArchetype = false): VosThing {
  return { Id, Name, Properties, IsArchetype };
}
function relationship(Id: string, SubjectId: string, PredicateId: string, TargetId: string): VosRelationship {
  return { Id, Name: Id, SubjectId, PredicateId, TargetId, Properties: {} };
}

const INSTANT = '2026-09-22T09:14:03Z';
const EARLIER = '2026-09-21T09:14:03Z';

/** The vocabulary a shipped template declares, under names of this model's own choosing. Built per
 *  call, because a test that takes a mark away would otherwise take it away from every test after it. */
const vocabulary = (): VosThing[] => [
  thing('is', 'is'),
  thing('decisionArchetype', 'Decision', { __IsDecisionArchetype: true }, true),
  thing('considerationArchetype', 'Consideration', { __IsConsiderationArchetype: true }, true),
  thing('constraintArchetype', 'Constraint', { __IsConstraintArchetype: true }, true),
  thing('about', 'about', { __IsDecisionSubjectPredicate: true }),
  thing('chose', 'chose', { __IsChosenPredicate: true }),
  thing('under', 'under', { __IsDecidedUnderPredicate: true }),
  thing('refusal', 'refusal', { __IsRefusalPredicate: true }),
  thing('support', 'support', { __IsSupportPredicate: true }),
  thing('because', 'because', { __IsConsiderationCausePredicate: true }),
  thing('cites', 'cites', { __IsConsiderationEvidencePredicate: true }),
  thing('refused', 'refused', { __IsRefusedCandidatePredicate: true }),
];

/**
 * A sourcing decision about a reservoir: it chose CATCHMENT-7 under a rule, supported by a capacity
 * constraint, and turned CATCHMENT-9 away under the same rule while counting nine more it did not name.
 */
function sourcing(overrides: { constraint?: Record<string, unknown>; cites?: string } = {}) {
  const constraint = overrides.constraint ?? {
    measureSubjectProperty: 'requestedCubicMetres',
    measureCandidateProperty: 'storedCubicMetres',
    limitSubjectProperty: 'capacityCubicMetres',
    unit: 'm3',
  };

  const things: VosThing[] = [
    ...vocabulary(),
    thing('sourcingKind', 'Sourcing'),
    thing('decision', 'sourcing-1', { decidedAt: INSTANT, score: 0.31, candidatesConsidered: 4 }),
    thing('reservoir', 'RESERVOIR-1', { requestedCubicMetres: 999, capacityCubicMetres: 999 }),
    thing('catchment7', 'CATCHMENT-7', { storedCubicMetres: 999 }),
    thing('catchment9', 'CATCHMENT-9', { storedCubicMetres: 999 }),
    thing('ruleA', 'draw from the fullest catchment'),
    thing('capacity', 'capacity', constraint),
    thing('supportOne', 'support-1'),
    thing('refusalOne', 'refusal-1', { candidatesRefused: 10 }),
    thing('pipe3', 'PIPE-3', { storedCubicMetres: 999 }),
  ];

  const relationships: VosRelationship[] = [
    relationship('k1', 'sourcingKind', 'is', 'decisionArchetype'),
    relationship('k2', 'decision', 'is', 'sourcingKind'),
    relationship('k3', 'capacity', 'is', 'constraintArchetype'),
    relationship('k4', 'supportOne', 'is', 'considerationArchetype'),
    relationship('k5', 'refusalOne', 'is', 'considerationArchetype'),
    relationship('e1', 'decision', 'about', 'reservoir'),
    relationship('e2', 'decision', 'chose', 'catchment7'),
    relationship('e3', 'decision', 'under', 'ruleA'),
    relationship('e4', 'decision', 'support', 'supportOne'),
    relationship('e5', 'supportOne', 'because', 'capacity'),
    relationship('e6', 'decision', 'refusal', 'refusalOne'),
    relationship('e7', 'refusalOne', 'because', 'ruleA'),
    relationship('e8', 'refusalOne', 'refused', 'catchment9'),
  ];

  if (overrides.cites) relationships.push(relationship('e9', 'supportOne', 'cites', overrides.cites));

  return buildModelIndex(things, relationships);
}

function readings(entries: [string, VosThing | null][]): InstantReadings {
  return new Map(entries);
}

function readAtTheInstant(thingId: string, properties: Record<string, unknown>): [string, VosThing] {
  return [instantReadKey(thingId, INSTANT), thing(thingId, thingId, properties)];
}

describe('the decisions on a Thing', () => {
  it('finds the decision it is the subject of, with its kind, its choice and the rule it decided under', () => {
    const [decision] = decisionsOn('reservoir', sourcing());

    expect(decision.kindName).toBe('Sourcing');
    expect(decision.decidedAt).toBe(INSTANT);
    expect(decision.subject).toEqual({ id: 'reservoir', name: 'RESERVOIR-1' });
    expect(decision.chose).toEqual({ id: 'catchment7', name: 'CATCHMENT-7' });
    expect(decision.under).toEqual({ id: 'ruleA', name: 'draw from the fullest catchment' });
    expect(decision.score).toBe(0.31);
    expect(decision.candidatesConsidered).toBe(4);
  });

  it('draws both halves from one archetype, told apart by the predicate the decision holds it through', () => {
    const [decision] = decisionsOn('reservoir', sourcing());

    expect(decision.supports.map((consideration) => consideration.cause?.name)).toEqual(['capacity']);
    expect(decision.refusals.map((consideration) => consideration.cause?.name)).toEqual([
      'draw from the fullest catchment',
    ]);
    expect(decision.refusals[0].refusedCandidates).toEqual([{ id: 'catchment9', name: 'CATCHMENT-9' }]);
    expect(decision.refusals[0].candidatesRefused).toBe(10);
  });

  it('opens a decision’s own card on the decision, rather than saying nothing about it', () => {
    expect(decisionsOn('decision', sourcing()).map((decision) => decision.id)).toEqual(['decision']);
  });

  it('says nothing for a Thing no decision is about', () => {
    expect(decisionsOn('ruleA', sourcing())).toEqual([]);
  });

  it('puts the latest first and settles a tie on the instant by the identifier, as the retirement does', () => {
    const index = buildModelIndex(
      [
        ...vocabulary(),
        thing('subject', 'RESERVOIR-1'),
        thing('a', 'a', { decidedAt: INSTANT }),
        thing('b', 'b', { decidedAt: INSTANT }),
        thing('c', 'c', { decidedAt: EARLIER }),
      ],
      [
        relationship('e1', 'a', 'about', 'subject'),
        relationship('e2', 'b', 'about', 'subject'),
        relationship('e3', 'c', 'about', 'subject'),
      ],
    );

    expect(decisionsOn('subject', index).map((decision) => decision.id)).toEqual(['a', 'b', 'c']);
  });
});

describe('what a decision asks to be read at its instant', () => {
  it('names the subject and each candidate once, against the decision’s own instant', () => {
    expect(instantReadsFor(decisionsOn('reservoir', sourcing()))).toEqual([
      { thingId: 'reservoir', instant: INSTANT },
      { thingId: 'catchment7', instant: INSTANT },
    ]);
  });

  it('asks for nothing where no cause is a constraint', () => {
    const index = buildModelIndex(
      [
        ...vocabulary(),
        thing('subject', 'RESERVOIR-1'),
        thing('decision', 'sourcing-1', { decidedAt: INSTANT }),
        thing('consideration', 'support-1'),
        thing('reason', 'the warden asked for it'),
      ],
      [
        relationship('e1', 'decision', 'about', 'subject'),
        relationship('e2', 'decision', 'support', 'consideration'),
        relationship('e3', 'consideration', 'because', 'reason'),
      ],
    );

    expect(instantReadsFor(decisionsOn('subject', index))).toEqual([]);
  });
});

describe('what a consideration compares', () => {
  it('sums the two measure properties against the limit, in the constraint’s unit', () => {
    const [decision] = decisionsOn('reservoir', sourcing());
    const held = readings([
      readAtTheInstant('reservoir', { requestedCubicMetres: 120, capacityCubicMetres: 500 }),
      readAtTheInstant('catchment7', { storedCubicMetres: 306 }),
    ]);

    expect(comparisonsOf(decision, decision.supports[0], held)).toEqual([
      { candidate: { id: 'catchment7', name: 'CATCHMENT-7' }, measure: 426, limit: 500, unit: 'm3' },
    ]);
  });

  it('reads the value the property held then, not the one it holds now', () => {
    const [decision] = decisionsOn('reservoir', sourcing());
    const held = readings([
      readAtTheInstant('reservoir', { requestedCubicMetres: 0, capacityCubicMetres: 500 }),
      readAtTheInstant('catchment7', { storedCubicMetres: 306 }),
    ]);

    expect(comparisonsOf(decision, decision.supports[0], held)[0].measure).toBe(306);
  });

  it('reads a value an instance holds for a name its archetype declares, where the overrides carry it', () => {
    const [decision] = decisionsOn('reservoir', sourcing());
    const held = readings([
      readAtTheInstant('reservoir', { requestedCubicMetres: 0, capacityCubicMetres: 500 }),
      [
        instantReadKey('catchment7', INSTANT),
        {
          Id: 'catchment7',
          Name: 'CATCHMENT-7',
          Properties: {},
          InheritedOverrides: {
            catchmentArchetype: {
              SourceId: 'catchmentArchetype',
              SourceName: 'Catchment',
              InheritedAt: INSTANT,
              Properties: { storedCubicMetres: 212 },
            },
          },
        },
      ],
    ]);

    expect(comparisonsOf(decision, decision.supports[0], held)[0].measure).toBe(212);
  });

  it('reads a property the platform cannot answer for that instant as not recorded, never as today’s', () => {
    const [decision] = decisionsOn('reservoir', sourcing());
    const held = readings([
      readAtTheInstant('reservoir', { capacityCubicMetres: 500 }),
      readAtTheInstant('catchment7', { storedCubicMetres: 306 }),
    ]);

    expect(comparisonsOf(decision, decision.supports[0], held)[0].measure).toBeNull();
  });

  it('reads a Thing the platform answers nothing for as not recorded, rather than dropping the row', () => {
    const [decision] = decisionsOn('reservoir', sourcing());
    const held = readings([
      readAtTheInstant('reservoir', { requestedCubicMetres: 120, capacityCubicMetres: 500 }),
      [instantReadKey('catchment7', INSTANT), null],
    ]);

    expect(comparisonsOf(decision, decision.supports[0], held)).toEqual([
      { candidate: { id: 'catchment7', name: 'CATCHMENT-7' }, measure: null, limit: 500, unit: 'm3' },
    ]);
  });

  it('reads the candidate side from the Thing a consideration cites, which stands in the candidate’s place', () => {
    const index = sourcing({ cites: 'pipe3' });
    const [decision] = decisionsOn('reservoir', index);

    expect(instantReadsFor([decision])).toEqual([
      { thingId: 'reservoir', instant: INSTANT },
      { thingId: 'pipe3', instant: INSTANT },
    ]);

    const held = readings([
      readAtTheInstant('reservoir', { requestedCubicMetres: 0, capacityCubicMetres: 500 }),
      readAtTheInstant('pipe3', { storedCubicMetres: 75 }),
    ]);
    expect(comparisonsOf(decision, decision.supports[0], held)).toEqual([
      { candidate: { id: 'pipe3', name: 'PIPE-3' }, measure: 75, limit: 500, unit: 'm3' },
    ]);
  });

  it('compares once per candidate a refusal named, because each carries its own value', () => {
    const index = sourcing();
    const [decision] = decisionsOn('reservoir', index);
    const refusal = { ...decision.refusals[0], constraint: decision.supports[0].constraint };
    const held = readings([
      readAtTheInstant('reservoir', { requestedCubicMetres: 0, capacityCubicMetres: 500 }),
      readAtTheInstant('catchment9', { storedCubicMetres: 712 }),
    ]);

    expect(comparisonsOf(decision, refusal, held)).toEqual([
      { candidate: { id: 'catchment9', name: 'CATCHMENT-9' }, measure: 712, limit: 500, unit: 'm3' },
    ]);
  });

  it('compares once where the constraint reads nothing off the candidate', () => {
    const index = sourcing({
      constraint: { measureSubjectProperty: 'requestedCubicMetres', limitSubjectProperty: 'capacityCubicMetres', unit: 'm3' },
    });
    const [decision] = decisionsOn('reservoir', index);
    const held = readings([readAtTheInstant('reservoir', { requestedCubicMetres: 120, capacityCubicMetres: 500 })]);

    expect(comparisonsOf(decision, decision.supports[0], held)).toEqual([
      { candidate: undefined, measure: 120, limit: 500, unit: 'm3' },
    ]);
  });

  it('carries a transient measurement the model does not hold, with the constraint’s unit', () => {
    const index = sourcing({ constraint: { unit: 'm3' } });
    const [decision] = decisionsOn('reservoir', index);
    const measured = { ...decision.supports[0], measured: 306, limit: 500 };

    expect(comparisonsOf(decision, measured, new Map())).toEqual([
      { candidate: undefined, measure: 306, limit: 500, unit: 'm3' },
    ]);
  });

  it('reads a measure with no limit as a value rather than as a comparison', () => {
    const index = sourcing({
      constraint: { measureSubjectProperty: 'requestedCubicMetres', unit: 'm3' },
    });
    const [decision] = decisionsOn('reservoir', index);
    const held = readings([readAtTheInstant('reservoir', { requestedCubicMetres: 120 })]);

    expect(comparisonsOf(decision, decision.supports[0], held)).toEqual([
      { candidate: undefined, measure: 120, limit: undefined, unit: 'm3' },
    ]);
  });

  it('has nothing to compare where the cause is not a constraint and no number travels with it', () => {
    const [decision] = decisionsOn('reservoir', sourcing());

    expect(comparisonsOf(decision, decision.refusals[0], new Map())).toEqual([]);
  });
});

describe('what a decision leaves unanswered', () => {
  it('asks for no read and compares nothing where the decision carries no instant', () => {
    const index = sourcing();
    index.byId.get('decision')!.Properties = {};
    const [decision] = decisionsOn('reservoir', index);

    expect(decision.decidedAt).toBeUndefined();
    expect(instantReadsFor([decision])).toEqual([]);
    expect(comparisonsOf(decision, decision.supports[0], new Map())[0].measure).toBeNull();
  });

  it('compares once with no candidate where a refusal naming none reaches a constraint', () => {
    const index = sourcing();
    const [decision] = decisionsOn('reservoir', index);
    const refusal = { ...decision.refusals[0], refusedCandidates: [], constraint: decision.supports[0].constraint };
    const held = readings([readAtTheInstant('reservoir', { requestedCubicMetres: 120, capacityCubicMetres: 500 })]);

    expect(comparisonsOf(decision, refusal, held)).toEqual([
      { candidate: undefined, measure: null, limit: 500, unit: 'm3' },
    ]);
  });

  it('reads a cause of a kind declared below the marked archetype as the constraint it is', () => {
    const index = sourcing();
    index.isParents.set('capacity', ['volumeConstraintKind']);
    index.isParents.set('volumeConstraintKind', ['constraintArchetype']);

    expect(decisionsOn('reservoir', index)[0].supports[0].constraint?.unit).toBe('m3');
  });

  it('answers an `is`-chain that loops back on itself rather than walking it forever', () => {
    const index = sourcing();
    index.isParents.set('ruleA', ['ruleA']);

    expect(decisionsOn('reservoir', index)[0].refusals[0].constraint).toBeUndefined();
  });
});

describe('how the marks are read', () => {
  it('remembers the wiring against the index rather than reading the marks once per card', () => {
    const index = sourcing();
    expect(decisionsOn('reservoir', index)).toHaveLength(1);

    index.byId.get('about')!.Properties = {};

    expect(decisionsOn('reservoir', index)).toHaveLength(1);
  });

  it('passes over a predicate the read did not carry, rather than reading a mark off nothing', () => {
    const index = sourcing();
    index.byId.delete('chose');

    expect(decisionsOn('reservoir', index)[0].chose).toBeUndefined();
  });
});
