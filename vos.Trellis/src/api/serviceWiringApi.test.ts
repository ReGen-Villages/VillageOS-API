import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

vi.mock('./client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('./client')>()),
  apiClient: { ensureToken: () => Promise.resolve('token') },
}));

import { fetchServiceWiring, serviceWiringOf } from './serviceWiringApi';
import { AWAITED_THING_ROLE_ID, CALL_RECORD_ROLE_ID, NOTIFIED_CONNECTION_ROLE_ID } from '@tendril-ui/tendril';
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
    const wiring = serviceWiringOf('well');
    expect(wiring.ids).toEqual(expect.arrayContaining([
      'well', ROLE.Connection, ROLE.Service, AWAITED_THING_ROLE_ID, NOTIFIED_CONNECTION_ROLE_ID, CALL_RECORD_ROLE_ID,
    ]));
    expect(wiring.boundTypes).toEqual([ROLE.Connection, ROLE.Service]);
  });

  it('reaches the Things watching the card\'s Thing, whose archetypes name the connection their answer comes through', () => {
    expect(serviceWiringOf('well').traverse).toContainEqual({ predicateRole: AWAITED_THING_ROLE_ID, direction: 'incoming', depth: 1 });
    expect(serviceWiringOf('well').includeIsAncestors).not.toBe(false);
  });

  it('builds the index from one read of the selection, opening no subscription', async () => {
    answering(200, {
      watermark: 7,
      snapshot: { things: [{ Id: 'measures', Name: 'measures', Properties: {} }], relationships: [] },
    });

    const wiring = await fetchServiceWiring('well');

    expect(wiring.byId.get('measures')?.Name).toBe('measures');
    expect(calls).toHaveLength(1);
    expect(calls[0]).toMatchObject({ url: '/api/snapshots', request: { method: 'POST' } });
    expect(JSON.parse(String(calls[0].request.body))).toEqual(serviceWiringOf('well'));
  });

  it('throws on a refusal, so the card lists no service call rather than a wrong one', async () => {
    answering(403, { error: 'no' });

    await expect(fetchServiceWiring('well')).rejects.toThrow();
    expect(calls).toHaveLength(1);
  });
});
