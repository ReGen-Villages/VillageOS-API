import { describe, it, expect } from 'vitest';
import { buildModelIndex } from '../../../api/dashboardApi';
import type { VosThing, VosRelationship } from '../../../types/vos';
import { serviceEdgesOn, dispatchesFrom } from './serviceHandling';

function thing(Id: string, Name: string, Properties: Record<string, unknown> = {}, IsArchetype = false): VosThing {
  return { Id, Name, Properties, IsArchetype };
}
function rel(
  Id: string,
  SubjectId: string,
  PredicateId: string,
  TargetId: string,
  Properties: Record<string, unknown> = {},
): VosRelationship {
  return { Id, Name: Id, SubjectId, PredicateId, TargetId, Properties };
}

/**
 * A site whose wiring is named nothing like the platform's own words: the connection archetype
 * is `Wiring`, the service archetype is `Daemon`, and the predicate binding one to the other is
 * `runs`. Only the flags say which is which, which is the whole point of reading them.
 *
 * `measures` is a handled predicate — an edge on it dispatches the metering daemon. `spring when
 * dry` is a state connection, reached by the record edge the platform writes under `handled-by`.
 * `watch` is a vigil naming that same connection through `tells`.
 */
function site() {
  return buildModelIndex(
    [
      thing('is', 'is'),
      thing('runs', 'runs'),
      thing('tells', 'tells', { __IsNotifiedConnectionPredicate: true }),
      thing('wiring', 'Wiring', { __IsConnectionArchetype: true }, true),
      thing('daemon', 'Daemon', { __IsServiceArchetype: true }, true),
      thing('lookout', 'Lookout', { __IsVigilArchetype: true }, true),
      thing('record', 'handled-by', { __IsDispatchRecordPredicate: true }),

      thing('measures', 'measures'),
      thing('meteringService', 'metering service'),
      thing('springWhenDry', 'spring when dry'),
      thing('springService', 'spring service'),
      thing('unbound', 'nothing is bound here'),
      thing('watch', 'watch on the spring'),

      thing('catchment', 'CATCHMENT-1'),
      thing('reservoir', 'RESERVOIR-1'),
      thing('spring', 'SPRING-1'),
      thing('well', 'WELL-1'),
    ],
    [
      rel('w1', 'measures', 'is', 'wiring'),
      rel('w2', 'measures', 'runs', 'meteringService'),
      rel('w3', 'meteringService', 'is', 'daemon'),
      rel('w4', 'springWhenDry', 'is', 'wiring'),
      rel('w5', 'springWhenDry', 'runs', 'springService'),
      rel('w6', 'springService', 'is', 'daemon'),
      rel('w7', 'unbound', 'is', 'wiring'),
      rel('w8', 'watch', 'is', 'lookout'),
      rel('w9', 'watch', 'tells', 'springWhenDry'),

      rel('e1', 'catchment', 'measures', 'reservoir', {
        __DispatchState: 'Done',
        __DispatchLastAttemptAt: '2026-08-27T08:09:32Z',
      }),
      rel('e2', 'spring', 'record', 'springWhenDry', {
        __DispatchState: 'Failed',
        __DispatchLastAttemptAt: '2026-08-27T08:10:00Z',
        __DispatchLastError: 'no reading came back',
      }),
      rel('e3', 'spring', 'record', 'unbound'),
      rel('e4', 'well', 'record', 'watch', {
        __DispatchState: 'Done',
        __DispatchLastAttemptAt: '2026-08-27T08:11:00Z',
      }),
    ],
  );
}

/** The stamps as the platform hands them back, keyed the way the hook keys its reads. */
function stampsFrom(idx: ReturnType<typeof site>): Map<string, VosRelationship> {
  return new Map(idx.relationships.map((edge) => [edge.Id, edge]));
}

describe('serviceEdgesOn', () => {
  it('names the service a handled edge reaches, from the subject the platform dispatched on', () => {
    const [edge] = serviceEdgesOn('catchment', site());

    expect(edge.serviceName).toBe('metering service');
    expect(edge.serviceId).toBe('meteringService');
    expect(edge.connectionName).toBe('measures');
    expect(edge.relationshipId).toBe('e1');
  });

  it('leaves the other end of a handled edge out: the work was done on the subject alone', () => {
    expect(serviceEdgesOn('reservoir', site())).toEqual([]);
  });

  it('reads a state dispatch through the connection the record edge points at', () => {
    const found = serviceEdgesOn('spring', site());

    expect(found.map((edge) => edge.serviceName)).toEqual(['spring service', 'nothing is bound here']);
    expect(found[0].connectionName).toBe('spring when dry');
  });

  it('follows a record edge targeting a vigil to the connection the vigil names', () => {
    const [edge] = serviceEdgesOn('well', site());

    expect(edge.serviceName).toBe('spring service');
    expect(edge.connectionName).toBe('spring when dry');
    expect(edge.relationshipId).toBe('e4');
  });

  it('calls a connection binding no service by its own name, rather than leaving the row nameless', () => {
    const unbound = serviceEdgesOn('spring', site()).find((edge) => edge.relationshipId === 'e3');

    expect(unbound?.serviceName).toBe('nothing is bound here');
    expect(unbound?.serviceId).toBeUndefined();
  });

  it('leaves the connection itself out: its target is the wiring, not something worked on', () => {
    expect(serviceEdgesOn('springWhenDry', site())).toEqual([]);
  });

  it('finds nothing where the model marks no connection archetype, rather than guessing at one', () => {
    const idx = buildModelIndex(
      [thing('is', 'is'), thing('measures', 'measures'), thing('catchment', 'CATCHMENT-1'), thing('reservoir', 'RESERVOIR-1')],
      [rel('e1', 'catchment', 'measures', 'reservoir', { __DispatchState: 'Done' })],
    );
    expect(serviceEdgesOn('catchment', idx)).toEqual([]);
  });

  it('finds nothing where two archetypes both claim the connection flag, as neither can be the one', () => {
    const idx = buildModelIndex(
      [
        thing('is', 'is'),
        thing('wiring', 'Wiring', { __IsConnectionArchetype: true }, true),
        thing('rival', 'Rival', { __IsConnectionArchetype: true }, true),
        thing('measures', 'measures'),
        thing('catchment', 'CATCHMENT-1'),
        thing('reservoir', 'RESERVOIR-1'),
      ],
      [rel('w1', 'measures', 'is', 'wiring'), rel('e1', 'catchment', 'measures', 'reservoir', { __DispatchState: 'Done' })],
    );
    expect(serviceEdgesOn('catchment', idx)).toEqual([]);
  });

  it('reads the flag off the archetype that owns it, not off a member that inherited it', () => {
    const idx = site();
    // `measures` is a member of Wiring; were the flag read as inherited, `measures` would contest
    // the archetype's claim and the whole model would answer nothing.
    expect(serviceEdgesOn('catchment', idx)).toHaveLength(1);
  });
});

describe('dispatchesFrom', () => {
  it('carries the instant, the state and the words a service refused with', () => {
    const idx = site();
    const [dispatch] = dispatchesFrom(serviceEdgesOn('spring', idx).slice(0, 1), stampsFrom(idx));

    expect(dispatch.at).toBe('2026-08-27T08:10:00Z');
    expect(dispatch.state).toBe('Failed');
    expect(dispatch.error).toBe('no reading came back');
  });

  it('orders by when the platform last ran each, oldest first', () => {
    const idx = site();
    const edges = [...serviceEdgesOn('spring', idx), ...serviceEdgesOn('catchment', idx)];

    expect(dispatchesFrom(edges, stampsFrom(idx)).map((d) => d.at)).toEqual([
      '2026-08-27T08:09:32Z',
      '2026-08-27T08:10:00Z',
      undefined,
    ]);
  });

  it('leaves a dispatch the platform holds no stamp for undated rather than inventing a time', () => {
    const [dispatch] = dispatchesFrom(serviceEdgesOn('catchment', site()), new Map());

    expect(dispatch.at).toBeUndefined();
    expect(dispatch.state).toBeUndefined();
    expect(dispatch.serviceName).toBe('metering service');
  });
});
