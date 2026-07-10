import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('./client', () => ({
  apiClient: {
    get: vi.fn(),
    getText: vi.fn(),
    post: vi.fn(),
    del: vi.fn(),
  },
}));

import { modelApi } from './modelApi';
import { apiClient } from './client';

const mockPost = vi.mocked(apiClient.post);

beforeEach(() => {
  vi.clearAllMocks();
});

describe('modelApi.applyFragment', () => {
  it('parses the fragment JSON and POSTs it to /api/model/fragment', async () => {
    mockPost.mockResolvedValue({ thingsCreated: 2, thingsUpdated: 1, relationshipsCreated: 3, things: [] });
    const fragment = '{"Name":"demo","Things":[{"Id":"1","Name":"a"}],"Relationships":[]}';

    const result = await modelApi.applyFragment(fragment);

    expect(mockPost).toHaveBeenCalledWith('/api/model/fragment', {
      Name: 'demo',
      Things: [{ Id: '1', Name: 'a' }],
      Relationships: [],
    });
    expect(result.thingsCreated).toBe(2);
    expect(result.relationshipsCreated).toBe(3);
  });

  it('throws on invalid JSON before calling the API', () => {
    expect(() => modelApi.applyFragment('not json')).toThrow();
    expect(mockPost).not.toHaveBeenCalled();
  });
});
