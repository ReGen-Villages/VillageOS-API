import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { AssetImage } from './AssetImage';
import { assetsApi } from '../../api/assetsApi';

vi.mock('../../api/assetsApi', () => ({
  assetsApi: { getContent: vi.fn() },
}));

const getContent = vi.mocked(assetsApi.getContent);
const ticket = 'sha256:' + 'ef'.repeat(32);

const revokeObjectURL = vi.fn();

beforeEach(() => {
  vi.clearAllMocks();
  vi.stubGlobal('URL', {
    ...URL,
    createObjectURL: vi.fn(() => 'blob:kept-content'),
    revokeObjectURL,
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('a ticket naming an image', () => {
  it('is shown as the image itself', async () => {
    getContent.mockResolvedValue({ bytes: new ArrayBuffer(4), contentType: 'image/png' });

    render(<AssetImage ticket={ticket} />);

    const image = await screen.findByRole('img');
    expect(image).toHaveAttribute('src', 'blob:kept-content');
    expect(image).toHaveAttribute('alt', 'Content retrieved from the source');
  });

  it('releases the object address once the image is gone', async () => {
    getContent.mockResolvedValue({ bytes: new ArrayBuffer(4), contentType: 'image/png' });
    const { unmount } = render(<AssetImage ticket={ticket} />);
    await screen.findByRole('img');

    unmount();

    expect(revokeObjectURL).toHaveBeenCalledWith('blob:kept-content');
  });
});

describe('a ticket the store holds nothing for', () => {
  it('degrades to the ticket text, saying why', async () => {
    getContent.mockResolvedValue(null);

    render(<AssetImage ticket={ticket} />);

    const fallback = await screen.findByText(ticket);
    expect(fallback).toHaveAttribute('title', 'The store holds nothing for this ticket');
    expect(screen.queryByRole('img')).toBeNull();
  });
});

describe('a ticket naming content that is not an image', () => {
  it('degrades to the ticket text rather than a broken picture', async () => {
    getContent.mockResolvedValue({ bytes: new ArrayBuffer(4), contentType: 'application/json' });

    render(<AssetImage ticket={ticket} />);

    const fallback = await screen.findByText(ticket);
    expect(fallback).toHaveAttribute('title', 'The kept content is not an image');
    expect(screen.queryByRole('img')).toBeNull();
  });
});
