import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('./client', () => ({
  apiClient: {
    action: <T>(_description: string, request: () => Promise<T>) => request(),
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    del: vi.fn(),
  },
  // getByName tells a missing Thing from a failed read by the status on this, so the mock has to
  // carry the real shape rather than a stub.
  ApiError: class ApiError extends Error {
    status: number;
    constructor(status: number, body: string) {
      super(body);
      this.status = status;
    }
  },
}));

import { thingApi } from './thingApi';
import { apiClient, ApiError } from './client';

const mockGet = vi.mocked(apiClient.get);
const mockPost = vi.mocked(apiClient.post);
const mockPut = vi.mocked(apiClient.put);

beforeEach(() => {
  vi.clearAllMocks();
});

describe('thingApi.getAll narrowing', () => {
  it('asks only for the properties the model declared', async () => {
    mockGet.mockResolvedValue([]);
    await thingApi.getAll(['ifcClass', 'ifcGlobalId']);
    expect(mockGet).toHaveBeenCalledWith('/api/things?properties=ifcClass%2CifcGlobalId');
  });

  // A model that declares nothing gets everything: its properties may be the live values a dashboard
  // is watching, and deferring those helps nobody.
  it('asks for everything when the model declared nothing', async () => {
    mockGet.mockResolvedValue([]);
    await thingApi.getAll();
    expect(mockGet).toHaveBeenCalledWith('/api/things');

    await thingApi.getAll([]);
    expect(mockGet).toHaveBeenLastCalledWith('/api/things');
  });
});

describe('thingApi.getByName', () => {
  it('reads the one Thing with that name', async () => {
    mockGet.mockResolvedValue({ Id: 'settings-1', Name: 'GUI_Settings', Properties: {} });
    const thing = await thingApi.getByName('GUI_Settings');
    expect(mockGet).toHaveBeenCalledWith('/api/things?name=GUI_Settings');
    expect(thing?.Id).toBe('settings-1');
  });

  it('answers null when the model has no such Thing', async () => {
    mockGet.mockRejectedValue(new ApiError(404, 'not found'));
    await expect(thingApi.getByName('GUI_Settings')).resolves.toBeNull();
  });

  // A read that failed for any other reason is not the same as an absent Thing, and swallowing it
  // would silently load the model as though it declared nothing.
  it('lets any other failure through', async () => {
    mockGet.mockRejectedValue(new ApiError(500, 'boom'));
    await expect(thingApi.getByName('GUI_Settings')).rejects.toThrow();
  });
});

describe('thingApi.getEffectiveProperties', () => {
  // The backend renamed /effective-properties to /properties; the caller
  // must track it or the Inherited section renders nothing.
  it('requests the /properties route', async () => {
    mockGet.mockResolvedValue({});
    await thingApi.getEffectiveProperties('thing-1');
    expect(mockGet).toHaveBeenCalledWith('/api/things/thing-1/properties');
  });
});

describe('thingApi property writes', () => {
  // A Thing has two property routes, and they are not interchangeable: the update one
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

describe('thingApi.getAtInstant', () => {
  it('asks the things route for the instant, which resolves before the active filter', async () => {
    mockGet.mockResolvedValue({ Id: 'thing-1', Name: 'One', Properties: {} });
    await thingApi.getAtInstant('thing-1', '2026-09-22T09:14:03Z');
    expect(mockGet).toHaveBeenCalledWith('/api/things/thing-1?timestamp=2026-09-22T09%3A14%3A03Z', undefined);
  });

  it('answers nothing where the platform has nothing to say for that instant', async () => {
    mockGet.mockRejectedValue(new ApiError(404, 'not found'));
    await expect(thingApi.getAtInstant('thing-1', '2026-09-22T09:14:03Z')).resolves.toBeNull();
  });

  // Only a 404 is an answer. Swallowing the rest would have a card read "not recorded" for a read the
  // caller was never allowed to make, or for a platform that never replied.
  it('raises a failure that is not the platform saying it has nothing', async () => {
    mockGet.mockRejectedValue(new ApiError(500, 'the model is loading'));
    await expect(thingApi.getAtInstant('thing-1', '2026-09-22T09:14:03Z')).rejects.toThrow('the model is loading');
  });

  it('carries the values an instance held under a name its archetype declares', async () => {
    mockGet.mockResolvedValue({
      Id: 'thing-1',
      Name: 'One',
      Properties: {},
      InheritedOverrides: {
        archetype: { SourceId: 'archetype', SourceName: 'Catchment', InheritedAt: '2026-09-22T09:14:03Z', Properties: { storedCubicMetres: 306 } },
      },
    });

    const answered = await thingApi.getAtInstant('thing-1', '2026-09-22T09:14:03Z');
    expect(answered?.InheritedOverrides?.archetype.Properties.storedCubicMetres).toBe(306);
  });
});
