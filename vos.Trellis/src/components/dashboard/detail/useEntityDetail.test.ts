import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';

const mockGetStateTransitions = vi.fn();
const stateReads = vi.fn();

vi.mock('../../../api/client', () => ({ apiClient: { get: (path: string) => stateReads(path) } }));
vi.mock('../../../api/stateApi', () => ({
  stateApi: {
    getStateTransitions: (id: string, _from?: string, _to?: string, signal?: AbortSignal) =>
      mockGetStateTransitions(id, signal),
  },
}));

import { buildModelIndex } from '../../../api/dashboardApi';
import type { VosThing, VosRelationship } from '../../../types/vos';
import { useModelStore } from '../../../stores/modelStore';
import { useEntityDetail } from './useEntityDetail';

function thing(Id: string, Name: string): VosThing {
  return { Id, Name, Properties: {} };
}
function rel(Id: string, SubjectId: string, PredicateId: string, TargetId: string): VosRelationship {
  return { Id, Name: Id, SubjectId, PredicateId, TargetId, Properties: {} };
}

// root -has-> child. One related Thing, so each round is a small, countable fan-out.
function index() {
  return buildModelIndex(
    [thing('root', 'ROOT-1'), thing('child', 'CHILD-1'), thing('has', 'has')],
    [rel('r1', 'root', 'has', 'child')],
  );
}

describe('useEntityDetail', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useModelStore.getState().clear();
    mockGetStateTransitions.mockResolvedValue({
      ThingId: 'root',
      ThingName: 'ROOT-1',
      Coverage: { Source: 'in-memory', From: '2026-07-17T00:00:00Z', To: '2026-07-17T01:00:00Z' },
      Transitions: [],
    });
    vi.useFakeTimers();
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  const detail = { relations: [{ predicate: 'has', direction: 'out' as const }] };

  async function settle() {
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });
  }

  const rounds = () => mockGetStateTransitions.mock.calls.length;

  // The dashboard bumps `nonce` every 400 ms while a sim runs. A round costs a history request,
  // so following every bump would put one open window into requests per second. The throttle is
  // leading-edge — the first bump after a quiet period refreshes promptly — so what must not
  // happen is a round per bump, not a round at all.
  it('does not start a fetch round per nonce bump', async () => {
    const idx = index();
    const { rerender } = renderHook(({ nonce }) => useEntityDetail(idx, 'root', detail, nonce), {
      initialProps: { nonce: 0 },
    });
    await settle();
    expect(rounds()).toBe(1);

    const bumps = 5; // ~2 s of dashboard bumps, inside one 5 s throttle window
    for (let n = 1; n <= bumps; n++) {
      rerender({ nonce: n });
      await act(async () => {
        await vi.advanceTimersByTimeAsync(400);
      });
    }

    expect(rounds()).toBeLessThan(bumps);
    expect(rounds()).toBeLessThanOrEqual(2); // initial round + one leading-edge refresh
  });

  // Guards against throttle-by-debounce: a value that keeps changing resets a debounce
  // forever, so the window would never refresh while the sim is busy — the failure mode
  // is silent staleness rather than a visible error.
  it('still refreshes under a continuously bumping nonce', async () => {
    const idx = index();
    const { rerender } = renderHook(({ nonce }) => useEntityDetail(idx, 'root', detail, nonce), {
      initialProps: { nonce: 0 },
    });
    await settle();
    const afterFirstRound = rounds();

    for (let n = 1; n <= 30; n++) {
      rerender({ nonce: n });
      await act(async () => {
        await vi.advanceTimersByTimeAsync(400);
      });
    }

    expect(rounds()).toBeGreaterThan(afterFirstRound);
  });

  it('abandons a superseded history round', async () => {
    const idx = index();
    const { rerender, unmount } = renderHook(({ nonce }) => useEntityDetail(idx, 'root', detail, nonce), {
      initialProps: { nonce: 0 },
    });
    await settle();

    const signal: AbortSignal = mockGetStateTransitions.mock.calls[0][1];
    expect(signal.aborted).toBe(false);

    rerender({ nonce: 1 });
    unmount();
    expect(signal.aborted).toBe(true);
  });

  it('shows the states the snapshot seeded for the root and its related Things, asking for none', async () => {
    useModelStore.getState().seedThingStates(new Map([['root', ['flagged']], ['child', ['metered', 'verified']]]));
    const idx = index();
    const { result } = renderHook(() => useEntityDetail(idx, 'root', detail, 0));
    await settle();

    expect(result.current.statesById.get('root')).toEqual(['flagged']);
    expect(result.current.statesById.get('child')).toEqual(['metered', 'verified']);
    expect(stateReads).not.toHaveBeenCalled();
  });

  // Reading the states back would pass without the card watching anything: a hook re-rendered for
  // any other reason reads them as they stand. Counting renders holds that a state moving is itself
  // what redraws the card.
  it('redraws when a state moves, with nothing else changing', async () => {
    useModelStore.setState({ things: [thing('root', 'ROOT-1')], relationships: [] });
    useModelStore.getState().seedThingStates(new Map([['root', ['flagged']]]));
    const idx = index();
    let renders = 0;
    const { result } = renderHook(() => {
      renders += 1;
      return useEntityDetail(idx, 'root', detail, 0);
    });
    await settle();
    const before = renders;

    await act(async () => {
      useModelStore.getState().applyBatch({ thingStateUpdates: [{ id: 'root', states: ['cleared'] }] });
    });

    expect(renders).toBeGreaterThan(before);
    expect(result.current.statesById.get('root')).toEqual(['cleared']);
  });

  it('resolves the configured relations against the model', async () => {
    const idx = index();
    const { result } = renderHook(() => useEntityDetail(idx, 'root', detail, 0));
    await settle();

    const edge = result.current.relations[0].edges[0];
    expect(edge.thingId).toBe('child');
    expect(edge.relatedName).toBe('CHILD-1');
  });

  it('fetches state history for the root only, not for related Things', async () => {
    const idx = index();
    renderHook(() => useEntityDetail(idx, 'root', detail, 0));
    await settle();

    expect(mockGetStateTransitions.mock.calls.map((c) => c[0])).toEqual(['root']);
  });

  it('exposes the root state changes and coverage once resolved', async () => {
    mockGetStateTransitions.mockResolvedValue({
      ThingId: 'root',
      ThingName: 'ROOT-1',
      Coverage: { Source: 'in-memory', From: '2026-07-17T00:00:00Z', To: '2026-07-17T01:00:00Z' },
      Transitions: [
        { At: '2026-07-17T00:30:00Z', Entered: ['allocated'], Exited: [], States: ['allocated'], TriggeringProperty: 'temp', OldValue: 50, NewValue: 150 },
      ],
    });
    const idx = index();
    const { result } = renderHook(() => useEntityDetail(idx, 'root', detail, 0));
    await settle();

    expect(result.current.stateChanges).toEqual([{ at: '2026-07-17T00:30:00Z', entered: ['allocated'], exited: [] }]);
    expect(result.current.coverage?.Source).toBe('in-memory');
  });

  // A model with no active reactive engine 503s on state history. The window must still render
  // its states and relations rather than losing the whole round to one rejected request.
  it('leaves coverage null and still resolves when the endpoint rejects', async () => {
    mockGetStateTransitions.mockRejectedValue(new Error('503'));
    const idx = index();
    const { result } = renderHook(() => useEntityDetail(idx, 'root', detail, 0));
    await settle();

    expect(result.current.coverage).toBeNull();
    expect(result.current.stateChanges).toEqual([]);
    expect(result.current.loading).toBe(false);
    expect(result.current.relations[0].edges[0].thingId).toBe('child');
  });
});
