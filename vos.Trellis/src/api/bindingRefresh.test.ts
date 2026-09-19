import { describe, it, expect } from 'vitest';
import { askedOfTheBroker, statesRead } from './bindingRefresh';
import { BINDING_FIELDS } from './bindingVocabulary';
import type { Binding } from '../types/dashboard';

/** One binding of every kind the vocabulary declares, each reading a state where its kind can. */
const ONE_OF_EACH: Record<Binding['kind'], Binding> = {
  const: { kind: 'const', value: 1 },
  property: { kind: 'property', thing: '$scope', property: 'area' },
  aggregate: { kind: 'aggregate', archetype: 'Building', op: 'count' },
  compareEntities: { kind: 'compareEntities', properties: ['area'] },
  thingList: { kind: 'thingList', archetype: 'Building' },
  related: { kind: 'related', via: [{ predicate: 'contains', inState: 'flagged' }] },
  origin: { kind: 'origin', property: 'area', reads: {}, source: { via: [{ predicate: 'feeds', notInState: 'dry' }] } },
  working: { kind: 'working', property: 'area', via: [{ predicate: 'contains', inState: 'metered' }] },
  stateCount: { kind: 'stateCount', state: 'flagged' },
  stateList: { kind: 'stateList', state: 'flagged', excludeState: 'cleared' },
  stateOf: { kind: 'stateOf', states: ['flagged', 'cleared'] },
  verdict: { kind: 'verdict', states: [{ state: 'flagged', reads: 'flagged' }], via: [{ predicate: 'feeds', inState: 'wet' }] },
  ratio: { kind: 'ratio', numerator: { kind: 'stateCount', state: 'flagged' }, denominator: { kind: 'aggregate', archetype: 'Building', op: 'count' } },
  timeseries: { kind: 'timeseries', archetype: 'Reading', happenedAt: 'at', property: 'volume', op: 'avg', bucketSeconds: 60, buckets: 10 },
  latest: { kind: 'latest', series: { kind: 'timeseries', archetype: 'Reading', happenedAt: 'at', property: 'volume', op: 'avg', bucketSeconds: 60, buckets: 10 } },
  service: { kind: 'service', endpoint: '/api/endpoints/reservoirs' },
  history: { kind: 'history', property: 'temperatureCelsius', windowSeconds: 31536000, steps: [{ fold: 'monthOfYear', function: 'Max' }] },
};

const READ_FROM_THE_MODEL: Binding['kind'][] = ['const', 'property', 'aggregate', 'compareEntities', 'thingList'];

describe('which bindings the broker answers', () => {
  it('classifies every kind the vocabulary declares, so a new kind cannot go unjudged', () => {
    expect(Object.keys(ONE_OF_EACH).sort()).toEqual(Object.keys(BINDING_FIELDS).sort());
  });

  it.each(Object.keys(ONE_OF_EACH) as Binding['kind'][])('%s', (kind) => {
    expect(askedOfTheBroker(ONE_OF_EACH[kind])).toBe(!READ_FROM_THE_MODEL.includes(kind));
  });

  it('reads a walk step that names a state through the broker, whatever the kind', () => {
    expect(askedOfTheBroker({ kind: 'related', via: [{ predicate: 'contains' }] })).toBe(false);
    expect(askedOfTheBroker({ kind: 'related', via: [{ predicate: 'contains', notInState: 'dry' }] })).toBe(true);
  });

  it('reads a computed column through the broker when the column does', () => {
    expect(askedOfTheBroker({
      kind: 'thingList', archetype: 'Building',
      computed: [{ key: 'status', value: { kind: 'stateOf', states: ['flagged'] } }],
    })).toBe(true);
  });
});

describe('the states a binding\'s answer is made of', () => {
  it.each([
    ['stateCount', ['flagged']],
    ['stateList', ['flagged', 'cleared']],
    ['stateOf', ['flagged', 'cleared']],
    ['verdict', ['flagged', 'wet']],
    ['related', ['flagged']],
    ['origin', ['dry']],
    ['working', ['metered']],
    ['ratio', ['flagged']],
    ['timeseries', []],
    ['latest', []],
    ['service', []],
    ['history', []],
    ['const', []],
  ] as [Binding['kind'], string[]][])('%s', (kind, states) => {
    expect(statesRead(ONE_OF_EACH[kind])).toEqual(states);
  });

  it('names the states a computed column reads', () => {
    expect(statesRead({
      kind: 'compareEntities', properties: [],
      computed: [{ key: 'status', value: { kind: 'stateOf', states: ['flagged'] } }],
    })).toEqual(['flagged']);
  });
});
