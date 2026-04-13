import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('./client', () => ({
  apiClient: {
    get: vi.fn(),
    post: vi.fn(),
    del: vi.fn(),
  },
}));

import { rangeApi, relationshipRangeApi } from './rangeApi';
import { apiClient } from './client';

const mockGet = vi.mocked(apiClient.get);
const mockPost = vi.mocked(apiClient.post);
const mockDel = vi.mocked(apiClient.del);

beforeEach(() => {
  vi.clearAllMocks();
});

describe('rangeApi.getSummary', () => {
  it('gets from correct URL', async () => {
    mockGet.mockResolvedValue({ ThingId: 't1', ThingName: 'X', OwnRanges: [], InheritedRanges: [], CurrentStates: [], RangeEvaluations: [], OutOfBoundsCount: 0, Relationships: [] });
    await rangeApi.getSummary('thing-1');
    expect(mockGet).toHaveBeenCalledWith('/api/things/thing-1/range-summary');
  });
});

describe('rangeApi.getAll', () => {
  it('gets all ranges for a thing', async () => {
    mockGet.mockResolvedValue([]);
    await rangeApi.getAll('thing-1');
    expect(mockGet).toHaveBeenCalledWith('/api/things/thing-1/ranges');
  });
});

describe('rangeApi.get', () => {
  it('gets a specific range by name', async () => {
    mockGet.mockResolvedValue({ Name: 'overheating', Criteria: 'temp > 100' });
    await rangeApi.get('thing-1', 'overheating');
    expect(mockGet).toHaveBeenCalledWith('/api/things/thing-1/ranges/overheating');
  });

  it('encodes the range name', async () => {
    mockGet.mockResolvedValue({});
    await rangeApi.get('thing-1', 'my range');
    expect(mockGet).toHaveBeenCalledWith('/api/things/thing-1/ranges/my%20range');
  });
});

describe('rangeApi.create', () => {
  it('posts to the correct URL with body', async () => {
    mockPost.mockResolvedValue({ Name: 'overheating' });
    await rangeApi.create('thing-1', { Name: 'overheating', Criteria: 'temp > 100' });
    expect(mockPost).toHaveBeenCalledWith('/api/things/thing-1/ranges', { Name: 'overheating', Criteria: 'temp > 100' });
  });
});

describe('rangeApi.delete', () => {
  it('deletes from the correct URL', async () => {
    mockDel.mockResolvedValue(undefined);
    await rangeApi.delete('thing-1', 'overheating');
    expect(mockDel).toHaveBeenCalledWith('/api/things/thing-1/ranges/overheating');
  });
});

describe('rangeApi.validateCriteria', () => {
  it('posts criteria for validation', async () => {
    mockPost.mockResolvedValue({ IsValid: true });
    const result = await rangeApi.validateCriteria('temp > 100');
    expect(mockPost).toHaveBeenCalledWith('/api/ranges/validate', { Criteria: 'temp > 100' });
    expect(result).toEqual({ IsValid: true });
  });
});

describe('relationshipRangeApi.getAll', () => {
  it('gets from correct URL', async () => {
    mockGet.mockResolvedValue({ RelationshipId: 'r1', RelationshipName: 'R', OwnRanges: [] });
    await relationshipRangeApi.getAll('rel-1');
    expect(mockGet).toHaveBeenCalledWith('/api/relationships/rel-1/ranges');
  });
});

describe('relationshipRangeApi.getStates', () => {
  it('gets from correct URL', async () => {
    mockGet.mockResolvedValue({ RelationshipId: 'r1', RelationshipName: 'R', CurrentStates: [], RangeEvaluations: [], OutOfBoundsCount: 0 });
    await relationshipRangeApi.getStates('rel-1');
    expect(mockGet).toHaveBeenCalledWith('/api/relationships/rel-1/states');
  });
});
