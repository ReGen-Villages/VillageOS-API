import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('./client', () => ({
  apiClient: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
  },
}));

import { myceliumApi } from './myceliumApi';
import { apiClient } from './client';

const mockGet = vi.mocked(apiClient.get);
const mockPost = vi.mocked(apiClient.post);
const mockPut = vi.mocked(apiClient.put);

beforeEach(() => {
  vi.clearAllMocks();
});

describe('myceliumApi.getServices', () => {
  it('gets from correct URL', async () => {
    mockGet.mockResolvedValue([]);
    await myceliumApi.getServices();
    expect(mockGet).toHaveBeenCalledWith('/api/mycelium/services');
  });
});

describe('myceliumApi.startService', () => {
  it('posts to correct URL', async () => {
    mockPost.mockResolvedValue({ message: 'ok' });
    await myceliumApi.startService('svc-1');
    expect(mockPost).toHaveBeenCalledWith('/api/mycelium/services/svc-1/start');
  });
});

describe('myceliumApi.stopService', () => {
  it('posts to correct URL', async () => {
    mockPost.mockResolvedValue({ message: 'ok' });
    await myceliumApi.stopService('svc-1');
    expect(mockPost).toHaveBeenCalledWith('/api/mycelium/services/svc-1/stop');
  });
});

describe('myceliumApi.reloadSeeds', () => {
  it('posts to correct URL', async () => {
    mockPost.mockResolvedValue({ message: 'Seeds reloaded' });
    const result = await myceliumApi.reloadSeeds();
    expect(mockPost).toHaveBeenCalledWith('/api/mycelium/seeds/reload');
    expect(result.message).toBe('Seeds reloaded');
  });
});

describe('myceliumApi.saveSeed', () => {
  it('encodes seed name', async () => {
    mockPut.mockResolvedValue({ message: 'ok', name: 'my seed', sizeMb: 1.5 });
    await myceliumApi.saveSeed('my seed');
    expect(mockPut).toHaveBeenCalledWith('/api/mycelium/library-seeds/my%20seed', {});
  });
});

describe('myceliumApi.shutdown', () => {
  it('posts shutdown', async () => {
    mockPost.mockResolvedValue({ message: 'Shutting down' });
    await myceliumApi.shutdown();
    expect(mockPost).toHaveBeenCalledWith('/api/mycelium/shutdown');
  });
});
