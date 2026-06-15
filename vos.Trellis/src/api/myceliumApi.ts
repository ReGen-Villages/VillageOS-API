import { apiClient } from './client';
import type { RegisteredService, DaemonInfo } from '../types/mycelium';

export interface StartupProgress {
  IsLoading: boolean;
  CurrentFile: string;
  Phase: string;
  ThingsLoaded: number;
  RelationshipsLoaded: number;
}

export const myceliumApi = {
  /** Unauthenticated — check seed loading progress before login completes. */
  getStartupStatus: async (): Promise<StartupProgress> => {
    const baseUrl = import.meta.env.VITE_BROKER_URL || '';
    const res = await fetch(`${baseUrl}/api/mycelium/startup-status`);
    if (!res.ok) throw new Error('Failed to fetch seed status');
    return res.json();
  },
  getServices: () => apiClient.get<RegisteredService[]>('/api/mycelium/services'),

  getService: (id: string) => apiClient.get<RegisteredService>(`/api/mycelium/services/${id}`),

  startService: (id: string) =>
    apiClient.post<{ message: string }>(`/api/mycelium/services/${id}/start`),

  stopService: (id: string) =>
    apiClient.post<{ message: string }>(`/api/mycelium/services/${id}/stop`),

  getDaemons: () => apiClient.get<DaemonInfo[]>('/api/mycelium/daemons'),

  stopDaemon: (key: string) =>
    apiClient.post<{ message: string }>(`/api/mycelium/daemons/${encodeURIComponent(key)}/stop`),

  shutdown: () => apiClient.post<{ message: string }>('/api/mycelium/shutdown'),

  getLibrarySeeds: () =>
    apiClient.get<{ name: string; sizeMb: number }[]>('/api/mycelium/library-seeds'),

  loadSeed: (name: string) =>
    apiClient.post<{ message: string; modelId: string; modelName: string }>(`/api/mycelium/library-seeds/${encodeURIComponent(name)}/load`),

  saveSeed: (name: string) =>
    apiClient.put<{ message: string; name: string; sizeMb: number }>(
      `/api/mycelium/library-seeds/${encodeURIComponent(name)}`,
      {},
    ),

  reloadSeeds: () =>
    apiClient.post<{ message: string }>('/api/mycelium/seeds/reload'),
};
