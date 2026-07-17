import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';

const mockGetThingStates = vi.fn();
const mockGetThingMutations = vi.fn();
const mockGetRelationshipMutations = vi.fn();
const mockGetPropertyFacts = vi.fn();

vi.mock('../../../api/stateApi', () => ({
  stateApi: { getThingStates: (id: string, signal?: AbortSignal) => mockGetThingStates(id, signal) },
}));
vi.mock('../../../api/temporalApi', () => ({
  temporalApi: {
    getThingMutations: (id: string, _start?: string, _end?: string, signal?: AbortSignal) =>
      mockGetThingMutations(id, signal),
    getRelationshipMutations: (id: string, _start?: string, _end?: string, signal?: AbortSignal) =>
      mockGetRelationshipMutations(id, signal),
    getPropertyFacts: (id: string, property: string, signal?: AbortSignal) =>
      mockGetPropertyFacts(id, property, signal),
  },
}));

import { buildModelIndex } from '../../../api/dashboardApi';
import type { VosThing, VosRelationship } from '../../../types/vos';
import { useEntityDetail } from './useEntityDetail';

function thing(Id: string, Name: string): VosThing {
  return { Id, Name, Properties: {} };
}
function rel(Id: string, SubjectId: string, PredicateId: string, TargetId: string): VosRelationship {
  return { Id, Name: Id, SubjectId, PredicateId, TargetId, Properties: {} };
}

// root -has-> child. One involved Thing, so each round is a small, countable fan-out.
function index() {
  return buildModelIndex(
    [thing('root', 'ROOT-1'), thing('child', 'CHILD-1'), thing('has', 'has')],
    [rel('r1', 'root', 'has', 'child')],
  );
}

describe('useEntityDetail', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGetThingStates.mockResolvedValue({ CurrentStates: [] });
    mockGetThingMutations.mockResolvedValue({ ObjectId: 'root', ObjectName: 'ROOT-1', Mutations: [] });
    mockGetRelationshipMutations.mockResolvedValue({ Mutations: [] });
    mockGetPropertyFacts.mockResolvedValue({ entries: [] });
    vi.useFakeTimers();
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  const detail = { involves: { predicates: ['has'], depth: 1 } };

  async function settle() {
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });
  }

  // The fixture has 2 Things (root + child), so each round is 2 state calls.
  const rounds = () => mockGetThingStates.mock.calls.length / 2;

  // The dashboard bumps `nonce` every 400 ms while a sim runs. A round costs a request per
  // involved Thing plus its Fact lookups, so following every bump would put one open window
  // into the hundreds of requests per second (#5961). The throttle is leading-edge — the
  // first bump after a quiet period refreshes promptly — so what must not happen is a round
  // per bump, not a round at all.
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
    const afterFirstRound = mockGetThingStates.mock.calls.length;

    for (let n = 1; n <= 30; n++) {
      rerender({ nonce: n });
      await act(async () => {
        await vi.advanceTimersByTimeAsync(400);
      });
    }

    expect(mockGetThingStates.mock.calls.length).toBeGreaterThan(afterFirstRound);
  });

  it('fetches the root and its involved Things, and abandons a superseded round', async () => {
    const idx = index();
    const { rerender, unmount } = renderHook(({ nonce }) => useEntityDetail(idx, 'root', detail, nonce), {
      initialProps: { nonce: 0 },
    });
    await settle();

    expect(mockGetThingStates.mock.calls.map((c) => c[0]).sort()).toEqual(['child', 'root']);
    const signal: AbortSignal = mockGetThingStates.mock.calls[0][1];
    expect(signal.aborted).toBe(false);

    rerender({ nonce: 1 });
    unmount();
    expect(signal.aborted).toBe(true);
  });
});
