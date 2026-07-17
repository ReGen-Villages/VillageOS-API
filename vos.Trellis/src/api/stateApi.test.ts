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

describe('stateApi.getStateTransitions', () => {
  it('requests the transition timeline for a thing', async () => {
    mockGet.mockResolvedValue({ ThingId: 't1', Transitions: [] });
    await stateApi.getStateTransitions('t1');
    expect(mockGet).toHaveBeenCalledWith('/api/things/t1/state-transitions', undefined);
  });

  it('passes a from/to window as query parameters', async () => {
    mockGet.mockResolvedValue({ ThingId: 't1', Transitions: [] });
    await stateApi.getStateTransitions('t1', '2026-07-01T00:00:00Z', '2026-07-02T00:00:00Z');
    expect(mockGet).toHaveBeenCalledWith(
      '/api/things/t1/state-transitions?from=2026-07-01T00%3A00%3A00Z&to=2026-07-02T00%3A00%3A00Z',
      undefined,
    );
  });

  it('omits the query string entirely when no window is given', async () => {
    mockGet.mockResolvedValue({ ThingId: 't1', Transitions: [] });
    await stateApi.getStateTransitions('t1');
    expect(mockGet.mock.calls[0][0]).not.toContain('?');
  });
});

describe('stateApi.getStateOccurrences', () => {
  it('encodes the state name', async () => {
    mockGet.mockResolvedValue({ ThingId: 't1', StateName: 'low power', Occurrences: [] });
    await stateApi.getStateOccurrences('t1', 'low power');
    expect(mockGet).toHaveBeenCalledWith('/api/things/t1/states/low%20power/occurrences', undefined);
  });
});
