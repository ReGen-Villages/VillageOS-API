import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('./client', () => ({
  apiClient: {
    get: vi.fn(),
  },
}));

import { temporalApi } from './temporalApi';
import { apiClient } from './client';

const mockGet = vi.mocked(apiClient.get);

beforeEach(() => {
  vi.clearAllMocks();
});

describe('temporalApi.getPropertyVersions', () => {
  it('builds URL with time params', async () => {
    mockGet.mockResolvedValue({ ObjectId: 't1', PropertyName: 'temp', Versions: [] });
    await temporalApi.getPropertyVersions('t1', 'temp', '2024-01-01', '2024-12-31');
    expect(mockGet).toHaveBeenCalledWith(
      '/api/things/t1/properties/temp/versions?startTime=2024-01-01&endTime=2024-12-31',
    );
  });

  it('omits empty time params', async () => {
    mockGet.mockResolvedValue({ ObjectId: 't1', PropertyName: 'temp', Versions: [] });
    await temporalApi.getPropertyVersions('t1', 'temp');
    expect(mockGet).toHaveBeenCalledWith('/api/things/t1/properties/temp/versions');
  });
});

describe('temporalApi.getModelMutations', () => {
  it('fetches model mutations', async () => {
    mockGet.mockResolvedValue({ TotalMutations: 0, ThingMutations: {} });
    await temporalApi.getModelMutations();
    expect(mockGet).toHaveBeenCalledWith('/api/mutations');
  });
});

describe('temporalApi.getThingMutations', () => {
  it('fetches thing mutations with time range', async () => {
    mockGet.mockResolvedValue({ ObjectId: 't1', Mutations: [] });
    await temporalApi.getThingMutations('t1', '2024-01-01', '2024-12-31');
    expect(mockGet).toHaveBeenCalledWith(
      '/api/things/t1/mutations?startTime=2024-01-01&endTime=2024-12-31',
      undefined,
    );
  });

  it('fetches thing mutations without time range', async () => {
    mockGet.mockResolvedValue({ ObjectId: 't1', Mutations: [] });
    await temporalApi.getThingMutations('t1');
    expect(mockGet).toHaveBeenCalledWith('/api/things/t1/mutations', undefined);
  });

  it('forwards an abort signal so a superseded round can be abandoned', async () => {
    mockGet.mockResolvedValue({ ObjectId: 't1', Mutations: [] });
    const { signal } = new AbortController();
    await temporalApi.getThingMutations('t1', undefined, undefined, signal);
    expect(mockGet).toHaveBeenCalledWith('/api/things/t1/mutations', signal);
  });
});

describe('temporalApi.getRelationshipMutations', () => {
  it('fetches relationship mutations', async () => {
    mockGet.mockResolvedValue({ RelationshipId: 'r1', Mutations: [] });
    await temporalApi.getRelationshipMutations('r1', '2024-06-01');
    expect(mockGet).toHaveBeenCalledWith(
      '/api/relationships/r1/mutations?startTime=2024-06-01',
      undefined,
    );
  });
});
