import { apiClient } from './client';
import type { PropertyModeConfiguration } from '../types/vos';

export const configurationApi = {
  getDefaultPropertyMode: () =>
    apiClient.get<PropertyModeConfiguration>('/api/config/property-mode'),

  setDefaultPropertyMode: (
    mode: string,
    ringBufferSize?: number,
    sampleRate?: number,
    sampleSeconds?: number,
    fullHistoryInMemory?: { versions?: number; seconds?: number },
  ) =>
    apiClient.action(`set the default property mode to ${mode}`, () =>
      apiClient.put<PropertyModeConfiguration>('/api/config/property-mode', {
        Mode: mode,
        ...(ringBufferSize != null && { RingBufferSize: ringBufferSize }),
        ...(sampleRate != null && { SampleRate: sampleRate }),
        ...(sampleSeconds != null && { SampleSeconds: sampleSeconds }),
        ...(fullHistoryInMemory?.versions != null && { FullHistoryVersionsInMemory: fullHistoryInMemory.versions }),
        ...(fullHistoryInMemory?.seconds != null && { FullHistorySecondsInMemory: fullHistoryInMemory.seconds }),
      }),
    ),

  getPropertyMode: (thingId: string, propertyName: string) =>
    apiClient.get<PropertyModeConfiguration>(
      `/api/things/${thingId}/properties/${encodeURIComponent(propertyName)}/mode`,
    ),

  setPropertyMode: (
    thingId: string,
    propertyName: string,
    mode: string,
    ringBufferSize?: number,
    sampleRate?: number,
    sampleSeconds?: number,
  ) =>
    apiClient.action(`set the mode of property "${propertyName}" on Thing ${thingId} to ${mode}`, () =>
      apiClient.put<PropertyModeConfiguration>(
        `/api/things/${thingId}/properties/${encodeURIComponent(propertyName)}/mode`,
        {
          Mode: mode,
          ...(ringBufferSize != null && { RingBufferSize: ringBufferSize }),
          ...(sampleRate != null && { SampleRate: sampleRate }),
          ...(sampleSeconds != null && { SampleSeconds: sampleSeconds }),
        },
      ),
    ),
};
