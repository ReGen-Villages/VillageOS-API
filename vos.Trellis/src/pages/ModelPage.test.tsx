import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, render, screen, waitFor } from '@testing-library/react';
import { ModelPage } from './ModelPage';

// BimFragmentsViewer pulls in @react-three/fiber + @thatopen/fragments which need
// WebGL and a worker. jsdom has neither, so we stub the whole component and
// expose the onPick callback so tests can simulate a pick.
let capturedOnPick: ((id: string | null) => void) | null = null;
let capturedHiddenIfcGuids: readonly string[] = [];
vi.mock('../components/model/BimFragmentsViewer', () => ({
  BimFragmentsViewer: ({
    bimFragmentsBytes,
    mapping,
    onPick,
    hiddenIfcGuids = [],
  }: {
    bimFragmentsBytes: ArrayBuffer;
    mapping: Record<string, string>;
    onPick: (id: string | null) => void;
    hiddenIfcGuids?: readonly string[];
  }) => {
    capturedOnPick = onPick;
    capturedHiddenIfcGuids = hiddenIfcGuids;
    return (
      <div
        data-testid="fragments-viewer-stub"
        data-bytesize={bimFragmentsBytes.byteLength}
        data-mapping-size={Object.keys(mapping).length}
        data-hidden-count={hiddenIfcGuids.length}
      />
    );
  },
}));

// NodeDetailPanel has its own test suite; stub it here so we only assert that
// ModelPage mounts the *same* component the GraphPage uses (Bug #5308).
vi.mock('../components/panels/NodeDetailPanel', () => ({
  NodeDetailPanel: ({
    thing,
    onClose,
  }: {
    thing: { Id: string; Name?: string };
    onClose: () => void;
  }) => (
    <aside data-testid="node-detail-panel" data-thing-id={thing.Id} data-thing-name={thing.Name}>
      <button data-testid="close-panel" onClick={onClose}>x</button>
    </aside>
  ),
}));

// ResizablePanel reads pointer state from useUiStore; for these tests we only
// care that its children render, so collapse it to a passthrough.
vi.mock('../components/panels/ResizablePanel', () => ({
  ResizablePanel: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('../api/client', () => ({
  apiClient: { getBytes: vi.fn(), get: vi.fn() },
}));

vi.mock('../api/thingApi', () => ({
  thingApi: { get: vi.fn(), deleteProperty: vi.fn() },
}));

let mockModelId: string | null = 'model-1';
vi.mock('../hooks/useAuth', () => ({
  useAuth: () => ({ modelId: mockModelId }),
}));

// Import after mocks so the mocked module is used.
import { apiClient } from '../api/client';
import { thingApi } from '../api/thingApi';
import { useUiStore } from '../stores/uiStore';
import { useModelStore } from '../stores/modelStore';
import type { VosThing } from '../types/vos';
const mockGetBytes = vi.mocked(apiClient.getBytes);
const mockGet = vi.mocked(thingApi.get);

function seedThings(things: VosThing[]) {
  useModelStore.setState({ things, relationships: [] });
}

function seedModel(things: VosThing[], relationships: import('../types/vos').VosRelationship[]) {
  useModelStore.setState({ things, relationships });
}

describe('ModelPage', () => {
  beforeEach(() => {
    mockGetBytes.mockReset();
    mockGet.mockReset();
    mockModelId = 'model-1';
    capturedOnPick = null;
    capturedHiddenIfcGuids = [];
    // Reset shared selection state so cross-test bleed-through can't mask bugs.
    useUiStore.setState({ selectedNodeId: null, selectedEdgeId: null, hiddenTypeIds: new Set() });
    // Feature #5329: ModelPage now consumes things from the model store,
    // populated by the app-shell-level useModelData hook. Tests seed it
    // directly because they don't mount the AuthenticatedApp shell.
    seedThings([]);
  });

  it('renders the Model heading', async () => {
    mockGetBytes.mockResolvedValue(null);
    render(<ModelPage />);
    expect(screen.getByRole('heading', { name: /model/i })).toBeInTheDocument();
  });

  it('shows loading state while fetching', () => {
    mockGetBytes.mockImplementation(() => new Promise(() => {}));
    render(<ModelPage />);
    expect(screen.getByTestId('model-viewer-loading')).toBeInTheDocument();
  });

  it('shows empty placeholder when Mycelium returns 404', async () => {
    mockGetBytes.mockResolvedValue(null);
    render(<ModelPage />);
    await waitFor(() => {
      expect(screen.getByTestId('model-viewer-placeholder')).toBeInTheDocument();
    });
  });

  it('mounts BimFragmentsViewer with bytes + mapping derived from the model store', async () => {
    const bytes = new Uint8Array([0xDE, 0xAD, 0xBE, 0xEF]).buffer;
    mockGetBytes.mockResolvedValue(bytes);
    seedThings([
      { Id: 'vos-guid-1', Name: 'T', Properties: { ifcGlobalId: '2UMzzDFwXBAe1ciOx9dLWU' } },
    ]);
    render(<ModelPage />);
    await waitFor(() => {
      expect(screen.getByTestId('fragments-viewer-stub')).toBeInTheDocument();
    });
    const stub = screen.getByTestId('fragments-viewer-stub');
    expect(stub.getAttribute('data-bytesize')).toBe('4');
    expect(stub.getAttribute('data-mapping-size')).toBe('1');
  });

  it('shows error placeholder when fetch throws', async () => {
    mockGetBytes.mockRejectedValue(new Error('network down'));
    render(<ModelPage />);
    await waitFor(() => {
      expect(screen.getByTestId('model-viewer-error')).toBeInTheDocument();
    });
    expect(screen.getByText(/network down/i)).toBeInTheDocument();
  });

  it('re-fetches the .frag when the JWT-scoped model changes', async () => {
    mockGetBytes.mockResolvedValue(null);
    const { rerender } = render(<ModelPage />);
    await waitFor(() => expect(mockGetBytes).toHaveBeenCalledTimes(1));

    mockModelId = 'model-2';
    rerender(<ModelPage />);
    await waitFor(() => expect(mockGetBytes).toHaveBeenCalledTimes(2));
  });

  // Regression test for Bug #5308: the model page must mount NodeDetailPanel
  // (the same component the GraphPage uses) — never a duplicate panel.
  it('mounts NodeDetailPanel (not a duplicate panel) when viewer picks an element', async () => {
    const bytes = new Uint8Array([0x01]).buffer;
    mockGetBytes.mockResolvedValue(bytes);
    seedThings([
      { Id: 'vos-guid-1', Name: 'LivingRoom_101', Properties: { ifcGlobalId: 'ifc1' } },
    ]);
    mockGet.mockResolvedValue({
      Id: 'vos-guid-1',
      Name: 'LivingRoom_101',
      Properties: { ifcGlobalId: 'ifc1', wall_color: 'white' },
    });

    render(<ModelPage />);
    await waitFor(() => expect(screen.getByTestId('fragments-viewer-stub')).toBeInTheDocument());
    expect(screen.queryByTestId('node-detail-panel')).toBeNull();

    await act(async () => capturedOnPick!('vos-guid-1'));
    const panel = await screen.findByTestId('node-detail-panel');
    expect(panel.getAttribute('data-thing-id')).toBe('vos-guid-1');
    expect(panel.getAttribute('data-thing-name')).toBe('LivingRoom_101');
    expect(mockGet).toHaveBeenCalledWith('vos-guid-1');
  });

  it('hides the detail panel when pick misses geometry', async () => {
    const bytes = new Uint8Array([0x01]).buffer;
    mockGetBytes.mockResolvedValue(bytes);
    seedThings([{ Id: 'vos-guid-1', Name: 'T', Properties: {} }]);
    mockGet.mockResolvedValue({ Id: 'vos-guid-1', Name: 'T', Properties: {} });

    render(<ModelPage />);
    await waitFor(() => expect(screen.getByTestId('fragments-viewer-stub')).toBeInTheDocument());

    await act(async () => capturedOnPick!('vos-guid-1'));
    expect(await screen.findByTestId('node-detail-panel')).toBeInTheDocument();

    await act(async () => capturedOnPick!(null));
    await waitFor(() => expect(screen.queryByTestId('node-detail-panel')).toBeNull());
  });

  it('hides the detail panel when close button is clicked', async () => {
    const bytes = new Uint8Array([0x01]).buffer;
    mockGetBytes.mockResolvedValue(bytes);
    seedThings([{ Id: 'vos-guid-1', Name: 'T', Properties: {} }]);
    mockGet.mockResolvedValue({ Id: 'vos-guid-1', Name: 'T', Properties: {} });

    render(<ModelPage />);
    await waitFor(() => expect(screen.getByTestId('fragments-viewer-stub')).toBeInTheDocument());

    await act(async () => capturedOnPick!('vos-guid-1'));
    const closeBtn = await screen.findByTestId('close-panel');
    await act(async () => closeBtn.click());
    await waitFor(() => expect(screen.queryByTestId('node-detail-panel')).toBeNull());
  });

  // Regression test for Bug #5384: the hiddenTypeIds → hiddenIfcGuids
  // translation must cover the same three cases as applyTypeFilter on the
  // Graph page: (a) instances of a hidden type, (b) the hidden type-Things
  // themselves, (c) untyped Things when NO_TYPE_ID is hidden. The previous
  // bespoke loop only handled (a), leaving ~9.6k IFC objects rendered after
  // clicking "None" on the example model.
  it('emits hiddenIfcGuids for instances, type-Things, AND untyped Things', async () => {
    const bytes = new Uint8Array([0x01]).buffer;
    mockGetBytes.mockResolvedValue(bytes);

    // Model: one type Thing (with its own IFC geometry — IfcWallType),
    // one instance Thing (is-related to the type), and one untyped Thing
    // (no `is` relation — e.g. IfcDistributionPort).
    const isPredicate: VosThing = { Id: 'pred-is', Name: 'is', Properties: {} };
    const typeThing: VosThing = {
      Id: 'type-wall',
      Name: 'Basic Wall:Generic-200mm',
      Properties: { ifcGlobalId: 'ifc-type-guid' },
    };
    const instance: VosThing = {
      Id: 'inst-1',
      Name: 'Wall_101',
      Properties: { ifcGlobalId: 'ifc-instance-guid' },
    };
    const untyped: VosThing = {
      Id: 'port-1',
      Name: 'Port_962966_1',
      Properties: { ifcGlobalId: 'ifc-untyped-guid' },
    };
    seedModel(
      [isPredicate, typeThing, instance, untyped],
      [{
        Id: 'rel-1',
        Name: 'inst-1 is type-wall',
        SubjectId: 'inst-1',
        PredicateId: 'pred-is',
        TargetId: 'type-wall',
        Properties: {},
      }],
    );

    // Hide every group — same set the TypeFilterPanel produces on "None".
    const { NO_TYPE_ID } = await import('../utils/typeFilter');
    useUiStore.setState({ hiddenTypeIds: new Set(['type-wall', NO_TYPE_ID]) });

    render(<ModelPage />);
    await waitFor(() => expect(screen.getByTestId('fragments-viewer-stub')).toBeInTheDocument());

    const guids = [...capturedHiddenIfcGuids].sort();
    expect(guids).toEqual(['ifc-instance-guid', 'ifc-type-guid', 'ifc-untyped-guid']);
  });

  it('drives selection through useUiStore so it stays in sync with the GraphPage', async () => {
    const bytes = new Uint8Array([0x01]).buffer;
    mockGetBytes.mockResolvedValue(bytes);
    seedThings([{ Id: 'vos-guid-1', Name: 'T', Properties: {} }]);
    mockGet.mockResolvedValue({ Id: 'vos-guid-1', Name: 'T', Properties: {} });

    render(<ModelPage />);
    await waitFor(() => expect(screen.getByTestId('fragments-viewer-stub')).toBeInTheDocument());
    expect(useUiStore.getState().selectedNodeId).toBeNull();

    await act(async () => capturedOnPick!('vos-guid-1'));
    await waitFor(() => expect(useUiStore.getState().selectedNodeId).toBe('vos-guid-1'));

    await act(async () => capturedOnPick!(null));
    await waitFor(() => expect(useUiStore.getState().selectedNodeId).toBeNull());
  });
});
