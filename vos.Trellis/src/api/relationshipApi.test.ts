import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('./client', () => ({
  apiClient: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    del: vi.fn(),
  },
}));

import { relationshipApi } from './relationshipApi';
import { apiClient } from './client';

const mockGet = vi.mocked(apiClient.get);
const mockPost = vi.mocked(apiClient.post);
const mockPut = vi.mocked(apiClient.put);
const mockDel = vi.mocked(apiClient.del);

beforeEach(() => {
  vi.clearAllMocks();
});

describe('relationshipApi.getAll', () => {
  it('gets from correct URL', async () => {
    mockGet.mockResolvedValue([]);
    await relationshipApi.getAll();
    expect(mockGet).toHaveBeenCalledWith('/api/relationships');
  });
});

describe('relationshipApi.create', () => {
  it('posts with subject, predicate, and target IDs', async () => {
    mockPost.mockResolvedValue({ Id: 'r1', Name: 'test', SubjectId: 's1', PredicateId: 'p1', TargetId: 't1', Properties: {} });
    await relationshipApi.create('s1', 'p1', 't1');
    expect(mockPost).toHaveBeenCalledWith('/api/relationships', {
      SubjectId: 's1',
      PredicateId: 'p1',
      TargetId: 't1',
    });
  });
});

describe('relationshipApi.remove', () => {
  it('deletes from correct URL', async () => {
    mockDel.mockResolvedValue({ message: 'Deleted' });
    await relationshipApi.remove('rel-123');
    expect(mockDel).toHaveBeenCalledWith('/api/relationships/rel-123');
  });
});

describe('relationshipApi.setProperty', () => {
  it('puts to correct URL with Name, Type, Value body', async () => {
    mockPut.mockResolvedValue({ Id: 'r1', Name: 'test', Properties: { qty: 1.0 } });
    await relationshipApi.setProperty('rel-123', 'qty', 'double', 1.0);
    expect(mockPut).toHaveBeenCalledWith('/api/relationships/rel-123/properties', {
      Name: 'qty',
      Type: 'double',
      Value: 1.0,
    });
  });

  it('handles string property values', async () => {
    mockPut.mockResolvedValue({});
    await relationshipApi.setProperty('r1', 'label', 'string', 'hello');
    expect(mockPut).toHaveBeenCalledWith('/api/relationships/r1/properties', {
      Name: 'label',
      Type: 'string',
      Value: 'hello',
    });
  });
});
