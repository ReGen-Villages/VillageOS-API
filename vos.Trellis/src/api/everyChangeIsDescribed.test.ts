import { describe, it, expect, vi, beforeEach } from 'vitest';

const described: string[] = [];

vi.mock('./client', () => ({
  apiClient: {
    action: async <T,>(description: string, request: () => Promise<T>) => {
      described.push(description);
      return request();
    },
    post: vi.fn(),
    put: vi.fn(),
    del: vi.fn(),
    get: vi.fn(),
  },
}));

import { apiClient } from './client';
import { thingApi } from './thingApi';
import { relationshipApi } from './relationshipApi';
import { modelApi } from './modelApi';
import { myceliumApi } from './myceliumApi';
import { pipelineApi } from './pipelineApi';
import { rangeApi } from './rangeApi';
import { configurationApi } from './configurationApi';

const answered = { Id: 'thing-1', Name: 'Pump', Properties: {} };

beforeEach(() => {
  described.length = 0;
  vi.clearAllMocks();
  for (const method of [apiClient.post, apiClient.put, apiClient.del]) vi.mocked(method).mockResolvedValue(answered);
});

// What a recipient reads on their phone is the description, so each change says what it did in words a
// person would use, and still sends the request it always sent.
describe('every change a person asks for is described and still sent (TC #7270)', () => {
  it.each([
    ['create a Thing', () => thingApi.create('Pump'), 'post', '/api/things', 'create Thing "Pump"'],
    ['delete a Thing', () => thingApi.remove('thing-1'), 'del', '/api/things/thing-1', 'delete Thing thing-1'],
    ['rename a Thing', () => thingApi.rename('thing-1', 'Pump'), 'put', '/api/things/thing-1/name', 'rename Thing thing-1 to "Pump"'],
    ['set a property', () => thingApi.setProperty('thing-1', 'flow', 'vos.Integer', 3), 'put', '/api/things/thing-1/properties', 'set property "flow" on Thing thing-1'],
    ['add a property', () => thingApi.addProperty('thing-1', 'flow', 'vos.Integer', 3), 'post', '/api/things/thing-1/properties', 'add property "flow" to Thing thing-1'],
    ['delete a property', () => thingApi.deleteProperty('thing-1', 'flow'), 'del', '/api/things/thing-1/properties/flow', 'delete property "flow" from Thing thing-1'],
    ['relate two Things', () => relationshipApi.create('thing-1', 'predicate-1', 'thing-2'), 'post', '/api/relationships', 'relate Thing thing-1 to Thing thing-2'],
    ['delete a relationship', () => relationshipApi.remove('relationship-1'), 'del', '/api/relationships/relationship-1', 'delete relationship relationship-1'],
    ['set a relationship property', () => relationshipApi.setProperty('relationship-1', 'flow', 'vos.Integer', 3), 'put', '/api/relationships/relationship-1/properties', 'set property "flow" on relationship relationship-1'],
    ['delete a relationship property', () => relationshipApi.deleteProperty('relationship-1', 'flow'), 'del', '/api/relationships/relationship-1/properties/flow', 'delete property "flow" from relationship relationship-1'],
    ['replace the model', () => modelApi.set('{"Things":[]}'), 'post', '/api/model', 'replace the whole model'],
    ['apply a fragment', () => modelApi.applyFragment('{"Name":"wells"}'), 'post', '/api/model/fragment', 'apply fragment "wells"'],
    ['clear the model', () => modelApi.clear(), 'del', '/api/model', 'clear the model'],
    ['start a service', () => myceliumApi.startService('service-1'), 'post', '/api/mycelium/services/service-1/start', 'start service service-1'],
    ['stop a service', () => myceliumApi.stopService('service-1'), 'post', '/api/mycelium/services/service-1/stop', 'stop service service-1'],
    ['shut the broker down', () => myceliumApi.shutdown(), 'post', '/api/mycelium/shutdown', 'shut the broker down'],
    ['load a seed', () => myceliumApi.loadSeed('wells'), 'post', '/api/mycelium/library-seeds/wells/load', 'load seed "wells"'],
    ['save a seed', () => myceliumApi.saveSeed('wells'), 'put', '/api/mycelium/library-seeds/wells', 'save the model as seed "wells"'],
    ['reload the seeds', () => myceliumApi.reloadSeeds(), 'post', '/api/mycelium/seeds/reload', 'reload the seeds'],
    ['run a pipeline', () => pipelineApi.spawn('pipeline-1'), 'post', '/api/endpoints/phloem', 'run pipeline pipeline-1'],
    ['start a pipeline', () => pipelineApi.spawnAsync('pipeline-1'), 'post', '/api/endpoints/phloem', 'start pipeline pipeline-1'],
    ['add a range', () => rangeApi.create('thing-1', { Name: 'dry', Criteria: 'level < 1' }), 'post', '/api/things/thing-1/ranges', 'add range "dry" to Thing thing-1'],
    ['set the default property mode', () => configurationApi.setDefaultPropertyMode('FullHistory'), 'put', '/api/config/property-mode', 'set the default property mode to FullHistory'],
  ] as const)('%s', async (_change, change, method, path, description) => {
    await change();

    expect(described).toEqual([description]);
    expect(vi.mocked(apiClient[method]).mock.calls[0][0]).toBe(path);
  });
});
