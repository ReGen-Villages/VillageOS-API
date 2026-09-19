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

/** A Thing as the stream carries it: the snapshot's shape, properties in their typed wrappers. */
const wireThing = (Id: string, Name: string, properties: Record<string, unknown> = {}) => ({
  Id, Name, IsArchetype: false,
  Properties: Object.fromEntries(Object.entries(properties).map(([name, value]) => [name, { typeInfo: 'vos.Double', value }])),
  RollupProperties: null, InheritedOverrides: null, States: [],
});
const wireEdge = (Id: string, SubjectId: string, PredicateId: string, TargetId: string) => ({
  Id, Name: null, SubjectId, PredicateId, TargetId, Properties: {}, InheritedOverrides: null, States: [],
});

// Capture the SSE handler registry so tests can fire events synthetically.
type Handler = (...args: unknown[]) => void;
const handlers = new Map<string, Handler>();
const mockResubscribe = vi.fn();
vi.mock('./useSse', () => ({
  SUBSCRIPTION_OPENED: 'SubscriptionOpened',
  resubscribe: () => mockResubscribe(),
  useDefaultSubscription: () => {},
  useSse: () => ({
    connected: true,
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
import type { SubscriptionOpened } from '../types/subscription';
import { useModelStore } from '../stores/modelStore';
import { useUiStore } from '../stores/uiStore';
import { toast } from '../components/common/toastStore';

/** A subscription opening. Every load starts from one of these now that what a page holds follows
 *  what it subscribed to, so a test that wants a loaded store announces one. */
function subscriptionOpened(over: Partial<SubscriptionOpened> = {}) {
  handlers.get('SubscriptionOpened')!({
    subscriptionId: 's1', watermark: 0, covered: null, ...over,
  });
}

/** Mount the hook the way the shell does and let the whole-model subscription answer. */
async function mountLoaded() {
  const rendered = renderHook(() => useModelData());
  await act(async () => { subscriptionOpened(); });
  return rendered;
}

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
    mockResubscribe.mockClear();
    vi.mocked(toast.error).mockClear();
    useModelStore.setState({ things: [], relationships: [], loaded: false });
    useUiStore.setState({ selectedNodeId: null, selectedEdgeId: null, statesVersion: 0, stateVersions: {} });
  });

  // The model says which properties travel with its load, and the load has to ask for them.
  it('loads only the properties the model declares', async () => {
    mockGetThingByName.mockResolvedValue({
      Id: 'settings-1',
      Name: 'GUI_Settings',
      Properties: { ModelLoadProperties: 'ifcClass,ifcGlobalId' },
    });

    await mountLoaded();

    await waitFor(() => expect(mockGetAllThings).toHaveBeenCalled());
    expect(mockGetAllThings).toHaveBeenCalledWith(['ifcClass', 'ifcGlobalId', 'spec']);
  });

  // The navigation lists a model's dashboards on every page and reads each one out of `spec`. A
  // model narrowing its load without naming it is describing what its pages draw, not asking for a
  // navigation with nothing in it.
  it('loads the property a dashboard is written in even when the model does not name it', async () => {
    mockGetThingByName.mockResolvedValue({
      Id: 'settings-1',
      Name: 'GUI_Settings',
      Properties: { ModelLoadProperties: 'ifcClass' },
    });

    await mountLoaded();

    await waitFor(() => expect(mockGetAllThings).toHaveBeenCalledWith(['ifcClass', 'spec']));
  });

  it('loads every property when the model declares none', async () => {
    mockGetThingByName.mockResolvedValue(null);

    await mountLoaded();

    await waitFor(() => expect(mockGetAllThings).toHaveBeenCalled());
    expect(mockGetAllThings).toHaveBeenCalledWith([]);
  });

  // Narrowing on a guess would strip properties a page needs; loading everything is only slower.
  it('loads every property when the settings cannot be read', async () => {
    mockGetThingByName.mockRejectedValue(new Error('unreachable'));

    await mountLoaded();

    await waitFor(() => expect(mockGetAllThings).toHaveBeenCalled());
    expect(mockGetAllThings).toHaveBeenCalledWith([]);
    expect(useModelStore.getState().loaded).toBe(true);
  });

  it('loads things + relationships into the model store on mount', async () => {
    mockGetAllThings.mockResolvedValue([{ Id: 't1', Name: 'A', Properties: {} }, { Id: 't2', Name: 'B', Properties: {} }]);
    mockGetAllRels.mockResolvedValue([{ Id: 'r1', Name: 'is', SubjectId: 't1', PredicateId: 'p', TargetId: 't2' }]);

    await mountLoaded();

    await waitFor(() => expect(useModelStore.getState().things).toHaveLength(2));
    expect(useModelStore.getState().relationships).toHaveLength(1);
  });

  it('ThingCreated lands the Thing it carries in the store, with its properties, and asks for nothing', async () => {
    await mountLoaded();
    await waitFor(() => expect(mockGetAllThings).toHaveBeenCalledTimes(1));
    mockGetAllThings.mockClear();

    await act(async () => {
      handlers.get('ThingCreated')!({ EntityId: 't-new', Thing: wireThing('t-new', 'New', { volume: 4 }) });
    });

    await waitFor(() => expect(useModelStore.getState().things).toHaveLength(1));
    expect(useModelStore.getState().things[0]).toMatchObject({ Id: 't-new', Name: 'New', Properties: { volume: 4 } });
    expect(mockGetThing).not.toHaveBeenCalled();
    expect(mockGetAllThings).not.toHaveBeenCalled();
  });

  it('ThingCreated is idempotent — a duplicate event does not double-add', async () => {
    await mountLoaded();
    await waitFor(() => expect(mockGetAllThings).toHaveBeenCalled());

    await act(async () => { handlers.get('ThingCreated')!({ EntityId: 't-new', Thing: wireThing('t-new', 'New') }); });
    await act(async () => { handlers.get('ThingCreated')!({ EntityId: 't-new', Thing: wireThing('t-new', 'New') }); });

    await waitFor(() => expect(useModelStore.getState().things.filter((t) => t.Id === 't-new')).toHaveLength(1));
  });

  it('coalesces a burst of structural events into a single batched store write', async () => {
    await mountLoaded();
    await waitFor(() => expect(mockGetAllThings).toHaveBeenCalled());

    let writes = 0;
    const unsub = useModelStore.subscribe(() => { writes++; });

    await act(async () => {
      for (const id of ['a', 'b', 'c']) handlers.get('ThingCreated')!({ EntityId: id, Thing: wireThing(id, id) });
    });

    await waitFor(() =>
      expect(useModelStore.getState().things.map((t) => t.Id).sort()).toEqual(['a', 'b', 'c']),
    );
    unsub();
    expect(writes).toBe(1); // one applyBatch for the whole burst, not one write per event
  });

  it('ThingCreated carrying no body changes nothing and asks for nothing', async () => {
    useModelStore.setState({ things: [{ Id: 't1', Name: 'A', Properties: {} }], relationships: [] });
    await mountLoaded();
    await waitFor(() => expect(mockGetAllThings).toHaveBeenCalled());
    useModelStore.setState({ things: [{ Id: 't1', Name: 'A', Properties: {} }], relationships: [] });

    await act(async () => { handlers.get('ThingCreated')!({ EntityId: 'gone' }); });

    expect(useModelStore.getState().things).toHaveLength(1);
    expect(mockGetThing).not.toHaveBeenCalled();
  });

  it('RelationshipCreated lands the edge it carries with its three ends, and asks for nothing', async () => {
    await mountLoaded();
    await waitFor(() => expect(mockGetAllRels).toHaveBeenCalled());
    mockGetAllRels.mockClear();

    await act(async () => {
      handlers.get('RelationshipCreated')!({ EntityId: 'r-new', Relationship: wireEdge('r-new', 't1', 'p', 't2') });
    });

    await waitFor(() => expect(useModelStore.getState().relationships).toHaveLength(1));
    expect(useModelStore.getState().relationships[0]).toMatchObject({ Id: 'r-new', SubjectId: 't1', PredicateId: 'p', TargetId: 't2' });
    expect(mockGetRel).not.toHaveBeenCalled();
    expect(mockGetAllRels).not.toHaveBeenCalled();
  });

  it('ThingEntered adds the Thing it carries', async () => {
    await mountLoaded();
    await waitFor(() => expect(mockGetAllThings).toHaveBeenCalled());

    await act(async () => {
      handlers.get('ThingEntered')!({ EntityId: 't-typed', Thing: wireThing('t-typed', 'Typed', { area: 9 }) });
    });

    await waitFor(() => expect(useModelStore.getState().things.map((t) => t.Id)).toEqual(['t-typed']));
    expect(useModelStore.getState().things[0].Properties).toEqual({ area: 9 });
    expect(mockGetThing).not.toHaveBeenCalled();
  });

  it('RelationshipEntered adds the edge it carries', async () => {
    await mountLoaded();
    await waitFor(() => expect(mockGetAllRels).toHaveBeenCalled());

    await act(async () => {
      handlers.get('RelationshipEntered')!({ EntityId: 'r-held', Relationship: wireEdge('r-held', 't-typed', 'is', 'arch') });
    });

    await waitFor(() => expect(useModelStore.getState().relationships.map((r) => r.Id)).toEqual(['r-held']));
    expect(mockGetRel).not.toHaveBeenCalled();
  });

  it('ThingLeft removes the Thing without a full refetch', async () => {
    mockGetAllThings.mockResolvedValue([{ Id: 't1', Name: 'A', Properties: {} }, { Id: 't2', Name: 'B', Properties: {} }]);
    await mountLoaded();
    await waitFor(() => expect(useModelStore.getState().things).toHaveLength(2));
    mockGetAllThings.mockClear();

    await act(async () => { handlers.get('ThingLeft')!({ EntityId: 't1' }); });

    await waitFor(() => expect(useModelStore.getState().things.map((t) => t.Id)).toEqual(['t2']));
    expect(mockGetAllThings).not.toHaveBeenCalled();
  });

  it('RelationshipLeft removes the edge without a full refetch', async () => {
    mockGetAllRels.mockResolvedValue([
      { Id: 'r1', Name: 'is', SubjectId: 't1', PredicateId: 'p', TargetId: 't2' },
      { Id: 'r2', Name: 'is', SubjectId: 't2', PredicateId: 'p', TargetId: 't3' },
    ]);
    await mountLoaded();
    await waitFor(() => expect(useModelStore.getState().relationships).toHaveLength(2));
    mockGetAllRels.mockClear();

    await act(async () => { handlers.get('RelationshipLeft')!({ EntityId: 'r1' }); });

    await waitFor(() => expect(useModelStore.getState().relationships.map((r) => r.Id)).toEqual(['r2']));
    expect(mockGetAllRels).not.toHaveBeenCalled();
  });

  it('a Thing that entered and left in one window is not kept', async () => {
    await mountLoaded();
    await waitFor(() => expect(mockGetAllThings).toHaveBeenCalled());

    await act(async () => {
      handlers.get('ThingEntered')!({ EntityId: 't-brief', Thing: wireThing('t-brief', 'Brief') });
      handlers.get('ThingLeft')!({ EntityId: 't-brief' });
    });
    await act(async () => { await new Promise((resolve) => setTimeout(resolve, 200)); });

    expect(useModelStore.getState().things).toHaveLength(0);
  });

  it('ThingDeleted removes the thing locally without a full refetch', async () => {
    useModelStore.setState({ things: [{ Id: 't1', Name: 'A', Properties: {} }, { Id: 't2', Name: 'B', Properties: {} }], relationships: [] });
    await mountLoaded();
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
    await mountLoaded();
    await waitFor(() => expect(mockGetAllRels).toHaveBeenCalled());
    useModelStore.setState({ things: [], relationships: [rel] });
    mockGetAllRels.mockClear();

    await act(async () => { handlers.get('RelationshipDeleted')!({ EntityId: 'r1' }); });

    await waitFor(() => expect(useModelStore.getState().relationships).toHaveLength(0));
    expect(mockGetAllRels).not.toHaveBeenCalled();
  });

  it('ThingDeleted for an unknown id is a no-op', async () => {
    useModelStore.setState({ things: [{ Id: 't1', Name: 'A', Properties: {} }], relationships: [] });
    await mountLoaded();
    await waitFor(() => expect(mockGetAllThings).toHaveBeenCalled());
    useModelStore.setState({ things: [{ Id: 't1', Name: 'A', Properties: {} }], relationships: [] });

    await act(async () => { handlers.get('ThingDeleted')!({ EntityId: 'nope' }); });

    expect(useModelStore.getState().things).toHaveLength(1);
  });

  it('clears the store when ModelCleared fires', async () => {
    mockGetAllThings.mockResolvedValue([{ Id: 't1', Name: 'A', Properties: {} }]);
    await mountLoaded();
    await waitFor(() => expect(useModelStore.getState().things).toHaveLength(1));

    await act(async () => handlers.get('ModelCleared')!());
    expect(useModelStore.getState().things).toHaveLength(0);
  });

  it('seeds the states a narrowed subscription answered with', async () => {
    renderHook(() => useModelData());
    await act(async () => {
      subscriptionOpened({ covered: {
        things: [{ Id: 't1', Name: 'A', Properties: {} }],
        relationships: [],
        thingStates: new Map([['t1', ['flagged']]]),
      } });
    });

    expect(useModelStore.getState().thingStates.get('t1')).toEqual(['flagged']);
  });

  it('lands a StatesChanged for a held Thing on a narrowed page, and nothing for one not held', async () => {
    renderHook(() => useModelData());
    await act(async () => {
      subscriptionOpened({ covered: {
        things: [{ Id: 't1', Name: 'A', Properties: {} }],
        relationships: [],
        thingStates: new Map([['t1', ['flagged']]]),
      } });
    });

    await act(async () => {
      handlers.get('StatesChanged')!({ entityId: 't1', currentStates: ['cleared'] });
      handlers.get('StatesChanged')!({ entityId: 't9', currentStates: ['flagged'] });
    });

    await waitFor(() => expect(useModelStore.getState().thingStates.get('t1')).toEqual(['cleared']));
    expect(useModelStore.getState().thingStates.has('t9')).toBe(false);
  });

  it('keeps no state change on a whole-model page', async () => {
    mockGetAllThings.mockResolvedValue([{ Id: 't1', Name: 'A', Properties: {} }]);
    await mountLoaded();
    await waitFor(() => expect(useModelStore.getState().things).toHaveLength(1));

    await act(async () => { handlers.get('StatesChanged')!({ entityId: 't1', currentStates: ['flagged'] }); });
    await act(async () => { await new Promise((resolve) => setTimeout(resolve, 200)); });

    expect(useModelStore.getState().thingStates.size).toBe(0);
  });

  it('a StatesChanged carrying no state set changes nothing', async () => {
    renderHook(() => useModelData());
    await act(async () => {
      subscriptionOpened({ covered: {
        things: [{ Id: 't1', Name: 'A', Properties: {} }],
        relationships: [],
        thingStates: new Map([['t1', ['flagged']]]),
      } });
    });

    await act(async () => { handlers.get('StatesChanged')!({ entityId: 't1' }); });
    await act(async () => { await new Promise((resolve) => setTimeout(resolve, 200)); });

    expect(useModelStore.getState().thingStates.get('t1')).toEqual(['flagged']);
  });

  it('seeds the states an entering Thing carries', async () => {
    renderHook(() => useModelData());
    await act(async () => {
      subscriptionOpened({ covered: { things: [], relationships: [], thingStates: new Map() } });
    });

    await act(async () => {
      handlers.get('ThingEntered')!({ EntityId: 't-typed', Thing: { ...wireThing('t-typed', 'Typed'), States: ['flagged'] } });
    });

    await waitFor(() => expect(useModelStore.getState().thingStates.get('t-typed')).toEqual(['flagged']));
  });

  it('moves the running states counter and the counters of the states a StatesChanged names', async () => {
    await mountLoaded();
    await waitFor(() => expect(mockGetAllThings).toHaveBeenCalled());
    const before = useUiStore.getState().statesVersion;

    await act(async () => handlers.get('StatesChanged')!({ entityId: 't1', currentStates: ['flagged', 'metered'] }));

    expect(useUiStore.getState().statesVersion).toBe(before + 1);
    expect(useUiStore.getState().stateVersions).toEqual({ flagged: 1, metered: 1 });
  });

  it('moves the running counter alone for a StatesChanged naming no states', async () => {
    await mountLoaded();
    await waitFor(() => expect(mockGetAllThings).toHaveBeenCalled());
    const before = useUiStore.getState().statesVersion;

    await act(async () => handlers.get('StatesChanged')!({ entityId: 't1' }));

    expect(useUiStore.getState().statesVersion).toBe(before + 1);
    expect(useUiStore.getState().stateVersions).toEqual({});
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

  // Regression (Bug #5940): a reopened subscription answers with a fresh snapshot, and the changes
  // missed while the stream was down come back with it — without blind polling.
  it('reads the model again when the subscription reopens, and says nothing about it', async () => {
    await mountLoaded();
    expect(mockGetAllThings).toHaveBeenCalledTimes(1);

    // A Thing created while we were disconnected shows up in the next full payload.
    mockGetAllThings.mockResolvedValue([{ Id: 't-late', Name: 'Late', Properties: {} }]);
    await act(async () => { subscriptionOpened(); });

    expect(mockGetAllThings).toHaveBeenCalledTimes(2);
    expect(useModelStore.getState().things.map((t) => t.Id)).toContain('t-late');
    // A refresh behind an already-drawn page must not raise a toast (they do not auto-dismiss).
    expect(toast.error).not.toHaveBeenCalled();
  });

  // The whole point of a narrowed subscription: what the page is about arrived with it, so nothing
  // reads the model to find it again.
  it('takes what a narrowed subscription covers as the load, without reading the model', async () => {
    renderHook(() => useModelData());
    await act(async () => {
      subscriptionOpened({
        covered: {
          things: [{ Id: 't1', Name: 'Scoped', Properties: {} }],
          relationships: [{ Id: 'r1', SubjectId: 't1', PredicateId: 'p', TargetId: 't2', Properties: {} }],
          thingStates: new Map(),
        },
      });
    });

    expect(mockGetAllThings).not.toHaveBeenCalled();
    expect(useModelStore.getState().things.map((t) => t.Id)).toEqual(['t1']);
    expect(useModelStore.getState().relationships).toHaveLength(1);
    expect(useModelStore.getState().loaded).toBe(true);
  });

  // A page that asked for the whole model must not be shown the narrower page's set as though it
  // were the model while the read is still in flight.
  it('empties a narrowed set before loading the whole model over it', async () => {
    renderHook(() => useModelData());
    await act(async () => {
      subscriptionOpened({ covered: { things: [{ Id: 't1', Name: 'Scoped', Properties: {} }], relationships: [], thingStates: new Map() } });
    });

    let finishLoad: (things: unknown[]) => void = () => {};
    mockGetAllThings.mockReturnValue(new Promise((resolve) => { finishLoad = resolve; }));
    await act(async () => { subscriptionOpened(); });

    expect(useModelStore.getState().things).toEqual([]);
    expect(useModelStore.getState().loaded).toBe(false);
    await act(async () => { finishLoad([{ Id: 't2', Name: 'Everything', Properties: {} }]); });
    expect(useModelStore.getState().things.map((t) => t.Id)).toEqual(['t2']);
  });

  // A subscription covers what it resolved to when it opened, and a replaced model holds none of
  // those Things — so the answer is to ask again, not to reconcile against the model that went.
  it('asks for the subscription again when the model is replaced', async () => {
    await mountLoaded();

    await act(async () => { handlers.get('ModelChanged')!({}); });

    expect(mockResubscribe).toHaveBeenCalledTimes(1);
  });

  it('a failed read behind a drawn page does not raise a toast', async () => {
    await mountLoaded();
    mockGetAllThings.mockRejectedValue(new Error('network'));

    await act(async () => { subscriptionOpened(); });

    expect(toast.error).not.toHaveBeenCalled();
  });

  it('PropertyChanged updates the things array in place without a full reload', async () => {
    mockGetAllThings.mockResolvedValue([{ Id: 't1', Name: 'A', Properties: { geometry: 'old' } }]);
    await mountLoaded();
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
    await mountLoaded();
    await waitFor(() => expect(useModelStore.getState().things).toHaveLength(1));

    await act(async () => handlers.get('PropertyChanged')!('t1', 'contained_units', 3));
    await waitFor(() => expect(useModelStore.getState().things[0].Properties?.contained_units).toBe(3));
  });

  // A thing can change several properties inside one debounce window; the buffer must keep
  // them all, not collapse to the last one written.
  it('coalesces multiple property changes on the same thing in one window', async () => {
    mockGetAllThings.mockResolvedValue([{ Id: 't1', Name: 'A', Properties: { contained_units: 18, available_units: 18 } }]);
    await mountLoaded();
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
      await mountLoaded();
      await waitFor(() => expect(useModelStore.getState().things).toHaveLength(1));

      await act(async () => handlers.get('PropertyChanged')!('t1', 'door_number', '4711'));
      await waitFor(() => expect(useModelStore.getState().things[0].Properties?.door_number).toBe('4711'));
    });

    it('deleting a property on a Thing takes it out of the store', async () => {
      mockGetAllThings.mockResolvedValue([{ Id: 't1', Name: 'A', Properties: { door_number: '4711' } }]);
      await mountLoaded();
      await waitFor(() => expect(useModelStore.getState().things).toHaveLength(1));

      await act(async () => handlers.get('PropertyDeleted')!('t1', 'door_number'));
      await waitFor(() =>
        expect('door_number' in (useModelStore.getState().things[0].Properties ?? {})).toBe(false),
      );
    });

    it('a change to the edge the user opened lands, with no node selected', async () => {
      mockGetAllRels.mockResolvedValue([relationship({ quantity: 5 })]);
      useUiStore.setState({ selectedNodeId: null, selectedEdgeId: 'r1' });
      await mountLoaded();
      await waitFor(() => expect(useModelStore.getState().relationships).toHaveLength(1));

      await act(async () => handlers.get('RelationshipPropertyChanged')!('r1', 'quantity', 9));
      await waitFor(() => expect(useModelStore.getState().relationships[0].Properties?.quantity).toBe(9));
    });

    it('keeps every relationship property changed in one window, not only the last', async () => {
      mockGetAllRels.mockResolvedValue([relationship({ quantity: 5, unit: 'crates' })]);
      useUiStore.setState({ selectedNodeId: null, selectedEdgeId: 'r1' });
      await mountLoaded();
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
      await mountLoaded();
      await waitFor(() => expect(useModelStore.getState().relationships).toHaveLength(1));

      await act(async () => handlers.get('RelationshipPropertyDeleted')!('r1', 'quantity'));
      await waitFor(() =>
        expect('quantity' in (useModelStore.getState().relationships[0].Properties ?? {})).toBe(false),
      );
    });

    it('keeps a relationship property that was genuinely set to null', async () => {
      mockGetAllRels.mockResolvedValue([relationship({ quantity: 5 })]);
      useUiStore.setState({ selectedNodeId: null, selectedEdgeId: 'r1' });
      await mountLoaded();
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
      await mountLoaded();
      await waitFor(() => expect(useModelStore.getState().relationships).toHaveLength(1));

      await act(async () => handlers.get('RelationshipPropertyDeleted')!('r1', 'quantity'));
      expect(useModelStore.getState().relationships[0].Properties?.quantity).toBe(5);
    });

    it('ignores a change to a relationship that is neither open nor on the open node', async () => {
      mockGetAllRels.mockResolvedValue([relationship({ quantity: 5 })]);
      useUiStore.setState({ selectedNodeId: null, selectedEdgeId: null });
      await mountLoaded();
      await waitFor(() => expect(useModelStore.getState().relationships).toHaveLength(1));

      await act(async () => handlers.get('RelationshipPropertyChanged')!('r1', 'quantity', 9));
      expect(useModelStore.getState().relationships[0].Properties?.quantity).toBe(5);
    });
  });
});
