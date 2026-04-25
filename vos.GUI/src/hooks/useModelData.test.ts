import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';

const mockGetAllThings = vi.fn();
const mockGetAllRels = vi.fn();
vi.mock('../api/thingApi', () => ({
  thingApi: { getAll: () => mockGetAllThings() },
}));
vi.mock('../api/relationshipApi', () => ({
  relationshipApi: { getAll: () => mockGetAllRels() },
}));

// Capture the SignalR handler registry so tests can fire events synthetically.
type Handler = (...args: unknown[]) => void;
const handlers = new Map<string, Handler>();
vi.mock('./useSignalR', () => ({
  useSignalR: () => ({
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
    mockGetAllThings.mockResolvedValue([]);
    mockGetAllRels.mockResolvedValue([]);
    useModelStore.setState({ things: [], relationships: [] });
    useUiStore.setState({ selectedNodeId: null, selectedEdgeId: null, statesVersion: 0 });
  });

  it('loads things + relationships into the model store on mount', async () => {
    mockGetAllThings.mockResolvedValue([{ Id: 't1', Name: 'A' }, { Id: 't2', Name: 'B' }]);
    mockGetAllRels.mockResolvedValue([{ Id: 'r1', Name: 'is', SubjectId: 't1', PredicateId: 'p', TargetId: 't2' }]);

    renderHook(() => useModelData());

    await waitFor(() => expect(useModelStore.getState().things).toHaveLength(2));
    expect(useModelStore.getState().relationships).toHaveLength(1);
  });

  it('reloads from the broker when ThingCreated fires', async () => {
    mockGetAllThings.mockResolvedValue([]);
    mockGetAllRels.mockResolvedValue([]);
    renderHook(() => useModelData());
    await waitFor(() => expect(mockGetAllThings).toHaveBeenCalledTimes(1));

    mockGetAllThings.mockResolvedValue([{ Id: 't-new', Name: 'New' }]);
    await act(async () => handlers.get('ThingCreated')!('t-new'));
    await waitFor(() => expect(useModelStore.getState().things).toHaveLength(1));
  });

  it('clears the store when ModelCleared fires', async () => {
    mockGetAllThings.mockResolvedValue([{ Id: 't1', Name: 'A' }]);
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
    mockGetAllThings.mockResolvedValue([{ Id: 't1', Name: 'A' }]);
    await reloadModelData();
    expect(useModelStore.getState().things).toHaveLength(1);
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
