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
  thingIdsOfArchetype,
  resolveBinding,
  filterRows,
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

// Bug #5942: archetypes are subtyped (Customer is Party, PickLocation is Location),
// so membership must be transitive over the is-chain and count instances only.
describe('thingIdsOfArchetype (transitive, instances-only)', () => {
  const t = (Id: string, Name: string): VosThing => ({ Id, Name, Properties: {} });
  const rel = (SubjectId: string, TargetId: string): VosRelationship => ({
    Id: `${SubjectId}-is-${TargetId}`, Name: `${SubjectId} is ${TargetId}`,
    SubjectId, PredicateId: 'is', TargetId, Properties: {},
  });
  // Party <- Customer(sub-archetype) <- ACME(instance); Warehouse(leaf) <- WH-1;
  // EquipmentClass <- EQC-REACH <- FORK-1, and FORK-1 also is-a Equipment (multi-parent).
  const idx = buildModelIndex(
    [t('is', 'is'), t('Party', 'Party'), t('Customer', 'Customer'), t('c1', 'ACME'),
     t('Warehouse', 'Warehouse'), t('wh1', 'WH-1'),
     t('EquipmentClass', 'EquipmentClass'), t('EQC', 'EQC-REACH'), t('Equipment', 'Equipment'), t('fork', 'FORK-1')],
    [rel('Customer', 'Party'), rel('c1', 'Customer'), rel('wh1', 'Warehouse'),
     rel('EQC', 'EquipmentClass'), rel('fork', 'EQC'), rel('fork', 'Equipment')],
  );

  it('includes instances under a sub-archetype and excludes the sub-archetype node', () => {
    expect(thingIdsOfArchetype('Party', idx)).toEqual(new Set(['c1'])); // ACME, not the Customer type node
  });

  it('resolves a directly-typed instance', () => {
    expect(thingIdsOfArchetype('Customer', idx)).toEqual(new Set(['c1']));
  });

  it('leaves a leaf archetype unchanged (transitive == direct)', () => {
    expect(thingIdsOfArchetype('Warehouse', idx)).toEqual(new Set(['wh1']));
  });

  it('descends multi-level and multi-parent chains to the instance', () => {
    expect(thingIdsOfArchetype('EquipmentClass', idx)).toEqual(new Set(['fork']));
    expect(thingIdsOfArchetype('Equipment', idx)).toEqual(new Set(['fork']));
  });

  it('terminates on an is-cycle without hanging', () => {
    const cyc = buildModelIndex(
      [t('is', 'is'), t('A', 'A'), t('B', 'B')],
      [rel('A', 'B'), rel('B', 'A')],
    );
    expect(thingIdsOfArchetype('A', cyc)).toEqual(new Set()); // no instances, no infinite loop
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

  // Derived statuses nest (a shipped order is still released), so an early stage's list would
  // otherwise include every later one. excludeState drops the Things that advanced further.
  it('stateList excludeState keeps only Things that reached this state and no further', async () => {
    vi.mocked(stateApi.getThingsInState).mockImplementation(async (state: string) => ({
      StateName: state,
      Things:
        state === 'released'
          ? [{ Id: 'o1', Name: 'O-1' }, { Id: 'o2', Name: 'O-2' }, { Id: 'o3', Name: 'O-3' }]
          : [{ Id: 'o2', Name: 'O-2' }, { Id: 'o3', Name: 'O-3' }], // allocated ⊂ released
    }));
    const rows = (await resolveBinding(
      { kind: 'stateList', state: 'released', excludeState: 'allocated' },
      ctxFor(null),
    )) as Record<string, unknown>[];
    expect(rows.map((r) => r.id)).toEqual(['o1']);
    expect(stateApi.getThingsInState).toHaveBeenCalledWith('allocated');
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

  // A scope predicate can nest: the scope entity `contains` mid-level Things that in turn
  // contain the leaves a widget counts. A one-hop walk reached the mid level but never the
  // leaves under it, so every leaf-scoped widget counted 0. A scope predicate can also link
  // its members directly, which is the depth-1 case of the same walk.
  describe('scope narrowing', () => {
    // root1 contains mid1 contains {leaf1, leaf2}; root2 contains mid2 contains leaf3.
    // root1 links direct1; root2 links direct2. Leaves are two hops from their root.
    function scopeCtx(scopeId: string | null): ResolveContext {
      const t = (Id: string, Name: string): VosThing => ({ Id, Name, Properties: {} });
      const things: VosThing[] = [
        t('contains', 'contains'), t('links', 'links'),
        t('root1', 'ROOT-1'), t('root2', 'ROOT-2'),
        t('mid1', 'MID-1'), t('mid2', 'MID-2'),
        t('leaf1', 'LEAF-1'), t('leaf2', 'LEAF-2'), t('leaf3', 'LEAF-3'),
        t('direct1', 'DIRECT-1'), t('direct2', 'DIRECT-2'),
      ];
      const rel = (SubjectId: string, PredicateId: string, TargetId: string): VosRelationship => ({
        Id: `${SubjectId}-${PredicateId}-${TargetId}`, Name: `${SubjectId} ${PredicateId} ${TargetId}`,
        SubjectId, PredicateId, TargetId, Properties: {},
      });
      const relationships = [
        rel('root1', 'contains', 'mid1'), rel('mid1', 'contains', 'leaf1'), rel('mid1', 'contains', 'leaf2'),
        rel('root2', 'contains', 'mid2'), rel('mid2', 'contains', 'leaf3'),
        rel('root1', 'links', 'direct1'), rel('root2', 'links', 'direct2'),
      ];
      return { idx: buildModelIndex(things, relationships), scopeId, compareArchetype: 'Root' };
    }

    it('reaches Things nested more than one hop below the scope entity', async () => {
      vi.mocked(stateApi.getThingsInState).mockResolvedValue({
        StateName: 'flagged',
        Things: [{ Id: 'leaf1', Name: 'LEAF-1' }, { Id: 'leaf2', Name: 'LEAF-2' }, { Id: 'leaf3', Name: 'LEAF-3' }],
      });
      const v = await resolveBinding(
        { kind: 'stateCount', state: 'flagged', scope: { viaPredicate: 'contains', direction: 'out' } },
        scopeCtx('root1'),
      );
      expect(v).toBe(2); // leaf1 + leaf2 — leaf3 hangs off root2
    });

    it('narrows a directly-linked state to the selected scope entity', async () => {
      vi.mocked(stateApi.getThingsInState).mockResolvedValue({
        StateName: 'flagged',
        Things: [{ Id: 'direct1', Name: 'DIRECT-1' }, { Id: 'direct2', Name: 'DIRECT-2' }],
      });
      const v = await resolveBinding(
        { kind: 'stateCount', state: 'flagged', scope: { viaPredicate: 'links', direction: 'out' } },
        scopeCtx('root1'),
      );
      expect(v).toBe(1);
    });

    it('counts across every entity when no scope is selected', async () => {
      vi.mocked(stateApi.getThingsInState).mockResolvedValue({
        StateName: 'flagged',
        Things: [{ Id: 'direct1', Name: 'DIRECT-1' }, { Id: 'direct2', Name: 'DIRECT-2' }],
      });
      const v = await resolveBinding(
        { kind: 'stateCount', state: 'flagged', scope: { viaPredicate: 'links', direction: 'out' } },
        scopeCtx(null),
      );
      expect(v).toBe(2);
    });

    // A cycle is a data error, but the walk must terminate rather than hang the dashboard —
    // and looping back must not smuggle the scope entity into its own scope.
    it('terminates on a cyclic scope graph without counting the scope entity itself', async () => {
      const ctx = scopeCtx('root1');
      const cycle = { Id: 'leaf1-contains-root1', Name: 'leaf1 contains root1',
        SubjectId: 'leaf1', PredicateId: 'contains', TargetId: 'root1', Properties: {} };
      ctx.idx = buildModelIndex(
        [...ctx.idx.byId.values()],
        [...ctx.idx.relationships, cycle],
      );
      vi.mocked(stateApi.getThingsInState).mockResolvedValue({
        StateName: 'flagged',
        Things: [{ Id: 'leaf1', Name: 'LEAF-1' }, { Id: 'leaf2', Name: 'LEAF-2' }, { Id: 'root1', Name: 'ROOT-1' }],
      });
      const v = await resolveBinding(
        { kind: 'stateCount', state: 'flagged', scope: { viaPredicate: 'contains', direction: 'out' } },
        ctx,
      );
      expect(v).toBe(2); // leaf1 + leaf2 — root1 is the scope, not a member of it
    });
  });

  describe('filterRows', () => {
    const rows = [
      { id: 'a1', name: 'THING-1001', grouping: 'alpha', rank: 3 },
      { id: 'a2', name: 'THING-2002', grouping: 'beta', rank: 1 },
    ];

    it('returns rows unchanged for an empty query', () => {
      expect(filterRows(rows, '')).toBe(rows);
      expect(filterRows(rows, '   ')).toBe(rows);
    });

    it('matches case-insensitively across all values by default', () => {
      expect(filterRows(rows, 'alpha').map((r) => r.id)).toEqual(['a1']);
      expect(filterRows(rows, 'thing-2002').map((r) => r.id)).toEqual(['a2']);
      expect(filterRows(rows, '1').map((r) => r.id)).toEqual(['a1', 'a2']); // matches rank 1 and THING-1001
    });

    it('restricts matching to the given keys', () => {
      // "1" appears in name (THING-1001) but searchKeys limits to grouping, so no match.
      expect(filterRows(rows, '1', ['grouping'])).toEqual([]);
      expect(filterRows(rows, 'bet', ['grouping']).map((r) => r.id)).toEqual(['a2']);
    });
  });
});
