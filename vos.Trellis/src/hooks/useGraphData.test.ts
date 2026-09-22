import { describe, it, expect } from 'vitest';
import { renderHook } from '@testing-library/react';
import { useGraphData } from './useGraphData';
import type { VosThing } from '../types/vos';

function makeThing(id: string, props: Record<string, unknown> = {}): VosThing {
  return { Id: id, Name: id, Properties: props } as VosThing;
}

const baseInput = {
  searchQuery: '',
  caseSensitive: false,
  exactMatch: false,
  useRegex: false,
};

describe('useGraphData', () => {
  it('returns all things', () => {
    const things = [makeThing('a'), makeThing('b'), makeThing('c')];
    const { result } = renderHook(() => useGraphData({ ...baseInput, things, relationships: [] }));

    expect(result.current.filteredThings).toHaveLength(3);
  });

  it('search filters things by name', () => {
    const things = [makeThing('Building A'), makeThing('Wall 1'), makeThing('Building B')];

    const { result } = renderHook(() =>
      useGraphData({ ...baseInput, things, relationships: [], searchQuery: 'Building' }),
    );

    expect(result.current.matchCount).toBe(2);
    expect(result.current.filteredThings.length).toBeGreaterThanOrEqual(2);
  });

  it('empty search returns all things with zero match count', () => {
    const things = [makeThing('a'), makeThing('b')];

    const { result } = renderHook(() =>
      useGraphData({ ...baseInput, things, relationships: [], searchQuery: '' }),
    );

    expect(result.current.filteredThings).toHaveLength(2);
    expect(result.current.matchCount).toBe(0);
  });
});
