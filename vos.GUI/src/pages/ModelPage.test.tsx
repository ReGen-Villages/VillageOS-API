import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, render, screen, waitFor } from '@testing-library/react';
import { ModelPage } from './ModelPage';

// FragmentsViewer pulls in @react-three/fiber + @thatopen/fragments which
// need WebGL and a worker. jsdom has neither, so we stub the whole component
// and expose the onPick callback so tests can simulate a pick.
let capturedOnPick: ((id: string | null) => void) | null = null;
vi.mock('../components/model/FragmentsViewer', () => ({
  FragmentsViewer: ({
    fragmentsBytes,
    mapping,
    onPick,
  }: {
    fragmentsBytes: ArrayBuffer;
    mapping: Record<string, string>;
    onPick: (id: string | null) => void;
  }) => {
    capturedOnPick = onPick;
    return (
      <div
        data-testid="fragments-viewer-stub"
        data-bytesize={fragmentsBytes.byteLength}
        data-mapping-size={Object.keys(mapping).length}
      />
    );
  },
}));

// FragmentsMetadataPanel is simple (fetches thingApi.get + renders). Stub it
// so ModelPage tests don't also exercise thingApi.
vi.mock('../components/model/FragmentsMetadataPanel', () => ({
  FragmentsMetadataPanel: ({ thingId, onClose }: { thingId: string; onClose: () => void }) => (
    <aside data-testid="fragments-metadata-panel" data-thing-id={thingId}>
      <button data-testid="close-panel" onClick={onClose}>x</button>
    </aside>
  ),
}));

vi.mock('../api/client', () => ({
  apiClient: { getBytes: vi.fn(), get: vi.fn() },
}));

vi.mock('../api/thingApi', () => ({
  thingApi: { getAll: vi.fn() },
}));

let mockModelId: string | null = 'model-1';
vi.mock('../hooks/useAuth', () => ({
  useAuth: () => ({ modelId: mockModelId }),
}));

// Import after mocks so the mocked module is used.
import { apiClient } from '../api/client';
import { thingApi } from '../api/thingApi';
const mockGetBytes = vi.mocked(apiClient.getBytes);
const mockGetAll = vi.mocked(thingApi.getAll);

describe('ModelPage', () => {
  beforeEach(() => {
    mockGetBytes.mockReset();
    mockGetAll.mockReset();
    mockGetAll.mockResolvedValue([]);
    mockModelId = 'model-1';
    capturedOnPick = null;
  });

  it('renders the Model heading', async () => {
    mockGetBytes.mockResolvedValue(null);
    mockGetAll.mockResolvedValue([]);
    render(<ModelPage />);
    expect(screen.getByRole('heading', { name: /model/i })).toBeInTheDocument();
  });

  it('shows loading state while fetching', () => {
    mockGetBytes.mockImplementation(() => new Promise(() => {}));
    mockGetAll.mockResolvedValue([]);
    render(<ModelPage />);
    expect(screen.getByTestId('model-viewer-loading')).toBeInTheDocument();
  });

  it('shows empty placeholder when broker returns 404', async () => {
    mockGetBytes.mockResolvedValue(null);
    mockGetAll.mockResolvedValue([]);
    render(<ModelPage />);
    await waitFor(() => {
      expect(screen.getByTestId('model-viewer-placeholder')).toBeInTheDocument();
    });
  });

  it('mounts FragmentsViewer with bytes + mapping when loaded', async () => {
    const bytes = new Uint8Array([0xDE, 0xAD, 0xBE, 0xEF]).buffer;
    mockGetBytes.mockResolvedValue(bytes);
    mockGetAll.mockResolvedValue([{ Id: 'vos-guid-1', Name: 'T', Properties: { ifcGlobalId: '2UMzzDFwXBAe1ciOx9dLWU' } }]);
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
    mockGetAll.mockResolvedValue([]);
    render(<ModelPage />);
    await waitFor(() => {
      expect(screen.getByTestId('model-viewer-error')).toBeInTheDocument();
    });
    expect(screen.getByText(/network down/i)).toBeInTheDocument();
  });

  it('re-fetches when the JWT-scoped model changes', async () => {
    mockGetBytes.mockResolvedValue(null);
    mockGetAll.mockResolvedValue([]);
    const { rerender } = render(<ModelPage />);
    await waitFor(() => expect(mockGetBytes).toHaveBeenCalledTimes(1));

    mockModelId = 'model-2';
    rerender(<ModelPage />);
    await waitFor(() => expect(mockGetBytes).toHaveBeenCalledTimes(2));
  });

  it('renders metadata panel when viewer picks an element', async () => {
    const bytes = new Uint8Array([0x01]).buffer;
    mockGetBytes.mockResolvedValue(bytes);
    mockGetAll.mockResolvedValue([{ Id: 'vos-guid-1', Name: 'T', Properties: { ifcGlobalId: 'ifc1' } }]);
    render(<ModelPage />);
    await waitFor(() => expect(screen.getByTestId('fragments-viewer-stub')).toBeInTheDocument());
    expect(screen.queryByTestId('fragments-metadata-panel')).toBeNull();

    await act(async () => capturedOnPick!('vos-guid-1'));
    const panel = await screen.findByTestId('fragments-metadata-panel');
    expect(panel.getAttribute('data-thing-id')).toBe('vos-guid-1');
  });

  it('hides metadata panel when pick misses geometry', async () => {
    const bytes = new Uint8Array([0x01]).buffer;
    mockGetBytes.mockResolvedValue(bytes);
    mockGetAll.mockResolvedValue([]);
    render(<ModelPage />);
    await waitFor(() => expect(screen.getByTestId('fragments-viewer-stub')).toBeInTheDocument());

    await act(async () => capturedOnPick!('vos-guid-1'));
    expect(await screen.findByTestId('fragments-metadata-panel')).toBeInTheDocument();

    await act(async () => capturedOnPick!(null));
    await waitFor(() => expect(screen.queryByTestId('fragments-metadata-panel')).toBeNull());
  });

  it('hides metadata panel when close button is clicked', async () => {
    const bytes = new Uint8Array([0x01]).buffer;
    mockGetBytes.mockResolvedValue(bytes);
    mockGetAll.mockResolvedValue([]);
    render(<ModelPage />);
    await waitFor(() => expect(screen.getByTestId('fragments-viewer-stub')).toBeInTheDocument());

    await act(async () => capturedOnPick!('vos-guid-1'));
    const closeBtn = await screen.findByTestId('close-panel');
    await act(async () => closeBtn.click());
    await waitFor(() => expect(screen.queryByTestId('fragments-metadata-panel')).toBeNull());
  });
});
