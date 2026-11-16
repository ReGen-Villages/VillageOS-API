import { describe, it, expect, vi, beforeEach } from 'vitest';
import type { ModelReads } from './modelReads';
import { postToEndpoint } from './dashboardWrites';

const fromService = vi.fn();
const reads = { fromService } as unknown as ModelReads;

describe('postToEndpoint', () => {
  beforeEach(() => {
    fromService.mockReset();
  });

  it('posts the body to the endpoint the specification names, through the context’s port, and hands back what it said', async () => {
    fromService.mockResolvedValue({ said: 'taken' });

    expect(await postToEndpoint(reads, 'readings', { view: 'book' })).toEqual({ said: 'taken' });
    expect(fromService).toHaveBeenCalledWith('/api/endpoints/readings', { view: 'book' });
  });

  it('posts to a platform route as written where the specification names one by its path', async () => {
    fromService.mockResolvedValue({ said: 'granted' });

    await postToEndpoint(reads, '/api/auth/administration', { view: 'grant' });

    expect(fromService).toHaveBeenCalledWith('/api/auth/administration', { view: 'grant' });
  });

  it('hands a refusal back as the endpoint’s own words rather than throwing', async () => {
    fromService.mockRejectedValue(new Error('already booked'));

    expect(await postToEndpoint(reads, 'readings', {})).toEqual({ error: 'already booked' });
  });

  describe('with the application wording refusals', () => {
    const wordRefusal = vi.fn();

    beforeEach(() => {
      wordRefusal.mockReset();
    });

    it('words a refusal from the endpoint’s whole answer', async () => {
      fromService.mockResolvedValue({ error: 'refused', rolesNeeded: ['steward'] });
      wordRefusal.mockReturnValue('Needs the steward role');

      expect(await postToEndpoint(reads, 'readings', {}, wordRefusal)).toEqual({ error: 'Needs the steward role', rolesNeeded: ['steward'] });
      expect(wordRefusal).toHaveBeenCalledWith({ error: 'refused', rolesNeeded: ['steward'] });
    });

    it('words a refusal the port threw from its message', async () => {
      fromService.mockRejectedValue(new Error('already booked'));
      wordRefusal.mockReturnValue('Someone else booked it first');

      expect(await postToEndpoint(reads, 'readings', {}, wordRefusal)).toEqual({ error: 'Someone else booked it first' });
      expect(wordRefusal).toHaveBeenCalledWith({ error: 'already booked' });
    });

    it('keeps the endpoint’s own words where the application has none for this refusal', async () => {
      fromService.mockResolvedValue({ error: 'no sample on record' });
      wordRefusal.mockReturnValue(undefined);

      expect(await postToEndpoint(reads, 'readings', {}, wordRefusal)).toEqual({ error: 'no sample on record' });
    });

    it('is not asked about a press that was taken', async () => {
      fromService.mockResolvedValue({ said: 'taken' });

      await postToEndpoint(reads, 'readings', {}, wordRefusal);

      expect(wordRefusal).not.toHaveBeenCalled();
    });
  });

  it('reads an empty answer as a press taken with nothing said', async () => {
    fromService.mockResolvedValue(null);

    expect(await postToEndpoint(reads, 'readings', {})).toEqual({});
  });

  it('reports a press as where it went and whether it was taken, never what was in it', async () => {
    const recordAction = vi.fn();
    fromService.mockResolvedValueOnce({ said: 'taken' }).mockRejectedValueOnce(new Error('already booked'));
    const reporting = { fromService, recordAction } as unknown as ModelReads;

    await postToEndpoint(reporting, 'readings', { email: 'someone@example.org' });
    await postToEndpoint(reporting, 'readings', { email: 'someone@example.org' });

    expect(recordAction.mock.calls).toEqual([
      ['send a form to readings', true],
      ['send a form to readings', false],
    ]);
  });
});
