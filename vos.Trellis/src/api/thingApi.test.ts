import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('./client', () => ({
  apiClient: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    del: vi.fn(),
  },
}));

import { thingApi } from './thingApi';
import { apiClient } from './client';

const mockGet = vi.mocked(apiClient.get);

beforeEach(() => {
  vi.clearAllMocks();
});

describe('thingApi.getEffectiveProperties', () => {
  // Bug #5908 — the backend renamed /effective-properties to /properties (cb6e84e); the caller
  // must track it or the Inherited section renders nothing.
  it('requests the /properties route', async () => {
    mockGet.mockResolvedValue({});
    await thingApi.getEffectiveProperties('thing-1');
    expect(mockGet).toHaveBeenCalledWith('/api/things/thing-1/properties');
  });
});
