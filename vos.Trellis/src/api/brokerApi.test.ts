import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('./client', () => ({
  apiClient: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
  },
}));

import { brokerApi } from './brokerApi';
import { apiClient } from './client';

const mockGet = vi.mocked(apiClient.get);
const mockPost = vi.mocked(apiClient.post);
const mockPut = vi.mocked(apiClient.put);

beforeEach(() => {
  vi.clearAllMocks();
});

describe('brokerApi.getServices', () => {
  it('gets from correct URL', async () => {
    mockGet.mockResolvedValue([]);
    await brokerApi.getServices();
    expect(mockGet).toHaveBeenCalledWith('/api/broker/services');
  });
});

describe('brokerApi.startService', () => {
  it('posts to correct URL', async () => {
    mockPost.mockResolvedValue({ message: 'ok' });
    await brokerApi.startService('svc-1');
    expect(mockPost).toHaveBeenCalledWith('/api/broker/services/svc-1/start');
  });
});

describe('brokerApi.stopService', () => {
  it('posts to correct URL', async () => {
    mockPost.mockResolvedValue({ message: 'ok' });
    await brokerApi.stopService('svc-1');
    expect(mockPost).toHaveBeenCalledWith('/api/broker/services/svc-1/stop');
  });
});

describe('brokerApi.reloadSeeds', () => {
  it('posts to correct URL', async () => {
    mockPost.mockResolvedValue({ message: 'Seeds reloaded' });
    const result = await brokerApi.reloadSeeds();
    expect(mockPost).toHaveBeenCalledWith('/api/broker/seeds/reload');
    expect(result.message).toBe('Seeds reloaded');
  });
});

describe('brokerApi.saveSeed', () => {
  it('encodes seed name', async () => {
    mockPut.mockResolvedValue({ message: 'ok', name: 'my seed', sizeMb: 1.5 });
    await brokerApi.saveSeed('my seed');
    expect(mockPut).toHaveBeenCalledWith('/api/broker/library-seeds/my%20seed', {});
  });
});

describe('brokerApi.shutdown', () => {
  it('posts shutdown', async () => {
    mockPost.mockResolvedValue({ message: 'Shutting down' });
    await brokerApi.shutdown();
    expect(mockPost).toHaveBeenCalledWith('/api/broker/shutdown');
  });
});
