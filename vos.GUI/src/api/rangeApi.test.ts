import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('./client', () => ({
  apiClient: {
    get: vi.fn(),
  },
}));

import { rangeApi, relationshipRangeApi } from './rangeApi';
import { apiClient } from './client';

const mockGet = vi.mocked(apiClient.get);

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
