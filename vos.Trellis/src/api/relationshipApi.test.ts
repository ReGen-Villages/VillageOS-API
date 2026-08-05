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

// Feature #6146 — the route is the same one the Thing side reads, served by the same handler, and
// is what tells a relationship panel a property's declared type and where an inherited one came from.
describe('relationshipApi.getEffectiveProperties', () => {
  it('reads the relationship properties route', async () => {
    mockGet.mockResolvedValue({});
    await relationshipApi.getEffectiveProperties('rel-1');
    expect(mockGet).toHaveBeenCalledWith('/api/relationships/rel-1/properties');
  });
});

describe('relationshipApi.getAll', () => {
  it('gets from correct URL', async () => {
    mockGet.mockResolvedValue([]);
    await relationshipApi.getAll();
    expect(mockGet).toHaveBeenCalledWith('/api/relationships');
  });
});

describe('relationshipApi.get', () => {
  it('gets a single relationship from the id URL', async () => {
    mockGet.mockResolvedValue({ Id: 'rel-123', Name: 'is', SubjectId: 's1', PredicateId: 'p1', TargetId: 't1', Properties: {} });
    const rel = await relationshipApi.get('rel-123');
    expect(mockGet).toHaveBeenCalledWith('/api/relationships/rel-123');
    expect(rel.Id).toBe('rel-123');
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
    mockPut.mockResolvedValue({ Id: 'r1', Name: 'test', Properties: { quantity: 1.0 } });
    await relationshipApi.setProperty('rel-123', 'quantity', 'vos.Double', 1.0);
    expect(mockPut).toHaveBeenCalledWith('/api/relationships/rel-123/properties', {
      Name: 'quantity',
      Type: 'vos.Double',
      Value: 1.0,
    });
  });

  it('handles string property values', async () => {
    mockPut.mockResolvedValue({});
    await relationshipApi.setProperty('r1', 'label', 'vos.String', 'hello');
    expect(mockPut).toHaveBeenCalledWith('/api/relationships/r1/properties', {
      Name: 'label',
      Type: 'vos.String',
      Value: 'hello',
    });
  });

  // Bug #6141 — a relationship has one property route that both creates and updates, unlike a
  // Thing. Adding must reach it, so a caller can add to either without knowing which it holds.
  it('adds through that same route', async () => {
    mockPut.mockResolvedValue({});
    await relationshipApi.addProperty('r1', 'label', 'vos.String', 'hello');
    expect(mockPut).toHaveBeenCalledWith('/api/relationships/r1/properties', {
      Name: 'label',
      Type: 'vos.String',
      Value: 'hello',
    });
  });
});
