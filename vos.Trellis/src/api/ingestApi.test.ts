import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('./client', () => ({ apiClient: { ensureToken: vi.fn() } }));

import { ingestApi } from './ingestApi';
import { apiClient } from './client';

const fetchMock = vi.fn();

beforeEach(() => {
  vi.clearAllMocks();
  (import.meta.env as Record<string, string>).VITE_INGEST_URL = 'http://localhost:6100';
  vi.stubGlobal('fetch', fetchMock);
  vi.mocked(apiClient.ensureToken).mockResolvedValue('jwt-123');
});

describe('ingestApi.upload', () => {
  it('posts multipart form-data to <ingest>/ingest with a bearer token', async () => {
    fetchMock.mockResolvedValue({
      ok: true,
      json: () => Promise.resolve({ success: true, thingsCreated: 7, thingsUpdated: 0, relationshipsCreated: 4 }),
    });
    const file = new File(['ISO-10303-21;'], 'building.ifc');

    const result = await ingestApi.upload(file, 'Demo', 'merge');

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('http://localhost:6100/ingest');
    expect(init.method).toBe('POST');
    expect(init.headers.Authorization).toBe('Bearer jwt-123');
    expect(init.body).toBeInstanceOf(FormData);
    const form = init.body as FormData;
    expect(form.get('name')).toBe('Demo');
    expect(form.get('mode')).toBe('merge');
    expect(result.thingsCreated).toBe(7);
    expect(result.relationshipsCreated).toBe(4);
  });

  it('throws when the ingest URL is not configured, without calling fetch', async () => {
    (import.meta.env as Record<string, string>).VITE_INGEST_URL = '';
    await expect(ingestApi.upload(new File([''], 'x.ifc'), 'D', 'merge')).rejects.toThrow();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('reports configured() from the env', () => {
    (import.meta.env as Record<string, string>).VITE_INGEST_URL = 'http://x';
    expect(ingestApi.configured()).toBe(true);
    (import.meta.env as Record<string, string>).VITE_INGEST_URL = '';
    expect(ingestApi.configured()).toBe(false);
  });
});
