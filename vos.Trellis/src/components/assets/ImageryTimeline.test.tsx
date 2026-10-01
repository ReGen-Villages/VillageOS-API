import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { ImageryTimeline } from './ImageryTimeline';
import { assetsApi } from '../../api/assetsApi';

vi.mock('../../api/assetsApi', () => ({
  assetsApi: { getContent: vi.fn() },
}));

const getContent = vi.mocked(assetsApi.getContent);

const earliest = 'sha256:' + '11'.repeat(32);
const middle = 'sha256:' + '22'.repeat(32);
const latest = 'sha256:' + '33'.repeat(32);

// Local-naive timestamps, so the fixed display pattern is the same on any machine.
const versions = [
  { Timestamp: '2026-02-01T00:00:00', Value: middle },
  { Timestamp: '2026-01-01T00:00:00', Value: earliest },
  { Timestamp: '2026-03-01T00:00:00', Value: latest },
  { Timestamp: '2026-01-15T00:00:00', Value: 42 },
];

beforeEach(() => {
  vi.clearAllMocks();
  getContent.mockResolvedValue({ bytes: new ArrayBuffer(4), contentType: 'image/png' });
  vi.stubGlobal('URL', {
    ...URL,
    createObjectURL: vi.fn(() => 'blob:kept-content'),
    revokeObjectURL: vi.fn(),
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('a series carrying tickets', () => {
  it('opens on the most recent image, with the whole span under the slider', async () => {
    render(<ImageryTimeline versions={versions} />);

    const slider = screen.getByRole('slider') as HTMLInputElement;
    expect(slider.max).toBe('2');
    expect(slider.value).toBe('2');
    expect(await screen.findByText('2026-03-01 00:00:00')).toBeInTheDocument();
  });

  it('scrubs to the image that was true then', async () => {
    render(<ImageryTimeline versions={versions} />);

    fireEvent.change(screen.getByRole('slider'), { target: { value: '0' } });

    expect(await screen.findByText('2026-01-01 00:00:00')).toBeInTheDocument();
    expect(screen.queryByText('2026-03-01 00:00:00')).toBeNull();
  });

  it('pins one instant so another can be scrubbed in beside it', async () => {
    render(<ImageryTimeline versions={versions} />);
    await screen.findByText('2026-03-01 00:00:00');

    fireEvent.click(screen.getByText('Pin for comparison'));
    fireEvent.change(screen.getByRole('slider'), { target: { value: '0' } });

    expect(await screen.findByText(/2026-03-01 00:00:00/)).toBeInTheDocument();
    expect(await screen.findByText('2026-01-01 00:00:00')).toBeInTheDocument();
    expect(await screen.findAllByRole('img')).toHaveLength(2);
    expect(screen.getByText('Unpin')).toBeInTheDocument();
  });
});

describe('a series carrying no ticket', () => {
  it('renders nothing: the plain history list already says everything', () => {
    const { container } = render(
      <ImageryTimeline versions={[{ Timestamp: '2026-01-01T00:00:00', Value: 3.5 }]} />,
    );

    expect(container.firstChild).toBeNull();
  });
});
