import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('./client', () => ({
  apiClient: {
    get: vi.fn(),
    put: vi.fn(),
  },
}));

import { configApi } from './configApi';
import { apiClient } from './client';

const mockGet = vi.mocked(apiClient.get);
const mockPut = vi.mocked(apiClient.put);

beforeEach(() => {
  vi.clearAllMocks();
});

describe('configApi.getDefaultPropertyMode', () => {
  it('gets from correct URL', async () => {
    mockGet.mockResolvedValue({ Mode: 'FullHistory' });
    const result = await configApi.getDefaultPropertyMode();
    expect(mockGet).toHaveBeenCalledWith('/api/config/property-mode');
    expect(result.Mode).toBe('FullHistory');
  });
});

describe('configApi.setDefaultPropertyMode', () => {
  it('sets mode only', async () => {
    mockPut.mockResolvedValue({ Mode: 'CurrentOnly' });
    await configApi.setDefaultPropertyMode('CurrentOnly');
    expect(mockPut).toHaveBeenCalledWith('/api/config/property-mode', { Mode: 'CurrentOnly' });
  });

  it('includes optional RingBufferSize', async () => {
    mockPut.mockResolvedValue({ Mode: 'RingBuffer', RingBufferSize: 50 });
    await configApi.setDefaultPropertyMode('RingBuffer', 50);
    expect(mockPut).toHaveBeenCalledWith('/api/config/property-mode', { Mode: 'RingBuffer', RingBufferSize: 50 });
  });

  it('includes optional SampleRate', async () => {
    mockPut.mockResolvedValue({ Mode: 'Sampled', SampleRate: 10 });
    await configApi.setDefaultPropertyMode('Sampled', undefined, 10);
    expect(mockPut).toHaveBeenCalledWith('/api/config/property-mode', { Mode: 'Sampled', SampleRate: 10 });
  });
});

describe('configApi.getPropertyMode', () => {
  it('gets per-property mode', async () => {
    mockGet.mockResolvedValue({ Mode: 'RingBuffer', RingBufferSize: 100 });
    await configApi.getPropertyMode('thing-1', 'temperature');
    expect(mockGet).toHaveBeenCalledWith('/api/things/thing-1/properties/temperature/mode');
  });

  it('encodes property name', async () => {
    mockGet.mockResolvedValue({ Mode: 'FullHistory' });
    await configApi.getPropertyMode('thing-1', 'my prop');
    expect(mockGet).toHaveBeenCalledWith('/api/things/thing-1/properties/my%20prop/mode');
  });
});

describe('configApi.setPropertyMode', () => {
  it('sets per-property mode with all options', async () => {
    mockPut.mockResolvedValue({ Mode: 'RingBuffer', RingBufferSize: 200 });
    await configApi.setPropertyMode('thing-1', 'temperature', 'RingBuffer', 200);
    expect(mockPut).toHaveBeenCalledWith(
      '/api/things/thing-1/properties/temperature/mode',
      { Mode: 'RingBuffer', RingBufferSize: 200 },
    );
  });
});
