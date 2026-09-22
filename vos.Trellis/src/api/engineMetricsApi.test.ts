import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('./client', () => ({
  apiClient: {
    get: vi.fn(),
  },
}));

import { engineMetricsApi } from './engineMetricsApi';
import { apiClient } from './client';

const mockGet = vi.mocked(apiClient.get);

beforeEach(() => {
  vi.clearAllMocks();
});

describe('engineMetricsApi.getSummary', () => {
  it('gets the summary route and returns the parsed body', async () => {
    const summary = { ModelId: 'm1', ModelName: 'M' };
    mockGet.mockResolvedValue(summary);

    const result = await engineMetricsApi.getSummary();

    expect(mockGet).toHaveBeenCalledWith('/api/engines/metrics');
    expect(result).toBe(summary);
  });
});
