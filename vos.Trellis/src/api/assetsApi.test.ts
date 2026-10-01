import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { assetsApi } from './assetsApi';
import { apiClient } from './client';

vi.mock('./client', () => ({
  apiClient: { ensureToken: vi.fn() },
}));

const ensureToken = vi.mocked(apiClient.ensureToken);
const ticket = 'sha256:' + 'cd'.repeat(32);

function answeredWith(status: number, contentType: string | null, bytes: ArrayBuffer): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: new Headers(contentType ? { 'Content-Type': contentType } : {}),
    arrayBuffer: async () => bytes,
  } as unknown as Response;
}

describe('assetsApi.getContent', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    ensureToken.mockResolvedValue('a-token');
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  // The token rides a header, never the address: an address is recorded in logs and history,
  // and a bare <img src> would arrive with no credential at all and be refused.
  it('asks the asset route with the bearer token in a header', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValue(answeredWith(200, 'image/png', new ArrayBuffer(4)));

    await assetsApi.getContent(ticket);

    const [address, options] = fetchSpy.mock.calls[0];
    expect(String(address)).toContain(`/api/assets/${ticket}`);
    expect((options!.headers as Record<string, string>).Authorization).toBe('Bearer a-token');
  });

  it('answers the bytes under the type the store served', async () => {
    const bytes = new ArrayBuffer(8);
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(answeredWith(200, 'image/webp', bytes));

    const content = await assetsApi.getContent(ticket);

    expect(content).not.toBeNull();
    expect(content!.bytes).toBe(bytes);
    expect(content!.contentType).toBe('image/webp');
  });

  it('answers null when the store holds nothing for the ticket', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(answeredWith(404, null, new ArrayBuffer(0)));

    expect(await assetsApi.getContent(ticket)).toBeNull();
  });

  it('answers null on any other refusal, so a row degrades to text instead of crashing', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(answeredWith(500, null, new ArrayBuffer(0)));

    expect(await assetsApi.getContent(ticket)).toBeNull();
  });
});
