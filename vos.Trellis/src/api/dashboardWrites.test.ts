import { describe, it, expect, vi, beforeEach } from 'vitest';
import type { ModelReads } from './modelReads';
import { postToEndpoint } from './dashboardWrites';

const fromService = vi.fn();
const reads = { fromService } as unknown as ModelReads;

describe('postToEndpoint', () => {
  beforeEach(() => {
    fromService.mockReset();
  });

  it('posts the body to the endpoint the spec names, through the context’s port, and hands back what it said', async () => {
    fromService.mockResolvedValue({ said: 'taken' });

    expect(await postToEndpoint(reads, 'readings', { view: 'book' })).toEqual({ said: 'taken' });
    expect(fromService).toHaveBeenCalledWith('/api/endpoints/readings', { view: 'book' });
  });

  it('posts to a platform route as written where the spec names one by its path', async () => {
    fromService.mockResolvedValue({ said: 'granted' });

    await postToEndpoint(reads, '/api/auth/administration', { view: 'grant' });

    expect(fromService).toHaveBeenCalledWith('/api/auth/administration', { view: 'grant' });
  });

  it('hands a refusal back as the endpoint’s own words rather than throwing', async () => {
    fromService.mockRejectedValue(new Error('already booked'));

    expect(await postToEndpoint(reads, 'readings', {})).toEqual({ error: 'already booked' });
  });

  it('reads an empty answer as a press taken with nothing said', async () => {
    fromService.mockResolvedValue(null);

    expect(await postToEndpoint(reads, 'readings', {})).toEqual({});
  });

  it('reports a press as where it went and whether it was taken, never what was in it (TC #7270)', async () => {
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
