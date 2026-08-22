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

describe('modelApi.promote', () => {
  it('posts the root, what travels with it, the template and the project name', async () => {
    mockPost.mockResolvedValue({ modelId: 'model-1', modelName: 'Meadow Lane' });

    const result = await modelApi.promote('meadow', ['covers', 'studies'], 'site-analysis.template.json', 'Meadow Lane');

    expect(mockPost).toHaveBeenCalledWith('/api/model/promote', {
      RootThingId: 'meadow',
      FollowedPredicateNames: ['covers', 'studies'],
      Template: 'site-analysis.template.json',
      ProjectName: 'Meadow Lane',
    });
    expect(result).toEqual({ modelId: 'model-1', modelName: 'Meadow Lane' });
  });

  it('does not swallow a refusal', async () => {
    mockPost.mockRejectedValue(new Error('The project model cannot answer every name'));

    await expect(modelApi.promote('meadow', [], 'site-analysis.template.json', 'Meadow Lane')).rejects.toThrow(
      'The project model cannot answer every name',
    );
  });
});
