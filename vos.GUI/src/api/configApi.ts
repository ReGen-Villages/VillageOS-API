import { apiClient } from './client';
import type { PropertyModeConfig } from '../types/vos';

export const configApi = {
  getDefaultPropertyMode: () =>
    apiClient.get<PropertyModeConfig>('/api/config/property-mode'),

  setDefaultPropertyMode: (mode: string, ringBufferSize?: number, sampleRate?: number) =>
    apiClient.put<PropertyModeConfig>('/api/config/property-mode', {
      Mode: mode,
      ...(ringBufferSize != null && { RingBufferSize: ringBufferSize }),
      ...(sampleRate != null && { SampleRate: sampleRate }),
    }),

  getPropertyMode: (thingId: string, propertyName: string) =>
    apiClient.get<PropertyModeConfig>(
      `/api/things/${thingId}/properties/${encodeURIComponent(propertyName)}/mode`,
    ),

  setPropertyMode: (
    thingId: string,
    propertyName: string,
    mode: string,
    ringBufferSize?: number,
    sampleRate?: number,
  ) =>
    apiClient.put<PropertyModeConfig>(
      `/api/things/${thingId}/properties/${encodeURIComponent(propertyName)}/mode`,
      {
        Mode: mode,
        ...(ringBufferSize != null && { RingBufferSize: ringBufferSize }),
        ...(sampleRate != null && { SampleRate: sampleRate }),
      },
    ),
};
