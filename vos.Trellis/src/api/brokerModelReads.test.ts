import { describe, it, expect, vi, beforeEach } from 'vitest';
import type { ThingRangesResponse, ThingsInStateResponse } from '../types/vos';

// Only the calls are stubbed. The request builder these share by lives in stateQuery and is left
// alone: the shared-request key is the path a narrowed read asks for, so a stubbed builder would
// prove sharing that the running client does not do.
vi.mock('./stateApi', () => ({ stateApi: { getThingsInState: vi.fn() } }));

vi.mock('./rangeApi', () => ({ rangeApi: { getAll: vi.fn() } }));
vi.mock('./client', () => ({ apiClient: { post: vi.fn() } }));

import { stateApi } from './stateApi';
import { rangeApi } from './rangeApi';
import { apiClient } from './client';
import { brokerModelReads } from './brokerModelReads';

const answered = (Things: { Id: string; Name: string }[]) =>
  ({ StateName: 'flagged', Things }) as ThingsInStateResponse;

const noRanges = { ThingId: 'study1', ThingName: 'Study', OwnRanges: [], InheritedRanges: [] } as ThingRangesResponse;

describe('what one refresh generation asks the broker', () => {
  beforeEach(() => {
    vi.mocked(stateApi.getThingsInState).mockReset().mockResolvedValue(answered([{ Id: 'b1', Name: 'BLD-1' }]));
    vi.mocked(rangeApi.getAll).mockReset().mockResolvedValue(noRanges);
    vi.mocked(apiClient.post).mockReset().mockResolvedValue({ reservoirs: 3 });
  });

  // A table and the figure beside it read the same list from the same service, and a service
  // answers each by walking what it holds — so asking twice a refresh doubles that walk for one
  // answer. The question is the endpoint and what is sent to it, never the path picked out of the
  // answer: two widgets reading different parts of one reply are still one question.
  it('asks a service one question once, however many widgets ask it', async () => {
    const reads = brokerModelReads();

    const answers = await Promise.all([
      reads.fromService('/api/endpoints/reservoirs', { scope: 'site1' }),
      reads.fromService('/api/endpoints/reservoirs', { scope: 'site1' }),
    ]);

    expect(apiClient.post).toHaveBeenCalledTimes(1);
    expect(answers[1]).toEqual({ reservoirs: 3 });
  });

  it('asks again when the same service is sent a different body', async () => {
    const reads = brokerModelReads();

    await Promise.all([
      reads.fromService('/api/endpoints/reservoirs', { scope: 'site1' }),
      reads.fromService('/api/endpoints/reservoirs', { scope: null }),
    ]);

    expect(apiClient.post).toHaveBeenCalledTimes(2);
  });

  it('asks again once an answer has landed, so a press through the same port is never answered by an earlier one', async () => {
    const reads = brokerModelReads();

    await reads.fromService('/api/endpoints/readings', { view: 'book' });
    await reads.fromService('/api/endpoints/readings', { view: 'book' });

    expect(apiClient.post).toHaveBeenCalledTimes(2);
  });

  it('drops a refused service read rather than sharing it', async () => {
    vi.mocked(apiClient.post).mockRejectedValueOnce(new Error('service unreachable'));
    const reads = brokerModelReads();

    await expect(reads.fromService('/api/endpoints/reservoirs', {})).rejects.toThrow();
    expect(await reads.fromService('/api/endpoints/reservoirs', {})).toEqual({ reservoirs: 3 });
    expect(apiClient.post).toHaveBeenCalledTimes(2);
  });

  it('asks once for a state however many widgets want it', async () => {
    const reads = brokerModelReads();

    await Promise.all([reads.thingsInState('flagged'), reads.thingsInState('flagged')]);

    expect(stateApi.getThingsInState).toHaveBeenCalledTimes(1);
  });

  // Two widgets narrowing one state differently must not be handed each other's answer, which would
  // put a wrong number on screen with nothing to say so.
  it('asks again when the same state is narrowed differently', async () => {
    const reads = brokerModelReads();

    await reads.thingsInState('flagged', { type: 'Building' });
    await reads.thingsInState('flagged', { type: 'Ridge' });

    expect(stateApi.getThingsInState).toHaveBeenCalledTimes(2);
  });

  // One broker hiccup would otherwise stick to every later reader of this generation, with nothing
  // to retry it until the next refresh.
  it('drops a failed state read rather than sharing it', async () => {
    const reads = brokerModelReads();
    vi.mocked(stateApi.getThingsInState).mockRejectedValueOnce(new Error('broker unreachable'));

    await expect(reads.thingsInState('flagged')).rejects.toThrow();
    await expect(reads.thingsInState('flagged')).resolves.toEqual(answered([{ Id: 'b1', Name: 'BLD-1' }]));
    expect(stateApi.getThingsInState).toHaveBeenCalledTimes(2);
  });

  // A study's judge-ranges sit on its archetype, so several verdict rows on one page ask about one
  // Thing and would otherwise each fetch the same answer.
  it('asks for one Thing\'s ranges once', async () => {
    const reads = brokerModelReads();

    await Promise.all([reads.thingRanges('study1'), reads.thingRanges('study1')]);

    expect(rangeApi.getAll).toHaveBeenCalledTimes(1);
  });

  // A verdict the model holds still reads, without the target it names.
  it('answers nothing for ranges it could not read, rather than refusing', async () => {
    const reads = brokerModelReads();
    vi.mocked(rangeApi.getAll).mockRejectedValue(new Error('broker unreachable'));

    await expect(reads.thingRanges('study1')).resolves.toBeNull();
  });

  // Each generation is discarded when the model moves on. A map outliving one would serve a
  // membership the model has since moved past.
  it('shares nothing with the generation before it', async () => {
    await brokerModelReads().thingsInState('flagged');
    await brokerModelReads().thingsInState('flagged');

    expect(stateApi.getThingsInState).toHaveBeenCalledTimes(2);
  });
});
