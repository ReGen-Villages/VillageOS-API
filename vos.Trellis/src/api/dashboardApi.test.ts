import { describe, it, expect, vi, beforeEach } from 'vitest';
import type { VosThing, VosRelationship } from '../types/vos';

vi.mock('./stateApi', () => ({
  stateApi: { getThingsInState: vi.fn() },
}));

import { stateApi } from './stateApi';
import {
  discoverDashboards,
  scopeEntities,
  buildModelIndex,
  thingsOfArchetype,
  resolveBinding,
  type ResolveContext,
} from './dashboardApi';

// ---- a tiny synthetic model: 2 warehouses + a Dashboard config ----
const SPEC = {
  title: 'Ops',
  compare: { label: 'site', archetype: 'Warehouse' },
  sections: [{ widgets: [] }],
};

function model(): { things: VosThing[]; relationships: VosRelationship[] } {
  const t = (Id: string, Name: string, Properties: Record<string, unknown> = {}): VosThing => ({
    Id,
    Name,
    Properties,
  });
  const things: VosThing[] = [
    t('is', 'is'),
    t('arch-dash', 'Dashboard'),
    t('arch-wh', 'Warehouse'),
    t('dash1', 'Operations Dashboard', { spec: JSON.stringify(SPEC) }),
    t('wh1', 'WH-1', { perfect_order_rate: 98.9, cube_utilization: 0.81 }),
    t('wh2', 'WH-2', { perfect_order_rate: 94.1, cube_utilization: 0.93 }),
  ];
  const r = (SubjectId: string, TargetId: string): VosRelationship => ({
    Id: `${SubjectId}-is-${TargetId}`,
    Name: `${SubjectId} is ${TargetId}`,
    SubjectId,
    PredicateId: 'is',
    TargetId,
    Properties: {},
  });
  const relationships: VosRelationship[] = [r('dash1', 'arch-dash'), r('wh1', 'arch-wh'), r('wh2', 'arch-wh')];
  return { things, relationships };
}

function ctxFor(scopeId: string | null): ResolveContext {
  const { things, relationships } = model();
  return { idx: buildModelIndex(things, relationships), scopeId, compareArchetype: 'Warehouse' };
}

describe('discovery', () => {
  it('finds Dashboard config Things and parses their spec', () => {
    const { things, relationships } = model();
    const found = discoverDashboards(things, relationships);
    expect(found).toHaveLength(1);
    expect(found[0].spec.title).toBe('Ops');
    expect(found[0].name).toBe('Operations Dashboard');
  });

  it('lists compare entities from the compare archetype', () => {
    const { things, relationships } = model();
    const idx = buildModelIndex(things, relationships);
    const ents = scopeEntities(discoverDashboards(things, relationships)[0].spec, idx);
    expect(ents.map((e) => e.name)).toEqual(['WH-1', 'WH-2']);
  });

  it('resolves archetype membership via is-edges', () => {
    const { things, relationships } = model();
    const idx = buildModelIndex(things, relationships);
    expect(thingsOfArchetype('Warehouse', idx).map((x) => x.Name).sort()).toEqual(['WH-1', 'WH-2']);
  });
});

describe('resolveBinding', () => {
  beforeEach(() => vi.clearAllMocks());

  it('const', async () => {
    expect(await resolveBinding({ kind: 'const', value: 7 }, ctxFor(null))).toBe(7);
  });

  it('property $scope reads the selected entity', async () => {
    const v = await resolveBinding({ kind: 'property', thing: '$scope', property: 'perfect_order_rate' }, ctxFor('wh1'));
    expect(v).toBeCloseTo(98.9);
  });

  it('property $scope averages across entities when scope is All', async () => {
    const v = await resolveBinding({ kind: 'property', thing: '$scope', property: 'perfect_order_rate' }, ctxFor(null));
    expect(v).toBeCloseTo((98.9 + 94.1) / 2);
  });

  // Regression (Bug #5932): under lazy inheritance the value lives in
  // InheritedProperties, not Properties. Bindings must read effective properties.
  it('property $scope resolves a value inherited from an archetype', async () => {
    const child: VosThing = {
      Id: 'wh3',
      Name: 'WH-3',
      Properties: {},
      InheritedProperties: {
        Warehouse: {
          SourceId: 'arch-wh',
          SourceName: 'Warehouse',
          InheritedAt: '2024-01-01',
          Properties: { perfect_order_rate: 88.5 },
        },
      },
    };
    const ctx: ResolveContext = { idx: buildModelIndex([child], []), scopeId: 'wh3', compareArchetype: 'Warehouse' };
    const v = await resolveBinding({ kind: 'property', thing: '$scope', property: 'perfect_order_rate' }, ctx);
    expect(v).toBeCloseTo(88.5);
  });

  it('aggregate count over an archetype', async () => {
    const v = await resolveBinding({ kind: 'aggregate', archetype: 'Warehouse', op: 'count' }, ctxFor(null));
    expect(v).toBe(2);
  });

  it('aggregate avg over a property', async () => {
    const v = await resolveBinding(
      { kind: 'aggregate', archetype: 'Warehouse', op: 'avg', property: 'cube_utilization' },
      ctxFor(null),
    );
    expect(v).toBeCloseTo((0.81 + 0.93) / 2);
  });

  it('compareEntities emits one row per entity with requested props', async () => {
    const rows = (await resolveBinding(
      { kind: 'compareEntities', properties: ['perfect_order_rate'] },
      ctxFor(null),
    )) as Record<string, unknown>[];
    expect(rows).toHaveLength(2);
    expect(rows[0]).toMatchObject({ name: 'WH-1', perfect_order_rate: 98.9 });
  });

  it('stateCount counts Things returned by the state endpoint', async () => {
    vi.mocked(stateApi.getThingsInState).mockResolvedValue({
      StateName: 'shipped',
      Things: [{ Id: 'a', Name: 'A' }, { Id: 'b', Name: 'B' }, { Id: 'c', Name: 'C' }],
    });
    const v = await resolveBinding({ kind: 'stateCount', state: 'shipped' }, ctxFor(null));
    expect(v).toBe(3);
    expect(stateApi.getThingsInState).toHaveBeenCalledWith('shipped');
  });

  it('stateList enriches state rows with the Thing properties', async () => {
    vi.mocked(stateApi.getThingsInState).mockResolvedValue({
      StateName: 'below_reorder',
      Things: [{ Id: 'wh1', Name: 'WH-1' }],
    });
    const rows = (await resolveBinding(
      { kind: 'stateList', state: 'below_reorder' },
      ctxFor(null),
    )) as Record<string, unknown>[];
    expect(rows[0]).toMatchObject({ id: 'wh1', name: 'WH-1', perfect_order_rate: 98.9 });
  });

  // Feature (#5933): a State can contain Things of several archetypes (Orders
  // and their OrderLines). `archetype` narrows the count/list to one archetype.
  describe('archetype narrowing', () => {
    // Model: 2 Orders + 1 OrderLine, all is-typed; state "open" holds all three.
    function orderCtx(): ResolveContext {
      const t = (Id: string, Name: string): VosThing => ({ Id, Name, Properties: {} });
      const things: VosThing[] = [
        t('is', 'is'), t('arch-order', 'Order'), t('arch-line', 'OrderLine'),
        t('o1', 'O-1'), t('o2', 'O-2'), t('l1', 'L-1'),
      ];
      const rel = (SubjectId: string, TargetId: string): VosRelationship => ({
        Id: `${SubjectId}-is-${TargetId}`, Name: `${SubjectId} is ${TargetId}`,
        SubjectId, PredicateId: 'is', TargetId, Properties: {},
      });
      const relationships = [rel('o1', 'arch-order'), rel('o2', 'arch-order'), rel('l1', 'arch-line')];
      return { idx: buildModelIndex(things, relationships), scopeId: null, compareArchetype: 'Order' };
    }

    beforeEach(() => {
      vi.mocked(stateApi.getThingsInState).mockResolvedValue({
        StateName: 'open',
        Things: [{ Id: 'o1', Name: 'O-1' }, { Id: 'o2', Name: 'O-2' }, { Id: 'l1', Name: 'L-1' }],
      });
    });

    it('stateCount counts only Things of the given archetype', async () => {
      const v = await resolveBinding({ kind: 'stateCount', state: 'open', archetype: 'Order' }, orderCtx());
      expect(v).toBe(2);
    });

    it('stateList returns only Things of the given archetype', async () => {
      const rows = (await resolveBinding(
        { kind: 'stateList', state: 'open', archetype: 'Order' },
        orderCtx(),
      )) as Record<string, unknown>[];
      expect(rows.map((r) => r.id)).toEqual(['o1', 'o2']);
    });

    it('without archetype counts every Thing in the state (backward-compatible)', async () => {
      const v = await resolveBinding({ kind: 'stateCount', state: 'open' }, orderCtx());
      expect(v).toBe(3);
    });
  });
});
