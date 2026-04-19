import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { ModelPage } from './ModelPage';

// The FragmentsViewer pulls in @react-three/fiber + @thatopen/fragments which
// need WebGL and a worker. jsdom has neither, so we stub the whole component
// and assert only that ModelPage mounts it when bytes are available.
vi.mock('../components/model/FragmentsViewer', () => ({
  FragmentsViewer: ({ fragmentsBytes }: { fragmentsBytes: ArrayBuffer }) => (
    <div data-testid="fragments-viewer-stub" data-bytesize={fragmentsBytes.byteLength} />
  ),
}));

vi.mock('../api/client', () => ({
  apiClient: { getBytes: vi.fn() },
}));

let mockModelId: string | null = 'model-1';
vi.mock('../hooks/useAuth', () => ({
  useAuth: () => ({ modelId: mockModelId }),
}));

// Import after mocks so the mocked module is used.
import { apiClient } from '../api/client';
const mockGetBytes = vi.mocked(apiClient.getBytes);

describe('ModelPage', () => {
  beforeEach(() => {
    mockGetBytes.mockReset();
    mockModelId = 'model-1';
  });

  it('renders the Model heading', async () => {
    mockGetBytes.mockResolvedValue(null);
    render(<ModelPage />);
    expect(screen.getByRole('heading', { name: /model/i })).toBeInTheDocument();
  });

  it('shows loading state while fetching', () => {
    // Pending promise — never resolves within the test
    mockGetBytes.mockImplementation(() => new Promise(() => {}));
    render(<ModelPage />);
    expect(screen.getByTestId('model-viewer-loading')).toBeInTheDocument();
  });

  it('shows empty placeholder when broker returns 404', async () => {
    mockGetBytes.mockResolvedValue(null);
    render(<ModelPage />);
    await waitFor(() => {
      expect(screen.getByTestId('model-viewer-placeholder')).toBeInTheDocument();
    });
    expect(screen.getByText(/no fragments artifact loaded/i)).toBeInTheDocument();
  });

  it('mounts FragmentsViewer when bytes are returned', async () => {
    const bytes = new Uint8Array([0xDE, 0xAD, 0xBE, 0xEF]).buffer;
    mockGetBytes.mockResolvedValue(bytes);
    render(<ModelPage />);
    await waitFor(() => {
      expect(screen.getByTestId('fragments-viewer-stub')).toBeInTheDocument();
    });
    expect(screen.getByTestId('fragments-viewer-stub').getAttribute('data-bytesize')).toBe('4');
  });

  it('shows error placeholder when fetch throws', async () => {
    mockGetBytes.mockRejectedValue(new Error('network down'));
    render(<ModelPage />);
    await waitFor(() => {
      expect(screen.getByTestId('model-viewer-error')).toBeInTheDocument();
    });
    expect(screen.getByText(/network down/i)).toBeInTheDocument();
  });

  it('calls the broker Fragments endpoint exactly once', async () => {
    mockGetBytes.mockResolvedValue(null);
    render(<ModelPage />);
    await waitFor(() => {
      expect(mockGetBytes).toHaveBeenCalledTimes(1);
    });
    expect(mockGetBytes).toHaveBeenCalledWith('/api/model/fragments');
  });

  it('re-fetches when the JWT-scoped model changes', async () => {
    mockGetBytes.mockResolvedValue(null);
    const { rerender } = render(<ModelPage />);
    await waitFor(() => expect(mockGetBytes).toHaveBeenCalledTimes(1));

    mockModelId = 'model-2';
    rerender(<ModelPage />);
    await waitFor(() => expect(mockGetBytes).toHaveBeenCalledTimes(2));
  });
});
