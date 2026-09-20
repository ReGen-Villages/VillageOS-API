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

const mockGetRelationship = vi.fn();

vi.mock('../../../api/relationshipApi', () => ({
  relationshipApi: {
    get: (id: string, signal?: AbortSignal) => mockGetRelationship(id, signal),
  },
}));

import { buildModelIndex } from '../../../api/dashboardApi';
import type { VosThing, VosRelationship } from '../../../types/vos';
import { useModelStore } from '../../../stores/modelStore';
import { useEntityDetail } from './useEntityDetail';

function thing(Id: string, Name: string): VosThing {
  return { Id, Name, Properties: {} };
}
function relationship(Id: string, SubjectId: string, PredicateId: string, TargetId: string): VosRelationship {
  return { Id, Name: Id, SubjectId, PredicateId, TargetId, Properties: {} };
}

// root -has-> child. One related Thing, so each round is a small, countable fan-out.
function index() {
  return buildModelIndex(
    [thing('root', 'ROOT-1'), thing('child', 'CHILD-1'), thing('has', 'has')],
    [relationship('r1', 'root', 'has', 'child')],
  );
}

/** The same root, with its edge dispatched through a connection the platform flagged. */
function indexWithAService() {
  return buildModelIndex(
    [
      thing('root', 'ROOT-1'),
      thing('child', 'CHILD-1'),
      thing('is', 'is'),
      thing('runs', 'runs'),
      { Id: 'connectionArchetype', Name: 'Connection', Properties: { __IsConnectionArchetype: true }, IsArchetype: true },
      { Id: 'serviceArchetype', Name: 'Service', Properties: { __IsServiceArchetype: true }, IsArchetype: true },
      thing('has', 'has'),
      thing('keeper', 'keeper service'),
    ],
    [
      relationship('w1', 'has', 'is', 'connectionArchetype'),
      relationship('w2', 'has', 'runs', 'keeper'),
      relationship('w3', 'keeper', 'is', 'serviceArchetype'),
      relationship('r1', 'root', 'has', 'child'),
    ],
  );
}

describe('useEntityDetail', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useModelStore.getState().clear();
    mockGetRelationship.mockResolvedValue({ Id: 'r1', SubjectId: 'root', PredicateId: 'has', TargetId: 'child', Properties: {} });
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
    const modelIndex = index();
    const { rerender } = renderHook(({ nonce }) => useEntityDetail(modelIndex, 'root', detail, nonce), {
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
    const modelIndex = index();
    const { rerender } = renderHook(({ nonce }) => useEntityDetail(modelIndex, 'root', detail, nonce), {
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
    const modelIndex = index();
    const { rerender, unmount } = renderHook(({ nonce }) => useEntityDetail(modelIndex, 'root', detail, nonce), {
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
    const modelIndex = index();
    const { result } = renderHook(() => useEntityDetail(modelIndex, 'root', detail, 0));
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
    const modelIndex = index();
    let renders = 0;
    const { result } = renderHook(() => {
      renders += 1;
      return useEntityDetail(modelIndex, 'root', detail, 0);
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
    const modelIndex = index();
    const { result } = renderHook(() => useEntityDetail(modelIndex, 'root', detail, 0));
    await settle();

    const edge = result.current.relations[0].edges[0];
    expect(edge.thingId).toBe('child');
    expect(edge.relatedName).toBe('CHILD-1');
  });

  it('fetches state history for the root only, not for related Things', async () => {
    const modelIndex = index();
    renderHook(() => useEntityDetail(modelIndex, 'root', detail, 0));
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
    const modelIndex = index();
    const { result } = renderHook(() => useEntityDetail(modelIndex, 'root', detail, 0));
    await settle();

    expect(result.current.stateChanges).toEqual([{ at: '2026-07-17T00:30:00Z', entered: ['allocated'], exited: [] }]);
    expect(result.current.coverage?.Source).toBe('in-memory');
  });

  // A model with no active reactive engine 503s on state history. The window must still render
  // its states and relations rather than losing the whole round to one rejected request.
  it('leaves coverage null and still resolves when the endpoint rejects', async () => {
    mockGetStateTransitions.mockRejectedValue(new Error('503'));
    const modelIndex = index();
    const { result } = renderHook(() => useEntityDetail(modelIndex, 'root', detail, 0));
    await settle();

    expect(result.current.coverage).toBeNull();
    expect(result.current.stateChanges).toEqual([]);
    expect(result.current.loading).toBe(false);
    expect(result.current.relations[0].edges[0].thingId).toBe('child');
  });
});

describe('useEntityDetail dispatches', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useModelStore.getState().clear();
    mockGetStateTransitions.mockRejectedValue(new Error('503'));
    vi.useFakeTimers();
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  async function settle() {
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });
  }

  it('reads each dispatched edge back from the platform in the same round as the history', async () => {
    mockGetRelationship.mockResolvedValue({
      Id: 'r1', SubjectId: 'root', PredicateId: 'has', TargetId: 'child',
      Properties: { __DispatchState: 'Done', __DispatchLastAttemptAt: '2026-07-17T00:30:00Z' },
    });
    const modelIndex = indexWithAService();
    const { result } = renderHook(() => useEntityDetail(modelIndex, 'root', { relations: [] }, 0));
    await settle();

    expect(mockGetRelationship.mock.calls.map((c) => c[0])).toEqual(['r1']);
    expect(mockGetRelationship.mock.calls[0][1]).toBe(mockGetStateTransitions.mock.calls[0][1]);
    expect(result.current.dispatches).toEqual([
      expect.objectContaining({ relationshipId: 'r1', serviceName: 'keeper service', state: 'Done', at: '2026-07-17T00:30:00Z' }),
    ]);
  });

  it('reads nothing back where no edge on the Thing is a dispatch', async () => {
    const modelIndex = index();
    renderHook(() => useEntityDetail(modelIndex, 'root', { relations: [] }, 0));
    await settle();

    expect(mockGetRelationship).not.toHaveBeenCalled();
  });

  it('still lists the dispatch, undated, when the platform refuses the edge read', async () => {
    mockGetRelationship.mockRejectedValue(new Error('404'));
    const modelIndex = indexWithAService();
    const { result } = renderHook(() => useEntityDetail(modelIndex, 'root', { relations: [] }, 0));
    await settle();

    expect(result.current.loading).toBe(false);
    expect(result.current.dispatches).toEqual([expect.objectContaining({ serviceName: 'keeper service', at: undefined })]);
  });
});
