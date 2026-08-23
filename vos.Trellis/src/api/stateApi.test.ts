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

describe('stateApi.getThingsInState, narrowed', () => {
  it('sends no query string when the caller narrows nothing', async () => {
    mockGet.mockResolvedValue({ StateName: 'overheating', Things: [] });
    await stateApi.getThingsInState('overheating', {});
    expect(mockGet).toHaveBeenCalledWith('/api/states/overheating/things');
  });

  it('puts every narrowing on the query string, escaped', async () => {
    mockGet.mockResolvedValue({ StateName: 'flagged', Things: [] });
    await stateApi.getThingsInState('flagged', {
      alsoIn: ['metered', 'verified'],
      notIn: ['decommissioned'],
      type: 'Water Reservoir',
      within: 'site-1',
      withinPredicate: 'contains',
      includeArchetypes: true,
      limit: 300,
      properties: ['volume', 'capacity'],
    });
    expect(mockGet).toHaveBeenCalledWith(
      '/api/states/flagged/things?alsoIn=metered%2Cverified&notIn=decommissioned&type=Water+Reservoir' +
        '&within=site-1&withinPredicate=contains&includeArchetypes=true&limit=300' +
        '&properties=volume%2Ccapacity',
    );
  });

  // The endpoint refuses a container without the predicate its containment is written with, so a
  // half-given scope is not a question worth spending a request on.
  it('sends a container only with the predicate it is reached by', async () => {
    mockGet.mockResolvedValue({ StateName: 'flagged', Things: [] });
    await stateApi.getThingsInState('flagged', { within: 'site-1' });
    expect(mockGet).toHaveBeenCalledWith('/api/states/flagged/things');
  });

  it('carries the properties the caller asked for back to it', async () => {
    mockGet.mockResolvedValue({
      StateName: 'flagged',
      Things: [{ Id: 'r1', Name: 'RSV-1', Properties: { volume: 4 } }],
    });
    const answer = await stateApi.getThingsInState('flagged', { properties: ['volume'] });
    expect(answer.Things[0].Properties).toEqual({ volume: 4 });
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
