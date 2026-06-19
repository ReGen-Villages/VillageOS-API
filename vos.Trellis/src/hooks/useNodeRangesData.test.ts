import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, act, waitFor } from '@testing-library/react';
import { useNodeRangesData } from './useNodeRangesData';
import type { ThingRangeSummary } from '../types/vos';

// ---------------------------------------------------------------------------
// Mocks
// ---------------------------------------------------------------------------

const mockGetSummary = vi.fn();

vi.mock('../api/rangeApi', () => ({
  rangeApi: {
    getSummary: (...args: unknown[]) => mockGetSummary(...args),
  },
}));

function emptySummary(thingId = 't1'): ThingRangeSummary {
  return {
    ThingId: thingId,
    ThingName: 'T',
    OwnRanges: [],
    InheritedRanges: [],
    CurrentStates: [],
    RangeEvaluations: [],
    OutOfBoundsCount: 0,
    Relationships: [],
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  mockGetSummary.mockResolvedValue(emptySummary());
});

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('useNodeRangesData', () => {
  describe('snapshot behavior', () => {
    it('does not fetch when tab is not ranges', async () => {
      renderHook(() => useNodeRangesData('t1', 'properties', 0));
      await act(() => Promise.resolve());
      expect(mockGetSummary).not.toHaveBeenCalled();
    });

    it('fetches summary when tab is ranges on mount', async () => {
      const { result } = renderHook(() => useNodeRangesData('t1', 'ranges', 0));
      await waitFor(() => expect(result.current.rangesLoading).toBe(false));
      expect(mockGetSummary).toHaveBeenCalledWith('t1');
      expect(mockGetSummary).toHaveBeenCalledTimes(1);
    });

    it('does not re-fetch when statesVersion changes while tab is already open', async () => {
      const { result, rerender } = renderHook(
        ({ version }) => useNodeRangesData('t1', 'ranges', version),
        { initialProps: { version: 0 } },
      );
      await waitFor(() => expect(result.current.rangesLoading).toBe(false));
      expect(mockGetSummary).toHaveBeenCalledTimes(1);

      // Simulate an SSE event bumping statesVersion
      rerender({ version: 1 });
      await act(() => Promise.resolve());
      rerender({ version: 2 });
      await act(() => Promise.resolve());

      // Should still be exactly 1 fetch — snapshot hasn't changed
      expect(mockGetSummary).toHaveBeenCalledTimes(1);
    });

    it('re-fetches when navigating away and back to ranges tab', async () => {
      const { result, rerender } = renderHook(
        ({ tab, version }: { tab: string; version: number }) =>
          useNodeRangesData('t1', tab, version),
        { initialProps: { tab: 'ranges', version: 0 } },
      );
      await waitFor(() => expect(result.current.rangesLoading).toBe(false));
      expect(mockGetSummary).toHaveBeenCalledTimes(1);

      // Navigate away
      rerender({ tab: 'properties', version: 1 });
      await act(() => Promise.resolve());

      // Navigate back
      const callsBefore = mockGetSummary.mock.calls.length;
      rerender({ tab: 'ranges', version: 2 });
      await waitFor(() => expect(mockGetSummary.mock.calls.length).toBeGreaterThan(callsBefore));
    });

    it('re-fetches when thingId changes while on ranges tab', async () => {
      const { result, rerender } = renderHook(
        ({ thingId }: { thingId: string }) =>
          useNodeRangesData(thingId, 'ranges', 0),
        { initialProps: { thingId: 't1' } },
      );
      await waitFor(() => expect(result.current.rangesLoading).toBe(false));
      expect(mockGetSummary).toHaveBeenCalledTimes(1);

      mockGetSummary.mockResolvedValue(emptySummary('t2'));
      rerender({ thingId: 't2' });
      await waitFor(() => expect(mockGetSummary).toHaveBeenCalledTimes(2));
      expect(mockGetSummary).toHaveBeenLastCalledWith('t2');
    });
  });

  describe('loading state', () => {
    it('sets rangesLoading during fetch', async () => {
      let resolve!: (v: ThingRangeSummary) => void;
      mockGetSummary.mockImplementation(() => new Promise((r) => { resolve = r; }));

      const { result } = renderHook(() => useNodeRangesData('t1', 'ranges', 0));

      await waitFor(() => expect(result.current.rangesLoading).toBe(true));

      await act(async () => { resolve(emptySummary()); });
      await waitFor(() => expect(result.current.rangesLoading).toBe(false));
    });

    it('sets data to null on fetch error', async () => {
      mockGetSummary.mockRejectedValue(new Error('network'));

      const { result } = renderHook(() => useNodeRangesData('t1', 'ranges', 0));

      await waitFor(() => expect(result.current.rangesLoading).toBe(false));
      expect(result.current.rangesData).toBeNull();
      expect(result.current.statesData).toBeNull();
    });
  });

  describe('data mapping', () => {
    it('maps summary into rangesData and statesData', async () => {
      mockGetSummary.mockResolvedValue({
        ...emptySummary(),
        OwnRanges: [{ Name: 'hot', Criteria: 'temp > 100', IsInherited: false, ActiveBindings: 0, Bindings: [] }],
        CurrentStates: ['hot'],
        RangeEvaluations: [{ RangeName: 'hot', IsActive: true, Criteria: 'temp > 100' }],
        OutOfBoundsCount: 1,
      });

      const { result } = renderHook(() => useNodeRangesData('t1', 'ranges', 0));

      await waitFor(() => expect(result.current.rangesLoading).toBe(false));
      expect(result.current.rangesData?.OwnRanges).toHaveLength(1);
      expect(result.current.statesData?.CurrentStates).toEqual(['hot']);
      expect(result.current.statesData?.OutOfBoundsCount).toBe(1);
    });

    it('maps relationship summaries into relRangesEntries', async () => {
      mockGetSummary.mockResolvedValue({
        ...emptySummary(),
        Relationships: [
          {
            RelationshipId: 'r1',
            RelationshipName: 'rel1',
            SubjectId: 's1',
            PredicateId: 'p1',
            TargetId: 't1',
            SubjectName: 'Subject',
            PredicateName: 'connects',
            TargetName: 'Target',
            OwnRanges: [{ Name: 'heavy', Criteria: 'w > 30', IsInherited: false, ActiveBindings: 0, Bindings: [] }],
            CurrentStates: ['heavy'],
            RangeEvaluations: [{ RangeName: 'heavy', IsActive: true, Criteria: 'w > 30' }],
            OutOfBoundsCount: 0,
          },
        ],
      });

      const { result } = renderHook(() => useNodeRangesData('t1', 'ranges', 0));

      await waitFor(() => expect(result.current.rangesLoading).toBe(false));
      expect(result.current.relRangesEntries).toHaveLength(1);

      const entry = result.current.relRangesEntries[0];
      expect(entry.relationshipId).toBe('r1');
      expect(entry.label).toBe('Subject → connects → Target');
      expect(entry.rangesData.OwnRanges).toHaveLength(1);
      expect(entry.statesData.CurrentStates).toEqual(['heavy']);
    });

    it('returns empty relRangesEntries when summary has no relationships', async () => {
      const { result } = renderHook(() => useNodeRangesData('t1', 'ranges', 0));

      await waitFor(() => expect(result.current.rangesLoading).toBe(false));
      expect(result.current.relRangesEntries).toHaveLength(0);
    });
  });

  describe('refresh', () => {
    it('re-fetches when refresh is called', async () => {
      const { result } = renderHook(() => useNodeRangesData('t1', 'ranges', 0));
      await waitFor(() => expect(result.current.rangesLoading).toBe(false));
      expect(mockGetSummary).toHaveBeenCalledTimes(1);

      mockGetSummary.mockResolvedValue({
        ...emptySummary(),
        CurrentStates: ['updated'],
      });

      act(() => { result.current.refresh(); });
      await waitFor(() => expect(result.current.statesData?.CurrentStates).toEqual(['updated']));
      expect(mockGetSummary).toHaveBeenCalledTimes(2);
    });

    it('returns a stable refresh function across renders', async () => {
      const { result, rerender } = renderHook(
        ({ version }) => useNodeRangesData('t1', 'ranges', version),
        { initialProps: { version: 0 } },
      );
      await waitFor(() => expect(result.current.rangesLoading).toBe(false));

      const firstRefresh = result.current.refresh;
      rerender({ version: 1 });
      expect(result.current.refresh).toBe(firstRefresh);
    });
  });
});
