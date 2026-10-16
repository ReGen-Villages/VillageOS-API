import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('../api/thingApi', () => ({
  thingApi: {
    create: vi.fn().mockResolvedValue({ Id: 'request-1' }),
    setProperty: vi.fn().mockResolvedValue({}),
  },
}));
vi.mock('../api/relationshipApi', () => ({
  relationshipApi: { create: vi.fn().mockResolvedValue({ Id: 'new-rel' }) },
}));

import type { VosThing } from '../types/vos';
import { PipelineModel, ROLE } from './model';
import { playing, withRoles } from './roles.test.fixture';
import { stopRun } from './stop';
import { thingApi } from '../api/thingApi';
import { relationshipApi } from '../api/relationshipApi';

function model(bindsStopping: boolean): PipelineModel {
  const things: VosThing[] = [{ Id: 'is', Name: 'is', Properties: {} }];
  if (bindsStopping) {
    things.push({ Id: 'kind-stop', Name: 'Halt', Properties: playing(ROLE.StopRequest) });
    things.push({ Id: 'conn-stops', Name: 'halts', Properties: playing(ROLE.RunStopping) });
  }
  const [held, heldRelationships] = withRoles(things, []);
  return new PipelineModel(held, heldRelationships);
}

describe('stopping a run from the page', () => {
  beforeEach(() => vi.clearAllMocks());

  it('writes a stop request of the bound kind with its reason, then relates it to the run along the bound connection', async () => {
    await stopRun('run-9', 'Cancelled from the Pipelines page', model(true));

    expect(vi.mocked(relationshipApi.create).mock.calls).toEqual([
      ['request-1', 'is', 'kind-stop'],
      ['request-1', 'conn-stops', 'run-9'],
    ]);
    expect(vi.mocked(thingApi.setProperty)).toHaveBeenCalledWith('request-1', 'reason', 'vos.String', 'Cancelled from the Pipelines page');
    const reasonWritten = vi.mocked(thingApi.setProperty).mock.invocationCallOrder[0];
    const relatedToTheRun = vi.mocked(relationshipApi.create).mock.invocationCallOrder[1];
    expect(reasonWritten).toBeLessThan(relatedToTheRun);
  });

  it('refuses a model that binds nothing to stopping runs, writing nothing', async () => {
    await expect(stopRun('run-9', 'reason', model(false))).rejects.toThrow();

    expect(thingApi.create).not.toHaveBeenCalled();
    expect(relationshipApi.create).not.toHaveBeenCalled();
  });
});
