import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('./client', () => ({
  apiClient: {
    action: <T>(_description: string, request: () => Promise<T>) => request(),
    get: vi.fn(),
    put: vi.fn(),
  },
}));

import { configurationApi } from './configurationApi';
import { apiClient } from './client';

const mockGet = vi.mocked(apiClient.get);
const mockPut = vi.mocked(apiClient.put);

beforeEach(() => {
  vi.clearAllMocks();
});

describe('configurationApi.getDefaultPropertyMode', () => {
  it('gets from correct URL', async () => {
    mockGet.mockResolvedValue({ Mode: 'FullHistory' });
    const result = await configurationApi.getDefaultPropertyMode();
    expect(mockGet).toHaveBeenCalledWith('/api/config/property-mode');
    expect(result.Mode).toBe('FullHistory');
  });
});

describe('configurationApi.setDefaultPropertyMode', () => {
  it('sets mode only', async () => {
    mockPut.mockResolvedValue({ Mode: 'CurrentOnly' });
    await configurationApi.setDefaultPropertyMode('CurrentOnly');
    expect(mockPut).toHaveBeenCalledWith('/api/config/property-mode', { Mode: 'CurrentOnly' });
  });

  it('includes optional RingBufferSize', async () => {
    mockPut.mockResolvedValue({ Mode: 'RingBuffer', RingBufferSize: 50 });
    await configurationApi.setDefaultPropertyMode('RingBuffer', 50);
    expect(mockPut).toHaveBeenCalledWith('/api/config/property-mode', { Mode: 'RingBuffer', RingBufferSize: 50 });
  });

  it('includes optional SampleRate', async () => {
    mockPut.mockResolvedValue({ Mode: 'SampledByObservations', SampleRate: 10 });
    await configurationApi.setDefaultPropertyMode('SampledByObservations', undefined, 10);
    expect(mockPut).toHaveBeenCalledWith('/api/config/property-mode', { Mode: 'SampledByObservations', SampleRate: 10 });
  });

  it('includes optional SampleSeconds', async () => {
    mockPut.mockResolvedValue({ Mode: 'SampledByTime', SampleSeconds: 60 });
    await configurationApi.setDefaultPropertyMode('SampledByTime', undefined, undefined, 60);
    expect(mockPut).toHaveBeenCalledWith('/api/config/property-mode', { Mode: 'SampledByTime', SampleSeconds: 60 });
  });

  it('includes the limits on full-history versions kept in memory, a zero among them', async () => {
    mockPut.mockResolvedValue({ Mode: 'FullHistory' });
    await configurationApi.setDefaultPropertyMode('FullHistory', undefined, undefined, undefined, { versions: 10, seconds: 0 });
    expect(mockPut).toHaveBeenCalledWith('/api/config/property-mode', {
      Mode: 'FullHistory', FullHistoryVersionsInMemory: 10, FullHistorySecondsInMemory: 0,
    });
  });

  it('leaves out a limit it was not given', async () => {
    mockPut.mockResolvedValue({ Mode: 'FullHistory' });
    await configurationApi.setDefaultPropertyMode('FullHistory', undefined, undefined, undefined, { seconds: 600 });
    expect(mockPut).toHaveBeenCalledWith('/api/config/property-mode', { Mode: 'FullHistory', FullHistorySecondsInMemory: 600 });
  });
});

describe('configurationApi.getPropertyMode', () => {
  it('gets per-property mode', async () => {
    mockGet.mockResolvedValue({ Mode: 'RingBuffer', RingBufferSize: 100 });
    await configurationApi.getPropertyMode('thing-1', 'temperature');
    expect(mockGet).toHaveBeenCalledWith('/api/things/thing-1/properties/temperature/mode');
  });

  it('encodes property name', async () => {
    mockGet.mockResolvedValue({ Mode: 'FullHistory' });
    await configurationApi.getPropertyMode('thing-1', 'my prop');
    expect(mockGet).toHaveBeenCalledWith('/api/things/thing-1/properties/my%20prop/mode');
  });
});

describe('configurationApi.setPropertyMode', () => {
  it('sets per-property mode with all options', async () => {
    mockPut.mockResolvedValue({ Mode: 'RingBuffer', RingBufferSize: 200 });
    await configurationApi.setPropertyMode('thing-1', 'temperature', 'RingBuffer', 200);
    expect(mockPut).toHaveBeenCalledWith(
      '/api/things/thing-1/properties/temperature/mode',
      { Mode: 'RingBuffer', RingBufferSize: 200 },
    );
  });
});
