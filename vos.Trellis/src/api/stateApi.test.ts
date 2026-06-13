import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('./client', () => ({
  apiClient: {
    get: vi.fn(),
  },
}));

import { stateApi } from './stateApi';
import { apiClient } from './client';

const mockGet = vi.mocked(apiClient.get);

beforeEach(() => {
  vi.clearAllMocks();
});

describe('stateApi.getThingsInState', () => {
  it('queries things in a given state', async () => {
    mockGet.mockResolvedValue({ StateName: 'overheating', Things: [{ Id: 't1', Name: 'Sensor' }] });
    const result = await stateApi.getThingsInState('overheating');
    expect(mockGet).toHaveBeenCalledWith('/api/states/overheating/things');
    expect(result.Things).toHaveLength(1);
  });

  it('encodes state name with spaces', async () => {
    mockGet.mockResolvedValue({ StateName: 'low power', Things: [] });
    await stateApi.getThingsInState('low power');
    expect(mockGet).toHaveBeenCalledWith('/api/states/low%20power/things');
  });
});
