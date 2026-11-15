import { describe, it, expect, vi } from 'vitest';

vi.mock('./client', () => ({ apiClient: { get: vi.fn() } }));

import { apiClient } from './client';
import { callSettingsApi } from './callSettingsApi';

describe('callSettingsApi.get', () => {
  it('reads what applies to each of a connection\'s call settings from the connection\'s route', async () => {
    const answer = { callsAgainAtMost: { value: 5, source: 'builtIn', from: null } };
    vi.mocked(apiClient.get).mockResolvedValue(answer);
    const { signal } = new AbortController();

    expect(await callSettingsApi.get('estimate-rainfall', signal)).toBe(answer);
    expect(apiClient.get).toHaveBeenCalledWith('/api/connections/estimate-rainfall/call-settings', signal);
  });
});
