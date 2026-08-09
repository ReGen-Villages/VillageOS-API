import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';

const mockGetAllThings = vi.fn();
const mockGetAllRels = vi.fn();
const mockGetThing = vi.fn();
const mockGetRel = vi.fn();
const mockGetThingByName = vi.fn();
vi.mock('../api/thingApi', () => ({
  thingApi: {
    getAll: (properties?: readonly string[]) => mockGetAllThings(properties),
    get: (id: string) => mockGetThing(id),
    getByName: (name: string) => mockGetThingByName(name),
  },
}));
vi.mock('../api/relationshipApi', () => ({
  relationshipApi: { getAll: () => mockGetAllRels(), get: (id: string) => mockGetRel(id) },
}));

// Capture the SSE handler registry so tests can fire events synthetically.
// `mockConnected` is read at call-time so a test can flip it and rerender() to
// simulate the stream dropping and recovering.
type Handler = (...args: unknown[]) => void;
const handlers = new Map<string, Handler>();
let mockConnected = false;
vi.mock('./useSse', () => ({
  useSse: () => ({
    connected: mockConnected,
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
vi.mock('../components/common/toastStore', () => ({
  toast: { error: vi.fn(), success: vi.fn(), info: vi.fn() },
}));

import { useModelData, reloadModelData } from './useModelData';
import { useModelStore } from '../stores/modelStore';
import { useUiStore } from '../stores/uiStore';
import { toast } from '../components/common/toastStore';

describe('useModelData', () => {
  beforeEach(() => {
    handlers.clear();
    mockGetAllThings.mockReset();
    mockGetAllRels.mockReset();
    mockGetThing.mockReset();
    mockGetRel.mockReset();
    mockGetThingByName.mockReset();
    mockGetAllThings.mockResolvedValue([]);
    mockGetAllRels.mockResolvedValue([]);
    mockGetThingByName.mockResolvedValue(null);
    mockConnected = false;
    vi.mocked(toast.error).mockClear();
    useModelStore.setState({ things: [], relationships: [], loaded: false });
    useUiStore.setState({ selectedNodeId: null, selectedEdgeId: null, statesVersion: 0 });
  });

  // The model says which properties travel with its load, and the load has to ask for them.
  it('loads only the properties the model declares', async () => {
    mockGetThingByName.mockResolvedValue({
      Id: 'settings-1',
      Name: 'GUI_Settings',
      Properties: { ModelLoadProperties: 'ifcClass,ifcGlobalId' },
    });

    renderHook(() => useModelData());

    await waitFor(() => expect(mockGetAllThings).toHaveBeenCalled());
    expect(mockGetAllThings).toHaveBeenCalledWith(['ifcClass', 'ifcGlobalId']);
  });

  it('loads every property when the model declares none', async () => {
    mockGetThingByName.mockResolvedValue(null);

    renderHook(() => useModelData());

    await waitFor(() => expect(mockGetAllThings).toHaveBeenCalled());
    expect(mockGetAllThings).toHaveBeenCalledWith([]);
  });

  // Narrowing on a guess would strip properties a page needs; loading everything is only slower.
  it('loads every property when the settings cannot be read', async () => {
    mockGetThingByName.mockRejectedValue(new Error('unreachable'));

    renderHook(() => useModelData());

    await waitFor(() => expect(mockGetAllThings).toHaveBeenCalled());
    expect(mockGetAllThings).toHaveBeenCalledWith([]);
    expect(useModelStore.getState().loaded).toBe(true);
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

    await waitFor(() => expect(useModelStore.getState().things.filter((t) => t.Id === 't-new')).toHaveLength(1));
    expect(mockGetThing).toHaveBeenCalledTimes(1); // deduped within the flush window
  });

  it('coalesces a burst of structural events into a single batched store write', async () => {
    renderHook(() => useModelData());
    await waitFor(() => expect(mockGetAllThings).toHaveBeenCalled());

    mockGetThing.mockImplementation((id: string) => Promise.resolve({ Id: id, Name: id, Properties: {} }));
    let writes = 0;
    const unsub = useModelStore.subscribe(() => { writes++; });

    await act(async () => {
      handlers.get('ThingCreated')!({ EntityId: 'a' });
      handlers.get('ThingCreated')!({ EntityId: 'b' });
      handlers.get('ThingCreated')!({ EntityId: 'c' });
    });

    await waitFor(() =>
      expect(useModelStore.getState().things.map((t) => t.Id).sort()).toEqual(['a', 'b', 'c']),
    );
    unsub();
    expect(writes).toBe(1); // one applyBatch for the whole burst, not one write per event
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

    await waitFor(() => expect(useModelStore.getState().things.map((t) => t.Id)).toEqual(['t2']));
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

    await waitFor(() => expect(useModelStore.getState().relationships).toHaveLength(0));
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

  // Regression (Bug #5940): reconcile once when the SSE stream RECOVERS so events
  // missed while disconnected are recovered — without blind polling.
  it('reconciles the model silently when the SSE stream reconnects', async () => {
    mockConnected = true; // already connected at mount
    const { rerender } = renderHook(() => useModelData());
    await act(async () => {});
    expect(mockGetAllThings).toHaveBeenCalledTimes(1); // mount load only; first connect does not reconcile

    // A Thing created while we were disconnected shows up in the next full payload.
    mockGetAllThings.mockResolvedValue([{ Id: 't-late', Name: 'Late', Properties: {} }]);
    mockConnected = false; rerender(); // stream drops
    await act(async () => { mockConnected = true; rerender(); }); // stream recovers

    expect(mockGetAllThings).toHaveBeenCalledTimes(2);
    expect(useModelStore.getState().things.map((t) => t.Id)).toContain('t-late');
    // Background reconcile must not raise a toast (error toasts do not auto-dismiss).
    expect(toast.error).not.toHaveBeenCalled();
  });

  it('does NOT reconcile on the first connect (mount already loaded)', async () => {
    mockConnected = false;
    const { rerender } = renderHook(() => useModelData());
    await act(async () => {});
    expect(mockGetAllThings).toHaveBeenCalledTimes(1); // mount only

    await act(async () => { mockConnected = true; rerender(); }); // first-ever connect
    expect(mockGetAllThings).toHaveBeenCalledTimes(1); // no extra reconcile
  });

  it('a failed reconnect reconcile does not raise a toast', async () => {
    mockConnected = true;
    const { rerender } = renderHook(() => useModelData());
    await act(async () => {});
    mockGetAllThings.mockRejectedValue(new Error('network'));

    mockConnected = false; rerender();
    await act(async () => { mockConnected = true; rerender(); });

    expect(toast.error).not.toHaveBeenCalled();
  });

  it('retries a failed Thing hydrate once, then upserts (Bug #5940)', async () => {
    vi.useFakeTimers();
    try {
      renderHook(() => useModelData());
      await act(async () => {});
      mockGetThing.mockReset();
      mockGetThing
        .mockRejectedValueOnce(new Error('transient'))
        .mockResolvedValueOnce({ Id: 't-new', Name: 'New', Properties: {} });

      await act(async () => {
        handlers.get('ThingCreated')!({ EntityId: 't-new' });
        // Flush debounce (150) + hydrate retry delay (400) must both elapse.
        await vi.advanceTimersByTimeAsync(700);
      });

      expect(mockGetThing).toHaveBeenCalledTimes(2);
      expect(useModelStore.getState().things.map((t) => t.Id)).toContain('t-new');
    } finally {
      vi.useRealTimers();
    }
  });

  it('PropertyChanged updates the things array in place without a full reload', async () => {
    mockGetAllThings.mockResolvedValue([{ Id: 't1', Name: 'A', Properties: { geometry: 'old' } }]);
    renderHook(() => useModelData());
    await waitFor(() => expect(useModelStore.getState().things).toHaveLength(1));

    // Reset reload mock so we can assert the in-place update did NOT trigger a full reload.
    mockGetAllThings.mockClear();

    await act(async () => handlers.get('PropertyChanged')!('t1', 'geometry', 'new'));
    await waitFor(() => expect(useModelStore.getState().things[0].Properties?.geometry).toBe('new'));
    expect(mockGetAllThings).not.toHaveBeenCalled();
  });

  // The Operations dashboard reads live business properties from the store, so a change to a
  // non-graph property must land there too — not only `geometry`.
  it('PropertyChanged on a business property updates the store', async () => {
    mockGetAllThings.mockResolvedValue([{ Id: 't1', Name: 'A', Properties: { contained_units: 18 } }]);
    renderHook(() => useModelData());
    await waitFor(() => expect(useModelStore.getState().things).toHaveLength(1));

    await act(async () => handlers.get('PropertyChanged')!('t1', 'contained_units', 3));
    await waitFor(() => expect(useModelStore.getState().things[0].Properties?.contained_units).toBe(3));
  });

  // A thing can change several properties inside one debounce window; the buffer must keep
  // them all, not collapse to the last one written.
  it('coalesces multiple property changes on the same thing in one window', async () => {
    mockGetAllThings.mockResolvedValue([{ Id: 't1', Name: 'A', Properties: { contained_units: 18, available_units: 18 } }]);
    renderHook(() => useModelData());
    await waitFor(() => expect(useModelStore.getState().things).toHaveLength(1));

    await act(async () => {
      handlers.get('PropertyChanged')!('t1', 'contained_units', 3);
      handlers.get('PropertyChanged')!('t1', 'available_units', 2);
    });
    await waitFor(() => {
      const props = useModelStore.getState().things[0].Properties;
      expect(props?.contained_units).toBe(3);
      expect(props?.available_units).toBe(2);
    });
  });

  // Bug #6143 — the panels no longer reload the whole model after a property write, so every case
  // the reload used to cover has to arrive on the stream instead.
  describe('what the removed full reload used to cover', () => {
    const relationship = (Properties: Record<string, unknown>) => ({
      Id: 'r1', Name: 'holds', SubjectId: 't1', PredicateId: 'p1', TargetId: 't2', Properties,
    });

    it('adding a property to a Thing puts it in the store, not only changing one', async () => {
      mockGetAllThings.mockResolvedValue([{ Id: 't1', Name: 'A', Properties: {} }]);
      renderHook(() => useModelData());
      await waitFor(() => expect(useModelStore.getState().things).toHaveLength(1));

      await act(async () => handlers.get('PropertyChanged')!('t1', 'door_number', '4711'));
      await waitFor(() => expect(useModelStore.getState().things[0].Properties?.door_number).toBe('4711'));
    });

    it('deleting a property on a Thing takes it out of the store', async () => {
      mockGetAllThings.mockResolvedValue([{ Id: 't1', Name: 'A', Properties: { door_number: '4711' } }]);
      renderHook(() => useModelData());
      await waitFor(() => expect(useModelStore.getState().things).toHaveLength(1));

      await act(async () => handlers.get('PropertyDeleted')!('t1', 'door_number'));
      await waitFor(() =>
        expect('door_number' in (useModelStore.getState().things[0].Properties ?? {})).toBe(false),
      );
    });

    it('a change to the edge the user opened lands, with no node selected', async () => {
      mockGetAllRels.mockResolvedValue([relationship({ quantity: 5 })]);
      useUiStore.setState({ selectedNodeId: null, selectedEdgeId: 'r1' });
      renderHook(() => useModelData());
      await waitFor(() => expect(useModelStore.getState().relationships).toHaveLength(1));

      await act(async () => handlers.get('RelationshipPropertyChanged')!('r1', 'quantity', 9));
      await waitFor(() => expect(useModelStore.getState().relationships[0].Properties?.quantity).toBe(9));
    });

    it('keeps every relationship property changed in one window, not only the last', async () => {
      mockGetAllRels.mockResolvedValue([relationship({ quantity: 5, unit: 'crates' })]);
      useUiStore.setState({ selectedNodeId: null, selectedEdgeId: 'r1' });
      renderHook(() => useModelData());
      await waitFor(() => expect(useModelStore.getState().relationships).toHaveLength(1));

      await act(async () => {
        handlers.get('RelationshipPropertyChanged')!('r1', 'quantity', 9);
        handlers.get('RelationshipPropertyChanged')!('r1', 'unit', 'pallets');
      });
      await waitFor(() => {
        const properties = useModelStore.getState().relationships[0].Properties;
        expect(properties?.quantity).toBe(9);
        expect(properties?.unit).toBe('pallets');
      });
    });

    // Bug #6149 — a retraction used to arrive as a change to null, which is also what setting a
    // property to null looks like, so a property another user deleted stayed on screen as an empty
    // row. It now says so, and the two are handled apart.
    it('takes a deleted relationship property out of the store', async () => {
      mockGetAllRels.mockResolvedValue([relationship({ quantity: 5 })]);
      useUiStore.setState({ selectedNodeId: null, selectedEdgeId: 'r1' });
      renderHook(() => useModelData());
      await waitFor(() => expect(useModelStore.getState().relationships).toHaveLength(1));

      await act(async () => handlers.get('RelationshipPropertyDeleted')!('r1', 'quantity'));
      await waitFor(() =>
        expect('quantity' in (useModelStore.getState().relationships[0].Properties ?? {})).toBe(false),
      );
    });

    it('keeps a relationship property that was genuinely set to null', async () => {
      mockGetAllRels.mockResolvedValue([relationship({ quantity: 5 })]);
      useUiStore.setState({ selectedNodeId: null, selectedEdgeId: 'r1' });
      renderHook(() => useModelData());
      await waitFor(() => expect(useModelStore.getState().relationships).toHaveLength(1));

      await act(async () => handlers.get('RelationshipPropertyChanged')!('r1', 'quantity', null));
      await waitFor(() => {
        const properties = useModelStore.getState().relationships[0].Properties ?? {};
        expect('quantity' in properties).toBe(true);
        expect(properties.quantity).toBeNull();
      });
    });

    it('ignores a deletion on a relationship that is neither open nor on the open node', async () => {
      mockGetAllRels.mockResolvedValue([relationship({ quantity: 5 })]);
      useUiStore.setState({ selectedNodeId: null, selectedEdgeId: null });
      renderHook(() => useModelData());
      await waitFor(() => expect(useModelStore.getState().relationships).toHaveLength(1));

      await act(async () => handlers.get('RelationshipPropertyDeleted')!('r1', 'quantity'));
      expect(useModelStore.getState().relationships[0].Properties?.quantity).toBe(5);
    });

    it('ignores a change to a relationship that is neither open nor on the open node', async () => {
      mockGetAllRels.mockResolvedValue([relationship({ quantity: 5 })]);
      useUiStore.setState({ selectedNodeId: null, selectedEdgeId: null });
      renderHook(() => useModelData());
      await waitFor(() => expect(useModelStore.getState().relationships).toHaveLength(1));

      await act(async () => handlers.get('RelationshipPropertyChanged')!('r1', 'quantity', 9));
      expect(useModelStore.getState().relationships[0].Properties?.quantity).toBe(5);
    });
  });
});
