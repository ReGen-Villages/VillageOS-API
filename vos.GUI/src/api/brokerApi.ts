import { apiClient } from './client';
import type { RegisteredService, DaemonInfo } from '../types/broker';

export interface SeedStatus {
  IsLoading: boolean;
  CurrentFile: string;
  Phase: string;
  ThingsLoaded: number;
  RelationshipsLoaded: number;
}

export const brokerApi = {
  /** Unauthenticated — check seed loading progress before login completes. */
  getSeedStatus: async (): Promise<SeedStatus> => {
    const baseUrl = import.meta.env.VITE_BROKER_URL || '';
    const res = await fetch(`${baseUrl}/api/broker/seed-status`);
    if (!res.ok) throw new Error('Failed to fetch seed status');
    return res.json();
  },
  getServices: () => apiClient.get<RegisteredService[]>('/api/broker/services'),

  getService: (id: string) => apiClient.get<RegisteredService>(`/api/broker/services/${id}`),

  startService: (id: string) =>
    apiClient.post<{ message: string }>(`/api/broker/services/${id}/start`),

  stopService: (id: string) =>
    apiClient.post<{ message: string }>(`/api/broker/services/${id}/stop`),

  getDaemons: () => apiClient.get<DaemonInfo[]>('/api/broker/daemons'),

  stopDaemon: (key: string) =>
    apiClient.post<{ message: string }>(`/api/broker/daemons/${encodeURIComponent(key)}/stop`),

  shutdown: () => apiClient.post<{ message: string }>('/api/broker/shutdown'),

  getLibrarySeeds: () =>
    apiClient.get<{ name: string; sizeMb: number }[]>('/api/broker/library-seeds'),

  loadSeed: (name: string) =>
    apiClient.post<{ message: string; modelId: string; modelName: string }>(`/api/broker/library-seeds/${encodeURIComponent(name)}/load`),

  saveSeed: (name: string) =>
    apiClient.put<{ message: string; name: string; sizeMb: number }>(
      `/api/broker/library-seeds/${encodeURIComponent(name)}`,
      {},
    ),
};
