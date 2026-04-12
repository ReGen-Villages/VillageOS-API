import { describe, it, expect } from 'vitest';
import { renderHook } from '@testing-library/react';
import { useGraphData } from './useGraphData';
import type { VosThing, VosRelationship } from '../types/vos';

function makeThing(id: string, props: Record<string, unknown> = {}): VosThing {
  return { Id: id, Name: id, Properties: props } as VosThing;
}

function makeRel(subj: string, pred: string, targ: string): VosRelationship {
  return { Id: `${subj}-${pred}-${targ}`, SubjectId: subj, PredicateId: pred, TargetId: targ, Name: '', Properties: {} } as VosRelationship;
}

const baseInput = {
  searchQuery: '',
  caseSensitive: false,
  exactMatch: false,
  useRegex: false,
  mapEnabled: false,
  showAllThings: false,
  hideOrphanSites: true,
};

describe('useGraphData', () => {
  it('returns all things when map is disabled', () => {
    const things = [makeThing('a'), makeThing('b'), makeThing('c')];
    const rels: VosRelationship[] = [];

    const { result } = renderHook(() => useGraphData({ ...baseInput, things, relationships: rels }));

    expect(result.current.filteredThings).toHaveLength(3);
    expect(result.current.hasGeoNodes).toBe(false);
  });

  it('hasGeoNodes is true when a thing has latitude + longitude', () => {
    const things = [
      makeThing('a', { latitude: 41.0, longitude: -70.0 }),
      makeThing('b'),
    ];

    const { result } = renderHook(() => useGraphData({ ...baseInput, things, relationships: [] }));

    expect(result.current.hasGeoNodes).toBe(true);
  });

  it('hasGeoNodes is true when a thing has geometry', () => {
    const things = [makeThing('a', { geometry: { format: 'ifc' } })];

    const { result } = renderHook(() => useGraphData({ ...baseInput, things, relationships: [] }));

    expect(result.current.hasGeoNodes).toBe(true);
  });

  it('in map mode with hideOrphanSites, only __IsMapSurfaceThing things pass', () => {
    const things = [
      makeThing('primary', { __IsSurface: true, __IsMapSurfaceThing: true }),
      makeThing('orphan', { __IsSurface: true }),
      makeThing('non-surface'),
    ];

    const { result } = renderHook(() =>
      useGraphData({ ...baseInput, things, relationships: [], mapEnabled: true, hideOrphanSites: true }),
    );

    const ids = result.current.filteredThings.map((t) => t.Id);
    expect(ids).toContain('primary');
    expect(ids).not.toContain('orphan');
    expect(ids).not.toContain('non-surface');
  });

  it('in map mode without hideOrphanSites, all __IsSurface things pass', () => {
    const things = [
      makeThing('primary', { __IsSurface: true, __IsMapSurfaceThing: true }),
      makeThing('orphan', { __IsSurface: true }),
      makeThing('non-surface'),
    ];

    const { result } = renderHook(() =>
      useGraphData({ ...baseInput, things, relationships: [], mapEnabled: true, hideOrphanSites: false }),
    );

    const ids = result.current.filteredThings.map((t) => t.Id);
    expect(ids).toContain('primary');
    expect(ids).toContain('orphan');
    expect(ids).not.toContain('non-surface');
  });

  it('containment predicates are excluded from neighbor expansion', () => {
    const containsPred = makeThing('pred-contains', { __IsMapContainmentPredicate: true });
    const isPred = makeThing('pred-is');
    const surface = makeThing('site', { __IsSurface: true, __IsMapSurfaceThing: true });
    const child = makeThing('wall');
    const typeTarget = makeThing('wallType');

    const rels = [
      makeRel('site', 'pred-contains', 'wall'),
      makeRel('wall', 'pred-is', 'wallType'),
    ];

    const { result } = renderHook(() =>
      useGraphData({
        ...baseInput,
        things: [containsPred, isPred, surface, child, typeTarget],
        relationships: rels,
        mapEnabled: true,
        hideOrphanSites: true,
      }),
    );

    const ids = result.current.filteredThings.map((t) => t.Id);
    expect(ids).toContain('site');
    expect(ids).toContain('pred-contains'); // predicates always kept
    expect(ids).toContain('pred-is');
    expect(ids).not.toContain('wall'); // containment child excluded
  });

  it('search filters things by name', () => {
    const things = [makeThing('Building A'), makeThing('Wall 1'), makeThing('Building B')];

    const { result } = renderHook(() =>
      useGraphData({ ...baseInput, things, relationships: [], searchQuery: 'Building' }),
    );

    expect(result.current.matchCount).toBe(2);
    expect(result.current.filteredThings.length).toBeGreaterThanOrEqual(2);
  });

  it('empty search returns all things', () => {
    const things = [makeThing('a'), makeThing('b')];

    const { result } = renderHook(() =>
      useGraphData({ ...baseInput, things, relationships: [], searchQuery: '' }),
    );

    expect(result.current.filteredThings).toHaveLength(2);
    expect(result.current.matchCount).toBe(0); // no search = 0 matches
  });

  it('showAllThings in map mode includes things with geometry', () => {
    const things = [
      makeThing('with-geo', { geometry: { format: 'ifc' } }),
      makeThing('with-fp', { footprint: '{}' }),
      makeThing('no-geo'),
    ];

    const { result } = renderHook(() =>
      useGraphData({ ...baseInput, things, relationships: [], mapEnabled: true, showAllThings: true }),
    );

    const ids = result.current.filteredThings.map((t) => t.Id);
    expect(ids).toContain('with-geo');
    expect(ids).toContain('with-fp');
    expect(ids).not.toContain('no-geo');
  });
});
