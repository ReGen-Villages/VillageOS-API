import { beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('./findingsApi', () => ({ findingsApi: { reduceWithTicket: vi.fn() } }));

import { findingsApi } from './findingsApi';
import { reduceUnderTicket } from './reduceUnderTicket';

const question = { thingId: 'site-1', property: 'temperature', windowSeconds: 60, steps: [] };
const answer = { Groups: [], Samples: 0, UnusableSamples: 0 };

beforeEach(() => vi.clearAllMocks());

describe('reduceUnderTicket', () => {
  it('asks without the Thing, under the ticket held, and keeps the ticket the answer hands back', async () => {
    vi.mocked(findingsApi.reduceWithTicket).mockResolvedValue({ answer, ticket: 'ticket-2' });
    const ticket = { current: 'ticket-1' as string | null };

    await expect(reduceUnderTicket(question, 'submission-1', ticket, 'no ticket')).resolves.toBe(answer);
    const { thingId: _dropped, ...asked } = question;
    expect(findingsApi.reduceWithTicket).toHaveBeenCalledWith('submission-1', 'ticket-1', asked);
    expect(ticket.current).toBe('ticket-2');
  });

  it('refuses with the page\'s words and asks nothing while no ticket is held', async () => {
    await expect(reduceUnderTicket(question, 'submission-1', { current: null }, 'no ticket')).rejects.toThrow('no ticket');
    expect(findingsApi.reduceWithTicket).not.toHaveBeenCalled();
  });
});
