import { describe, it, expect, vi, beforeEach } from 'vitest';
import type { VosThing, VosRelationship } from '../types/vos';
import type { Binding } from '../types/dashboard';

vi.mock('./stateApi', () => ({
  stateApi: { getThingsInState: vi.fn() },
}));

vi.mock('./client', () => ({
  apiClient: { post: vi.fn() },
}));

import { stateApi } from './stateApi';
import { apiClient } from './client';
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

// ---- a tiny synthetic model: 2 villages + a Dashboard config ----
const SPEC = {
  title: 'Ops',
  compare: { label: 'site', archetype: 'Village' },
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
    t('arch-vil', 'Village'),
    t('dash1', 'Operations Dashboard', { spec: JSON.stringify(SPEC) }),
    t('vil1', 'V-1', { self_sufficiency_rate: 98.9, land_utilization: 0.81 }),
    t('vil2', 'V-2', { self_sufficiency_rate: 94.1, land_utilization: 0.93 }),
  ];
  const r = (SubjectId: string, TargetId: string): VosRelationship => ({
    Id: `${SubjectId}-is-${TargetId}`,
    Name: `${SubjectId} is ${TargetId}`,
    SubjectId,
    PredicateId: 'is',
    TargetId,
    Properties: {},
  });
  const relationships: VosRelationship[] = [r('dash1', 'arch-dash'), r('vil1', 'arch-vil'), r('vil2', 'arch-vil')];
  return { things, relationships };
}

function ctxFor(scopeId: string | null): ResolveContext {
  const { things, relationships } = model();
  return { idx: buildModelIndex(things, relationships), scopeId, compareArchetype: 'Village' };
}

async function rowsOf(binding: Binding, ctx: ResolveContext): Promise<Record<string, unknown>[]> {
  return (await resolveBinding(binding, ctx)) as Record<string, unknown>[];
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
    expect(ents.map((e) => e.name)).toEqual(['V-1', 'V-2']);
  });

  it('resolves archetype membership via is-edges', () => {
    const { things, relationships } = model();
    const idx = buildModelIndex(things, relationships);
    expect(thingsOfArchetype('Village', idx).map((x) => x.Name).sort()).toEqual(['V-1', 'V-2']);
  });
});

// Bug #5942: archetypes are subtyped (Resident is Party, GardenPlot is Location),
// so membership must be transitive over the is-chain and count instances only.
describe('thingIdsOfArchetype (transitive, instances-only)', () => {
  const t = (Id: string, Name: string): VosThing => ({ Id, Name, Properties: {} });
  const rel = (SubjectId: string, TargetId: string): VosRelationship => ({
    Id: `${SubjectId}-is-${TargetId}`, Name: `${SubjectId} is ${TargetId}`,
    SubjectId, PredicateId: 'is', TargetId, Properties: {},
  });
  // Party <- Resident(sub-archetype) <- ROWAN(instance); Village(leaf) <- V-1;
  // AssetClass <- ASC-SOLAR <- PANEL-1, and PANEL-1 also is-a Asset (multi-parent).
  const idx = buildModelIndex(
    [t('is', 'is'), t('Party', 'Party'), t('Resident', 'Resident'), t('c1', 'ROWAN'),
     t('Village', 'Village'), t('vil1', 'V-1'),
     t('AssetClass', 'AssetClass'), t('ASC', 'ASC-SOLAR'), t('Asset', 'Asset'), t('panel', 'PANEL-1')],
    [rel('Resident', 'Party'), rel('c1', 'Resident'), rel('vil1', 'Village'),
     rel('ASC', 'AssetClass'), rel('panel', 'ASC'), rel('panel', 'Asset')],
  );

  it('includes instances under a sub-archetype and excludes the sub-archetype node', () => {
    expect(thingIdsOfArchetype('Party', idx)).toEqual(new Set(['c1'])); // ROWAN, not the Resident type node
  });

  it('resolves a directly-typed instance', () => {
    expect(thingIdsOfArchetype('Resident', idx)).toEqual(new Set(['c1']));
  });

  it('leaves a leaf archetype unchanged (transitive == direct)', () => {
    expect(thingIdsOfArchetype('Village', idx)).toEqual(new Set(['vil1']));
  });

  it('descends multi-level and multi-parent chains to the instance', () => {
    expect(thingIdsOfArchetype('AssetClass', idx)).toEqual(new Set(['panel']));
    expect(thingIdsOfArchetype('Asset', idx)).toEqual(new Set(['panel']));
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
    const v = await resolveBinding({ kind: 'property', thing: '$scope', property: 'self_sufficiency_rate' }, ctxFor('vil1'));
    expect(v).toBeCloseTo(98.9);
  });

  it('property $scope averages across entities when scope is All', async () => {
    const v = await resolveBinding({ kind: 'property', thing: '$scope', property: 'self_sufficiency_rate' }, ctxFor(null));
    expect(v).toBeCloseTo((98.9 + 94.1) / 2);
  });

  // Regression (Bug #5932): under lazy inheritance an overridden value lives in
  // InheritedOverrides, not Properties. Bindings must read effective properties.
  it('property $scope resolves a value inherited from an archetype', async () => {
    const child: VosThing = {
      Id: 'vil3',
      Name: 'V-3',
      Properties: {},
      InheritedOverrides: {
        Village: {
          SourceId: 'arch-vil',
          SourceName: 'Village',
          InheritedAt: '2024-01-01',
          Properties: { self_sufficiency_rate: 88.5 },
        },
      },
    };
    const ctx: ResolveContext = { idx: buildModelIndex([child], []), scopeId: 'vil3', compareArchetype: 'Village' };
    const v = await resolveBinding({ kind: 'property', thing: '$scope', property: 'self_sufficiency_rate' }, ctx);
    expect(v).toBeCloseTo(88.5);
  });

  it('aggregate count over an archetype', async () => {
    const v = await resolveBinding({ kind: 'aggregate', archetype: 'Village', op: 'count' }, ctxFor(null));
    expect(v).toBe(2);
  });

  it('aggregate avg over a property', async () => {
    const v = await resolveBinding(
      { kind: 'aggregate', archetype: 'Village', op: 'avg', property: 'land_utilization' },
      ctxFor(null),
    );
    expect(v).toBeCloseTo((0.81 + 0.93) / 2);
  });

  it('compareEntities emits one row per entity with requested props', async () => {
    const rows = (await resolveBinding(
      { kind: 'compareEntities', properties: ['self_sufficiency_rate'] },
      ctxFor(null),
    )) as Record<string, unknown>[];
    expect(rows).toHaveLength(2);
    expect(rows[0]).toMatchObject({ name: 'V-1', self_sufficiency_rate: 98.9 });
  });

  it('stateCount counts Things returned by the state endpoint', async () => {
    vi.mocked(stateApi.getThingsInState).mockResolvedValue({
      StateName: 'harvested',
      Things: [{ Id: 'a', Name: 'A' }, { Id: 'b', Name: 'B' }, { Id: 'c', Name: 'C' }],
    });
    const v = await resolveBinding({ kind: 'stateCount', state: 'harvested' }, ctxFor(null));
    expect(v).toBe(3);
    expect(stateApi.getThingsInState).toHaveBeenCalledWith('harvested');
  });

  it('stateList enriches state rows with the Thing properties', async () => {
    vi.mocked(stateApi.getThingsInState).mockResolvedValue({
      StateName: 'below_target',
      Things: [{ Id: 'vil1', Name: 'V-1' }],
    });
    const rows = (await resolveBinding(
      { kind: 'stateList', state: 'below_target' },
      ctxFor(null),
    )) as Record<string, unknown>[];
    expect(rows[0]).toMatchObject({ id: 'vil1', name: 'V-1', self_sufficiency_rate: 98.9 });
  });

  // Derived statuses nest (a harvested plot is still planted), so an early stage's list would
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

  // Feature (#6135): the list-shaped bindings could only reach the members of one derived state
  // or the compare entities. A roster wants every Thing of an archetype whatever condition each
  // is in — a healthy idle machine is in no state at all, so no stateList ever reaches it.
  describe('thingList', () => {
    // Machine <- Robot(sub-archetype) <- RBT-1, RBT-2; Machine <- CNV-1 directly.
    // SITE-1 contains RBT-1 and CNV-1; SITE-2 contains RBT-2.
    function fleetCtx(scopeId: string | null): ResolveContext {
      const t = (Id: string, Name: string, Properties: Record<string, unknown> = {}): VosThing => ({
        Id, Name, Properties,
      });
      const things: VosThing[] = [
        t('is', 'is'), t('contains', 'contains'),
        t('arch-machine', 'Machine'), t('arch-robot', 'Robot'),
        t('site1', 'SITE-1'), t('site2', 'SITE-2'),
        t('rbt1', 'RBT-1', { duty_cycle: 0.62 }), t('rbt2', 'RBT-2', { duty_cycle: 0.41 }),
        t('cnv1', 'CNV-1', { duty_cycle: 0.88 }),
      ];
      const rel = (SubjectId: string, PredicateId: string, TargetId: string): VosRelationship => ({
        Id: `${SubjectId}-${PredicateId}-${TargetId}`, Name: `${SubjectId} ${PredicateId} ${TargetId}`,
        SubjectId, PredicateId, TargetId, Properties: {},
      });
      const relationships = [
        rel('arch-robot', 'is', 'arch-machine'),
        rel('rbt1', 'is', 'arch-robot'), rel('rbt2', 'is', 'arch-robot'), rel('cnv1', 'is', 'arch-machine'),
        rel('site1', 'contains', 'rbt1'), rel('site1', 'contains', 'cnv1'), rel('site2', 'contains', 'rbt2'),
      ];
      return { idx: buildModelIndex(things, relationships), scopeId, compareArchetype: 'Site' };
    }

    it('lists every Thing of the archetype, in no state and with no scope selected', async () => {
      const rows = await rowsOf({ kind: 'thingList', archetype: 'Machine' }, fleetCtx(null));
      expect(rows.map((r) => r.name)).toEqual(['CNV-1', 'RBT-1', 'RBT-2']);
      expect(stateApi.getThingsInState).not.toHaveBeenCalled();
    });

    it('carries each Thing effective properties into the row', async () => {
      const rows = await rowsOf({ kind: 'thingList', archetype: 'Machine' }, fleetCtx(null));
      expect(rows.find((r) => r.name === 'CNV-1')).toMatchObject({ id: 'cnv1', duty_cycle: 0.88 });
    });

    // A childless archetype answers `is` exactly like an instance, so a service-side lister has
    // to filter it out by convention; the index tells the two apart structurally instead.
    it('returns instances only — a sub-archetype is descended into, never listed', async () => {
      const rows = await rowsOf({ kind: 'thingList', archetype: 'Machine' }, fleetCtx(null));
      expect(rows.map((r) => r.id)).not.toContain('arch-robot');
    });

    it('narrows to the Things reachable from the selected scope entity', async () => {
      const rows = await rowsOf(
        { kind: 'thingList', archetype: 'Machine', scope: { viaPredicate: 'contains', direction: 'out' } },
        fleetCtx('site1'),
      );
      expect(rows.map((r) => r.name)).toEqual(['CNV-1', 'RBT-1']);
    });

    // Order is by name so a capped list is the same list every time, not whatever order the
    // archetype walk happened to produce.
    it('caps the row count at limit, taking the first by name', async () => {
      const rows = await rowsOf({ kind: 'thingList', archetype: 'Machine', limit: 2 }, fleetCtx(null));
      expect(rows.map((r) => r.name)).toEqual(['CNV-1', 'RBT-1']);
    });

    it('resolves an unknown archetype to an empty list', async () => {
      const rows = await rowsOf({ kind: 'thingList', archetype: 'Spaceship' }, fleetCtx(null));
      expect(rows).toEqual([]);
    });
  });

  // Feature (#6140): a row carried only what the row's own Thing stores, so a column whose value
  // sits on an edge (the archetype a Thing is, where it stands, what an open command points at)
  // or in the platform's derived condition could not be expressed at all.
  describe('columns an edge or a derived state answers', () => {
    // Machine <- Robot <- RBT-1, RBT-2. RBT-1 is at LOC-A and operates_in ZN-1; RBT-2 is at LOC-B.
    // MOVE-1 targets RBT-1 and references LOC-C and ZN-1; MOVE-0 targets RBT-1 and references LOC-D.
    // Only MOVE-1 is in the 'open' state, so only its destination is the one still being moved to.
    function fleet(scopeId: string | null): ResolveContext {
      const t = (Id: string, Name: string, Properties: Record<string, unknown> = {}): VosThing => ({
        Id, Name, Properties,
      });
      const things: VosThing[] = [
        t('is', 'is'), t('at', 'at'), t('operates_in', 'operates_in'),
        t('targets', 'targets'), t('references', 'references'),
        t('arch-machine', 'Machine'), t('arch-robot', 'Robot'),
        t('arch-loc', 'Location'), t('arch-zone', 'Zone'), t('arch-cmd', 'Command'),
        t('rbt1', 'RBT-1'), t('rbt2', 'RBT-2'),
        t('locA', 'LOC-A', { bay_count: 12 }), t('locB', 'LOC-B'), t('locC', 'LOC-C'), t('locD', 'LOC-D'),
        t('zn1', 'ZN-1'), t('cmd1', 'MOVE-1'), t('cmd0', 'MOVE-0'),
      ];
      const rel = (SubjectId: string, PredicateId: string, TargetId: string): VosRelationship => ({
        Id: `${SubjectId}-${PredicateId}-${TargetId}`, Name: `${SubjectId} ${PredicateId} ${TargetId}`,
        SubjectId, PredicateId, TargetId, Properties: {},
      });
      const relationships = [
        rel('arch-robot', 'is', 'arch-machine'),
        rel('rbt1', 'is', 'arch-robot'), rel('rbt2', 'is', 'arch-robot'),
        rel('locA', 'is', 'arch-loc'), rel('locB', 'is', 'arch-loc'),
        rel('locC', 'is', 'arch-loc'), rel('locD', 'is', 'arch-loc'),
        rel('zn1', 'is', 'arch-zone'), rel('cmd1', 'is', 'arch-cmd'), rel('cmd0', 'is', 'arch-cmd'),
        rel('rbt1', 'at', 'locA'), rel('rbt2', 'at', 'locB'),
        rel('rbt1', 'operates_in', 'zn1'),
        rel('cmd1', 'targets', 'rbt1'), rel('cmd1', 'references', 'locC'), rel('cmd1', 'references', 'zn1'),
        rel('cmd0', 'targets', 'rbt1'), rel('cmd0', 'references', 'locD'),
      ];
      return { idx: buildModelIndex(things, relationships), scopeId, compareArchetype: 'Machine' };
    }

    const MEMBERS: Record<string, string[]> = {
      open: ['cmd1'],
      blocked: ['rbt1'],
      reachable: ['rbt1', 'rbt2'],
      offline: [],
    };

    beforeEach(() => {
      vi.mocked(stateApi.getThingsInState).mockImplementation(async (state: string) => ({
        StateName: state,
        Things: (MEMBERS[state] ?? []).map((id) => ({ Id: id, Name: id })),
      }));
    });

    const DESTINATION_OF_OPEN_COMMAND: Binding = {
      kind: 'related',
      via: [
        { predicate: 'targets', direction: 'in', inState: 'open' },
        { predicate: 'references', archetype: 'Location' },
      ],
    };

    describe('related', () => {
      it('names the Thing one step out', async () => {
        expect(await resolveBinding({ kind: 'related', via: [{ predicate: 'at' }] }, fleet('rbt1'))).toBe('LOC-A');
      });

      // The is-edge answers what kind of machine this is — the sub-archetype it was typed with,
      // which is the word an operator reads, not the parent archetype the roster was listed by.
      it('names the archetype a Thing is, not the archetype it was listed under', async () => {
        expect(await resolveBinding({ kind: 'related', via: [{ predicate: 'is' }] }, fleet('rbt1'))).toBe('Robot');
      });

      it('follows an inbound step and then an outbound one to reach a value two edges away', async () => {
        expect(await resolveBinding(DESTINATION_OF_OPEN_COMMAND, fleet('rbt1'))).toBe('LOC-C');
      });

      // Without the state filter the walk reaches every command ever issued to the machine, so
      // the cell would name a destination that was reached and left long ago.
      it('keeps only the neighbour still in the given state', async () => {
        const everyCommand: Binding = {
          kind: 'related',
          via: [{ predicate: 'targets', direction: 'in' }, { predicate: 'references', archetype: 'Location' }],
        };
        expect(await resolveBinding(everyCommand, fleet('rbt1'))).toBe('LOC-C, LOC-D');
      });

      it('drops the neighbour that is in the given state', async () => {
        const notOpen: Binding = {
          kind: 'related',
          via: [
            { predicate: 'targets', direction: 'in', notInState: 'open' },
            { predicate: 'references', archetype: 'Location' },
          ],
        };
        expect(await resolveBinding(notOpen, fleet('rbt1'))).toBe('LOC-D');
      });

      it('narrows a predicate that reaches several archetypes to the one asked for', async () => {
        const zoneOfCommand: Binding = {
          kind: 'related', thing: 'MOVE-1', via: [{ predicate: 'references', archetype: 'Zone' }],
        };
        expect(await resolveBinding(zoneOfCommand, fleet(null))).toBe('ZN-1');
      });

      // Alphabetical rather than relationship order, so a cell naming several Things reads the
      // same on every refresh.
      it('joins several matches in a stable order, starting from a Thing named in the binding', async () => {
        const references: Binding = { kind: 'related', thing: 'MOVE-1', via: [{ predicate: 'references' }] };
        expect(await resolveBinding(references, fleet(null))).toBe('LOC-C, ZN-1');
      });

      it('reads a property of the reached Thing, keeping a lone number a number', async () => {
        const bays: Binding = { kind: 'related', via: [{ predicate: 'at' }], property: 'bay_count' };
        expect(await resolveBinding(bays, fleet('rbt1'))).toBe(12);
      });

      it('resolves to null when the reached Thing does not carry the property', async () => {
        const bays: Binding = { kind: 'related', via: [{ predicate: 'at' }], property: 'bay_count' };
        expect(await resolveBinding(bays, fleet('rbt2'))).toBeNull();
      });

      // A relationship can name an id the loaded model has no Thing for; the cell drops it rather
      // than showing a gap among the names.
      it('skips an edge pointing at a Thing the model does not hold', async () => {
        const ctx = fleet('rbt1');
        ctx.idx = buildModelIndex(
          [...ctx.idx.byId.values()],
          [...ctx.idx.relationships, { Id: 'rbt1-at-ghost', Name: 'rbt1 at ghost',
            SubjectId: 'rbt1', PredicateId: 'at', TargetId: 'ghost', Properties: {} }],
        );
        expect(await resolveBinding({ kind: 'related', via: [{ predicate: 'at' }] }, ctx)).toBe('LOC-A');
      });

      it('resolves to null when the path reaches nothing', async () => {
        expect(await resolveBinding({ kind: 'related', via: [{ predicate: 'operates_in' }] }, fleet('rbt2'))).toBeNull();
      });

      it('resolves to null when the model has no such predicate', async () => {
        expect(await resolveBinding({ kind: 'related', via: [{ predicate: 'orbits' }] }, fleet('rbt1'))).toBeNull();
      });

      it('resolves to null with nothing selected to start from', async () => {
        expect(await resolveBinding({ kind: 'related', via: [{ predicate: 'at' }] }, fleet(null))).toBeNull();
      });
    });

    describe('stateOf', () => {
      // Derived states nest, so a Thing usually holds several at once; the listed order is what
      // decides which one the cell shows.
      it('returns the first listed state the Thing holds', async () => {
        const ctx = fleet('rbt1');
        expect(await resolveBinding({ kind: 'stateOf', states: ['blocked', 'reachable'] }, ctx)).toBe('blocked');
        expect(await resolveBinding({ kind: 'stateOf', states: ['reachable', 'blocked'] }, ctx)).toBe('reachable');
      });

      it('returns null when the Thing holds none of them', async () => {
        expect(await resolveBinding({ kind: 'stateOf', states: ['blocked'] }, fleet('rbt2'))).toBeNull();
      });

      it('resolves to null with nothing selected to read the states of', async () => {
        expect(await resolveBinding({ kind: 'stateOf', states: ['blocked'] }, fleet(null))).toBeNull();
        expect(stateApi.getThingsInState).not.toHaveBeenCalled();
      });

      it('reads the states of a Thing named in the binding', async () => {
        const ctx = fleet(null);
        expect(await resolveBinding({ kind: 'stateOf', states: ['blocked'], thing: 'RBT-1' }, ctx)).toBe('blocked');
      });
    });

    // Every widget resolves its own bindings, so the same state wanted by a count, a funnel stage
    // and a table used to be three requests per refresh.
    describe('state reads shared across a refresh generation', () => {
      it('asks once for a state that several bindings of one generation want', async () => {
        const ctx: ResolveContext = { ...fleet(null), stateMembers: new Map() };
        await Promise.all([
          resolveBinding({ kind: 'stateCount', state: 'reachable' }, ctx),
          resolveBinding({ kind: 'stateList', state: 'reachable' }, ctx),
          resolveBinding({ kind: 'thingList', archetype: 'Machine',
            computed: [{ key: 'condition', value: { kind: 'stateOf', states: ['reachable'] } }] }, ctx),
        ]);
        expect(stateApi.getThingsInState).toHaveBeenCalledTimes(1);
      });

      // A shared read must not share a failure: one hiccup would otherwise stick to every later
      // reader of the generation, with nothing to retry it.
      it('retries a state read that failed instead of sharing the failure', async () => {
        const ctx: ResolveContext = { ...fleet(null), stateMembers: new Map() };
        vi.mocked(stateApi.getThingsInState)
          .mockRejectedValueOnce(new Error('broker unreachable'))
          .mockResolvedValueOnce({ StateName: 'reachable', Things: [{ Id: 'rbt1', Name: 'RBT-1' }] });
        await expect(resolveBinding({ kind: 'stateCount', state: 'reachable' }, ctx)).rejects.toThrow();
        expect(await resolveBinding({ kind: 'stateCount', state: 'reachable' }, ctx)).toBe(1);
      });
    });

    describe('computed columns on a row-producing binding', () => {
      const roster: Binding = {
        kind: 'thingList',
        archetype: 'Machine',
        computed: [
          { key: 'machine_class', value: { kind: 'related', via: [{ predicate: 'is' }] } },
          { key: 'current_location', value: { kind: 'related', via: [{ predicate: 'at' }] } },
          { key: 'destination', value: DESTINATION_OF_OPEN_COMMAND },
          { key: 'condition', value: { kind: 'stateOf', states: ['blocked', 'reachable'] } },
        ],
      };

      it('fills each row from that row own Thing', async () => {
        const rows = await rowsOf(roster, fleet(null));
        expect(rows.find((r) => r.name === 'RBT-1')).toMatchObject({
          machine_class: 'Robot', current_location: 'LOC-A', destination: 'LOC-C', condition: 'blocked',
        });
        expect(rows.find((r) => r.name === 'RBT-2')).toMatchObject({
          machine_class: 'Robot', current_location: 'LOC-B', destination: null, condition: 'reachable',
        });
      });

      // The values used to pass through a number coercion, which emptied every column carrying
      // a name or a status word.
      it('keeps a text value as text', async () => {
        const rows = await rowsOf(roster, fleet(null));
        expect(rows.every((r) => typeof r.current_location === 'string')).toBe(true);
      });

      it('leaves a column empty when its binding resolves to something no cell can hold', async () => {
        const rows = await rowsOf(
          {
            kind: 'thingList',
            archetype: 'Machine',
            computed: [{ key: 'nested', value: { kind: 'thingList', archetype: 'Location' } }],
          },
          fleet(null),
        );
        expect(rows.map((r) => r.nested)).toEqual([null, null]);
      });

      // The trap a per-row binding sets: one request per row per refresh. State reads are shared
      // across the rows of one resolution, so the count follows the listed states, not the roster.
      it('asks each state once for the whole table, not once per row', async () => {
        await rowsOf(
          {
            kind: 'thingList',
            archetype: 'Machine',
            computed: [{ key: 'condition', value: { kind: 'stateOf', states: ['offline', 'reachable'] } }],
          },
          fleet(null),
        );
        expect(vi.mocked(stateApi.getThingsInState).mock.calls.map((c) => c[0]).sort()).toEqual([
          'offline',
          'reachable',
        ]);
      });

      it('adds the same columns to a state-driven list', async () => {
        const rows = await rowsOf(
          {
            kind: 'stateList',
            state: 'blocked',
            computed: [{ key: 'current_location', value: { kind: 'related', via: [{ predicate: 'at' }] } }],
          },
          fleet(null),
        );
        expect(rows).toEqual([expect.objectContaining({ id: 'rbt1', current_location: 'LOC-A' })]);
      });
    });
  });

  // A utilization is a ratio of sums over the scope's members, which no single aggregate op
  // yields: averaging per-location ratios weights a nearly-empty plot the same as a full one.
  describe('ratio', () => {
    // V-A contains 2 locations (80 of 200 filled); V-B contains 1 (90 of 100).
    function siteCtx(scopeId: string | null): ResolveContext {
      const t = (Id: string, Name: string, Properties: Record<string, unknown> = {}): VosThing => ({
        Id, Name, Properties,
      });
      const things: VosThing[] = [
        t('is', 'is'), t('contains', 'contains'),
        t('arch-vil', 'Village'), t('arch-loc', 'Location'),
        t('vilA', 'V-A'), t('vilB', 'V-B'),
        t('znA', 'ZN-A'), t('znB', 'ZN-B'),
        t('locA1', 'LOC-A1', { contained_units: 60, capacity_units: 100 }),
        t('locA2', 'LOC-A2', { contained_units: 20, capacity_units: 100 }),
        t('locB1', 'LOC-B1', { contained_units: 90, capacity_units: 100 }),
      ];
      const rel = (SubjectId: string, PredicateId: string, TargetId: string): VosRelationship => ({
        Id: `${SubjectId}-${PredicateId}-${TargetId}`, Name: `${SubjectId} ${PredicateId} ${TargetId}`,
        SubjectId, PredicateId, TargetId, Properties: {},
      });
      const relationships = [
        rel('vilA', 'is', 'arch-vil'), rel('vilB', 'is', 'arch-vil'),
        rel('locA1', 'is', 'arch-loc'), rel('locA2', 'is', 'arch-loc'), rel('locB1', 'is', 'arch-loc'),
        rel('vilA', 'contains', 'znA'), rel('znA', 'contains', 'locA1'), rel('znA', 'contains', 'locA2'),
        rel('vilB', 'contains', 'znB'), rel('znB', 'contains', 'locB1'),
      ];
      return { idx: buildModelIndex(things, relationships), scopeId, compareArchetype: 'Village' };
    }

    const LOCATION_SCOPE = { viaPredicate: 'contains', direction: 'out' as const };
    const utilization = {
      kind: 'ratio' as const,
      numerator: { kind: 'aggregate' as const, archetype: 'Location', op: 'sum' as const,
        property: 'contained_units', scope: LOCATION_SCOPE },
      denominator: { kind: 'aggregate' as const, archetype: 'Location', op: 'sum' as const,
        property: 'capacity_units', scope: LOCATION_SCOPE },
    };

    it('divides the scoped sums for the selected Thing', async () => {
      expect(await resolveBinding(utilization, siteCtx('vilA'))).toBeCloseTo(80 / 200);
      expect(await resolveBinding(utilization, siteCtx('vilB'))).toBeCloseTo(90 / 100);
    });

    it('sums across every Thing before dividing when scope is All', async () => {
      const v = await resolveBinding(utilization, siteCtx(null));
      expect(v).toBeCloseTo(170 / 300);
      expect(v).not.toBeCloseTo((0.4 + 0.9) / 2); // not the average of per-site ratios
    });

    it('resolves to null rather than Infinity when the denominator is zero', async () => {
      const v = await resolveBinding(
        { kind: 'ratio', numerator: { kind: 'const', value: 5 }, denominator: { kind: 'const', value: 0 } },
        siteCtx(null),
      );
      expect(v).toBeNull();
    });

    // An absent property resolves to NaN, which must read as "no value" rather than propagate.
    it('resolves to null when a side is not numeric', async () => {
      const v = await resolveBinding(
        {
          kind: 'ratio',
          numerator: { kind: 'property', thing: 'vilA', property: 'missing' },
          denominator: { kind: 'const', value: 10 },
        },
        siteCtx(null),
      );
      expect(v).toBeNull();
    });

    it('gives each compared Thing its own value in a computed column', async () => {
      const rows = (await resolveBinding(
        { kind: 'compareEntities', properties: [], computed: [{ key: 'utilization', value: utilization }] },
        siteCtx(null),
      )) as Record<string, unknown>[];
      expect(rows).toHaveLength(2);
      expect(rows.find((r) => r.name === 'V-A')!.utilization).toBeCloseTo(0.4);
      expect(rows.find((r) => r.name === 'V-B')!.utilization).toBeCloseTo(0.9);
    });

    it('leaves a computed column null for a Thing with no members to measure', async () => {
      const ctx = siteCtx(null);
      const empty: VosThing = { Id: 'vilC', Name: 'V-C', Properties: {} };
      ctx.idx = buildModelIndex(
        [...ctx.idx.byId.values(), empty],
        [...ctx.idx.relationships, { Id: 'vilC-is', Name: 'vilC is arch-vil',
          SubjectId: 'vilC', PredicateId: 'is', TargetId: 'arch-vil', Properties: {} }],
      );
      const rows = (await resolveBinding(
        { kind: 'compareEntities', properties: [], computed: [{ key: 'utilization', value: utilization }] },
        ctx,
      )) as Record<string, unknown>[];
      expect(rows.find((r) => r.name === 'V-C')!.utilization).toBeNull();
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

describe('service bindings carry the selected scope', () => {
  beforeEach(() => vi.clearAllMocks());

  const binding = {
    kind: 'service' as const,
    endpoint: '/api/endpoints/metrics',
    body: { view: 'yield-series', scope: '$scope' },
    select: 'series',
  };

  it('replaces $scope with the selected entity id', async () => {
    vi.mocked(apiClient.post).mockResolvedValue({ series: [1, 2, 3] });

    const v = await resolveBinding(binding, ctxFor('vil1'));

    expect(apiClient.post).toHaveBeenCalledWith('/api/endpoints/metrics', {
      view: 'yield-series',
      scope: 'vil1',
    });
    expect(v).toEqual([1, 2, 3]);
  });

  it('sends a null scope when All is selected, so the service answers for everything', async () => {
    vi.mocked(apiClient.post).mockResolvedValue({ series: [] });

    await resolveBinding(binding, ctxFor(null));

    expect(apiClient.post).toHaveBeenCalledWith('/api/endpoints/metrics', {
      view: 'yield-series',
      scope: null,
    });
  });

  it('leaves a body with no placeholder untouched', async () => {
    vi.mocked(apiClient.post).mockResolvedValue({ rows: [] });

    await resolveBinding({ ...binding, body: { view: 'visit-schedule' }, select: 'rows' }, ctxFor('vil1'));

    expect(apiClient.post).toHaveBeenCalledWith('/api/endpoints/metrics', { view: 'visit-schedule' });
  });
});
