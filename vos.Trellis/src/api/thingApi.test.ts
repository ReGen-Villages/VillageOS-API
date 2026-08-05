import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('./client', () => ({
  apiClient: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    del: vi.fn(),
  },
}));

import { thingApi } from './thingApi';
import { apiClient } from './client';

const mockGet = vi.mocked(apiClient.get);
const mockPost = vi.mocked(apiClient.post);
const mockPut = vi.mocked(apiClient.put);

beforeEach(() => {
  vi.clearAllMocks();
});

describe('thingApi.getEffectiveProperties', () => {
  // Bug #5908 — the backend renamed /effective-properties to /properties (cb6e84e); the caller
  // must track it or the Inherited section renders nothing.
  it('requests the /properties route', async () => {
    mockGet.mockResolvedValue({});
    await thingApi.getEffectiveProperties('thing-1');
    expect(mockGet).toHaveBeenCalledWith('/api/things/thing-1/properties');
  });
});

describe('thingApi property writes', () => {
  // Bug #6141 — a Thing has two property routes, and they are not interchangeable: the update one
  // answers "Property does not exist on the thing" for a name it has never seen. Adding through it
  // fails every time.
  it('adds through the create route, not the update route', async () => {
    mockPost.mockResolvedValue({ Id: 'thing-1', Name: 'One', Properties: {} });
    await thingApi.addProperty('thing-1', 'door_number', 'vos.String', '12');
    expect(mockPost).toHaveBeenCalledWith('/api/things/thing-1/properties', {
      Name: 'door_number',
      Type: 'vos.String',
      Value: '12',
    });
    expect(mockPut).not.toHaveBeenCalled();
  });

  it('updates through the update route', async () => {
    mockPut.mockResolvedValue({ Id: 'thing-1', Name: 'One', Properties: {} });
    await thingApi.setProperty('thing-1', 'door_number', 'vos.String', '13');
    expect(mockPut).toHaveBeenCalledWith('/api/things/thing-1/properties', {
      Name: 'door_number',
      Type: 'vos.String',
      Value: '13',
    });
    expect(mockPost).not.toHaveBeenCalled();
  });
});
