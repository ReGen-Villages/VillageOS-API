import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

vi.mock('./client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('./client')>()),
  apiClient: { ensureToken: () => Promise.resolve('token') },
}));

import { fetchServiceWiring, SERVICE_WIRING } from './serviceWiringApi';
import { DISPATCH_RECORD_ROLE_ID, NOTIFIED_CONNECTION_ROLE_ID, VIGIL_ROLE_ID } from './platformRoles';
import { ROLE } from '../pipeline/model';

const calls: { url: string; request: RequestInit }[] = [];

function answering(status: number, body: unknown) {
  vi.stubGlobal('fetch', vi.fn((url: string, request: RequestInit) => {
    calls.push({ url, request });
    return Promise.resolve(new Response(JSON.stringify(body), { status }));
  }));
}

describe('fetchServiceWiring', () => {
  beforeEach(() => { calls.length = 0; });
  afterEach(() => { vi.unstubAllGlobals(); });

  it('asks for every role the service section reads, with the members of each archetype it binds', () => {
    expect(SERVICE_WIRING.ids).toEqual(expect.arrayContaining([
      ROLE.Connection, ROLE.Service, VIGIL_ROLE_ID, NOTIFIED_CONNECTION_ROLE_ID, DISPATCH_RECORD_ROLE_ID,
    ]));
    expect(SERVICE_WIRING.boundTypes).toEqual([ROLE.Connection, ROLE.Service, VIGIL_ROLE_ID]);
  });

  it('builds the index from the snapshot and hands the subscription back', async () => {
    answering(200, {
      subscriptionId: 'held-1',
      snapshot: { things: [{ Id: 'measures', Name: 'measures', Properties: {} }], relationships: [] },
    });

    const wiring = await fetchServiceWiring();

    expect(wiring.byId.get('measures')?.Name).toBe('measures');
    expect(JSON.parse(String(calls[0].request.body))).toMatchObject({ ...SERVICE_WIRING, includeSnapshot: true });
    expect(calls[1]).toMatchObject({ url: '/api/subscriptions/held-1', request: { method: 'DELETE' } });
  });

  it('throws on a refusal, so the card lists no dispatch rather than a wrong one', async () => {
    answering(403, { error: 'no' });

    await expect(fetchServiceWiring()).rejects.toThrow();
    expect(calls).toHaveLength(1);
  });
});
