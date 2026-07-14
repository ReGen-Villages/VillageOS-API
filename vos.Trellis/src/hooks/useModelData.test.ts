import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';

const mockGetAllThings = vi.fn();
const mockGetAllRels = vi.fn();
const mockGetThing = vi.fn();
const mockGetRel = vi.fn();
vi.mock('../api/thingApi', () => ({
  thingApi: { getAll: () => mockGetAllThings(), get: (id: string) => mockGetThing(id) },
}));
vi.mock('../api/relationshipApi', () => ({
  relationshipApi: { getAll: () => mockGetAllRels(), get: (id: string) => mockGetRel(id) },
}));

// Capture the SSE handler registry so tests can fire events synthetically.
type Handler = (...args: unknown[]) => void;
const handlers = new Map<string, Handler>();
vi.mock('./useSse', () => ({
  useSse: () => ({
    on: (event: string, cb: Handler) => {
      handlers.set(event, cb);
      return () => handlers.delete(event);
    },
  }),
}));

// useFlashTimer touches uiStore but we don't need real timers for these tests.
vi.mock('./useFlashTimer', () => ({
  useFlashTimer: () => ({ triggerFlashNode: vi.fn(), triggerFlashEdge: vi.fn() }),
}));

// Toast is fire-and-forget; silence it.
vi.mock('../components/common/Toast', () => ({
  toast: { error: vi.fn(), success: vi.fn(), info: vi.fn() },
}));

import { useModelData, reloadModelData } from './useModelData';
import { useModelStore } from '../stores/modelStore';
import { useUiStore } from '../stores/uiStore';

describe('useModelData', () => {
  beforeEach(() => {
    handlers.clear();
    mockGetAllThings.mockReset();
    mockGetAllRels.mockReset();
    mockGetThing.mockReset();
    mockGetRel.mockReset();
    mockGetAllThings.mockResolvedValue([]);
    mockGetAllRels.mockResolvedValue([]);
    useModelStore.setState({ things: [], relationships: [], loaded: false });
    useUiStore.setState({ selectedNodeId: null, selectedEdgeId: null, statesVersion: 0 });
  });

  it('loads things + relationships into the model store on mount', async () => {
    mockGetAllThings.mockResolvedValue([{ Id: 't1', Name: 'A', Properties: {} }, { Id: 't2', Name: 'B', Properties: {} }]);
    mockGetAllRels.mockResolvedValue([{ Id: 'r1', Name: 'is', SubjectId: 't1', PredicateId: 'p', TargetId: 't2' }]);

    renderHook(() => useModelData());

    await waitFor(() => expect(useModelStore.getState().things).toHaveLength(2));
    expect(useModelStore.getState().relationships).toHaveLength(1);
  });

  it('ThingCreated hydrates the single new thing and upserts it without a full refetch', async () => {
    renderHook(() => useModelData());
    await waitFor(() => expect(mockGetAllThings).toHaveBeenCalledTimes(1));
    mockGetAllThings.mockClear();

    mockGetThing.mockResolvedValue({ Id: 't-new', Name: 'New' });
    await act(async () => { handlers.get('ThingCreated')!({ EntityId: 't-new' }); });

    await waitFor(() => expect(useModelStore.getState().things).toHaveLength(1));
    expect(mockGetThing).toHaveBeenCalledWith('t-new');
    expect(mockGetAllThings).not.toHaveBeenCalled();
  });

  it('ThingCreated is idempotent — a duplicate event does not double-add', async () => {
    renderHook(() => useModelData());
    await waitFor(() => expect(mockGetAllThings).toHaveBeenCalled());

    mockGetThing.mockResolvedValue({ Id: 't-new', Name: 'New' });
    await act(async () => { handlers.get('ThingCreated')!({ EntityId: 't-new' }); });
    await act(async () => { handlers.get('ThingCreated')!({ EntityId: 't-new' }); });

    expect(useModelStore.getState().things.filter((t) => t.Id === 't-new')).toHaveLength(1);
  });

  it('ThingCreated with a failed hydrate does not throw or change the store', async () => {
    useModelStore.setState({ things: [{ Id: 't1', Name: 'A', Properties: {} }], relationships: [] });
    renderHook(() => useModelData());
    await waitFor(() => expect(mockGetAllThings).toHaveBeenCalled());
    useModelStore.setState({ things: [{ Id: 't1', Name: 'A', Properties: {} }], relationships: [] });

    mockGetThing.mockRejectedValue(new Error('404'));
    await act(async () => { handlers.get('ThingCreated')!({ EntityId: 'gone' }); });

    expect(useModelStore.getState().things).toHaveLength(1);
  });

  it('RelationshipCreated hydrates the single new relationship and upserts it', async () => {
    renderHook(() => useModelData());
    await waitFor(() => expect(mockGetAllRels).toHaveBeenCalled());
    mockGetAllRels.mockClear();

    mockGetRel.mockResolvedValue({ Id: 'r-new', Name: 'is', SubjectId: 't1', PredicateId: 'p', TargetId: 't2' });
    await act(async () => { handlers.get('RelationshipCreated')!({ EntityId: 'r-new' }); });

    await waitFor(() => expect(useModelStore.getState().relationships).toHaveLength(1));
    expect(mockGetRel).toHaveBeenCalledWith('r-new');
    expect(mockGetAllRels).not.toHaveBeenCalled();
  });

  it('ThingDeleted removes the thing locally without a full refetch', async () => {
    useModelStore.setState({ things: [{ Id: 't1', Name: 'A', Properties: {} }, { Id: 't2', Name: 'B', Properties: {} }], relationships: [] });
    renderHook(() => useModelData());
    await waitFor(() => expect(mockGetAllThings).toHaveBeenCalled());
    useModelStore.setState({ things: [{ Id: 't1', Name: 'A', Properties: {} }, { Id: 't2', Name: 'B', Properties: {} }], relationships: [] });
    mockGetAllThings.mockClear();

    await act(async () => { handlers.get('ThingDeleted')!({ EntityId: 't1' }); });

    expect(useModelStore.getState().things.map((t) => t.Id)).toEqual(['t2']);
    expect(mockGetAllThings).not.toHaveBeenCalled();
  });

  it('RelationshipDeleted removes the relationship locally without a full refetch', async () => {
    const rel = { Id: 'r1', Name: 'is', SubjectId: 't1', PredicateId: 'p', TargetId: 't2', Properties: {} };
    useModelStore.setState({ things: [], relationships: [rel] });
    renderHook(() => useModelData());
    await waitFor(() => expect(mockGetAllRels).toHaveBeenCalled());
    useModelStore.setState({ things: [], relationships: [rel] });
    mockGetAllRels.mockClear();

    await act(async () => { handlers.get('RelationshipDeleted')!({ EntityId: 'r1' }); });

    expect(useModelStore.getState().relationships).toHaveLength(0);
    expect(mockGetAllRels).not.toHaveBeenCalled();
  });

  it('ThingDeleted for an unknown id is a no-op', async () => {
    useModelStore.setState({ things: [{ Id: 't1', Name: 'A', Properties: {} }], relationships: [] });
    renderHook(() => useModelData());
    await waitFor(() => expect(mockGetAllThings).toHaveBeenCalled());
    useModelStore.setState({ things: [{ Id: 't1', Name: 'A', Properties: {} }], relationships: [] });

    await act(async () => { handlers.get('ThingDeleted')!({ EntityId: 'nope' }); });

    expect(useModelStore.getState().things).toHaveLength(1);
  });

  it('clears the store when ModelCleared fires', async () => {
    mockGetAllThings.mockResolvedValue([{ Id: 't1', Name: 'A', Properties: {} }]);
    renderHook(() => useModelData());
    await waitFor(() => expect(useModelStore.getState().things).toHaveLength(1));

    await act(async () => handlers.get('ModelCleared')!());
    expect(useModelStore.getState().things).toHaveLength(0);
  });

  it('bumps uiStore.statesVersion when StatesChanged fires', async () => {
    renderHook(() => useModelData());
    await waitFor(() => expect(mockGetAllThings).toHaveBeenCalled());
    const before = useUiStore.getState().statesVersion;
    await act(async () => handlers.get('StatesChanged')!());
    expect(useUiStore.getState().statesVersion).toBe(before + 1);
  });

  it('reloadModelData is callable directly (from mutation handlers)', async () => {
    mockGetAllThings.mockResolvedValue([{ Id: 't1', Name: 'A', Properties: {} }]);
    await reloadModelData();
    expect(useModelStore.getState().things).toHaveLength(1);
  });

  // Regression (Bug #5930): OperationsPage blocks rendering on the store's
  // `loaded` flag. reloadModelData must flip it, or the page hangs on
  // "Loading model…" forever even though the data arrived.
  it('marks the store loaded after a successful fetch', async () => {
    expect(useModelStore.getState().loaded).toBe(false);
    await reloadModelData();
    expect(useModelStore.getState().loaded).toBe(true);
  });

  it('leaves the store unloaded when the fetch fails', async () => {
    mockGetAllThings.mockRejectedValue(new Error('network'));
    await reloadModelData();
    expect(useModelStore.getState().loaded).toBe(false);
  });

  it('PropertyChanged on a graph-affecting property updates the things array in place', async () => {
    mockGetAllThings.mockResolvedValue([{ Id: 't1', Name: 'A', Properties: { geometry: 'old' } }]);
    renderHook(() => useModelData());
    await waitFor(() => expect(useModelStore.getState().things).toHaveLength(1));

    // Reset reload mock so we can assert the in-place update did NOT trigger a full reload.
    mockGetAllThings.mockClear();

    await act(async () => handlers.get('PropertyChanged')!('t1', 'geometry', 'new'));
    expect(useModelStore.getState().things[0].Properties?.geometry).toBe('new');
    expect(mockGetAllThings).not.toHaveBeenCalled();
  });
});
