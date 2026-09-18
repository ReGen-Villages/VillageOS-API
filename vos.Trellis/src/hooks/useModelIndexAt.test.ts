import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import type { TemporalSnapshot, VosThing, VosRelationship } from '../types/vos';

vi.mock('../api/modelApi', () => ({ modelApi: { getAtTime: vi.fn() } }));

import { modelApi } from '../api/modelApi';
import { buildModelIndex, thingIdsOfArchetype } from '../api/dashboardApi';
import { useModelIndexAt } from './useModelIndexAt';

const INSTANT = '2026-09-01T09:00:00.000Z';
const LATER = '2026-09-02T09:00:00.000Z';

function thing(Id: string, Name: string, IsArchetype = false): VosThing {
  return { Id, Name, Properties: {}, IsArchetype };
}
function rel(Id: string, SubjectId: string, PredicateId: string, TargetId: string): VosRelationship {
  return { Id, SubjectId, PredicateId, TargetId, Properties: {} };
}

const live = buildModelIndex(
  [thing('is', 'is'), thing('spring', 'Spring', true), thing('hot', 'Hot spring', true), thing('s1', 'SPRING-1')],
  [rel('i1', 'hot', 'is', 'spring'), rel('i2', 's1', 'is', 'hot')],
);

const stood: TemporalSnapshot = {
  Timestamp: INSTANT,
  Things: [
    { Id: 'is', Name: 'is', Properties: {}, InheritedOverrides: {} },
    { Id: 'spring', Name: 'Spring', Properties: {}, InheritedOverrides: {} },
    { Id: 'hot', Name: 'Hot spring', Properties: {}, InheritedOverrides: {} },
    { Id: 's1', Name: 'SPRING-1', Properties: { flow: 4 }, InheritedOverrides: { spring: { SourceId: 'spring', SourceName: 'Spring', Properties: { capacity: 9 }, Inherited: {} } } },
  ],
  Relationships: [
    { Id: 'i1', SubjectId: 'hot', PredicateId: 'is', TargetId: 'spring', Properties: {}, InheritedOverrides: {} },
    { Id: 'i2', SubjectId: 's1', PredicateId: 'is', TargetId: 'hot', Properties: {}, InheritedOverrides: {} },
  ],
};

describe('useModelIndexAt', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.useFakeTimers();
    vi.mocked(modelApi.getAtTime).mockResolvedValue(stood);
  });
  afterEach(() => vi.useRealTimers());

  it('reads nothing while no instant is chosen', () => {
    const { result } = renderHook(() => useModelIndexAt(undefined, live));
    expect(result.current).toEqual({ index: null, failed: false });
    expect(modelApi.getAtTime).not.toHaveBeenCalled();
  });

  it('lets the instant settle before it reads, and abandons a read the next instant supersedes', async () => {
    const { rerender } = renderHook(({ instant }) => useModelIndexAt(instant, live), { initialProps: { instant: INSTANT } });
    await act(async () => { await vi.advanceTimersByTimeAsync(100); });
    expect(modelApi.getAtTime).not.toHaveBeenCalled();

    rerender({ instant: LATER });
    await act(async () => { await vi.advanceTimersByTimeAsync(500); });

    expect(modelApi.getAtTime).toHaveBeenCalledTimes(1);
    expect(modelApi.getAtTime).toHaveBeenCalledWith(LATER, expect.any(AbortSignal));
  });

  it('indexes the model as it stood, with the kind declarations the live model carries', async () => {
    const { result } = renderHook(() => useModelIndexAt(INSTANT, live));
    await act(async () => { await vi.advanceTimersByTimeAsync(500); });

    const index = result.current.index!;
    expect(index.byId.get('s1')?.Properties).toEqual({ flow: 4 });
    expect(index.byId.get('s1')?.InheritedOverrides?.spring?.Properties).toEqual({ capacity: 9 });
    expect([...thingIdsOfArchetype('Spring', index)]).toEqual(['s1']);
  });

  it('says a moment was refused, and only that moment', async () => {
    vi.mocked(modelApi.getAtTime).mockRejectedValue(new Error('refused'));
    const { result, rerender } = renderHook(({ instant }) => useModelIndexAt(instant, live), { initialProps: { instant: INSTANT } });
    await act(async () => { await vi.advanceTimersByTimeAsync(500); });
    expect(result.current.failed).toBe(true);

    rerender({ instant: LATER });
    expect(result.current.failed).toBe(false);
  });
});
