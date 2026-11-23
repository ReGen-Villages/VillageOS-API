import { apiClient } from './client';
import type { RegisteredService } from '../types/mycelium';
import i18n from '../i18n';

export interface StartupProgress {
  IsLoading: boolean;
  CurrentFile: string;
  Phase: string;
  ThingsLoaded: number;
  RelationshipsLoaded: number;
}

/** A page the platform declares for the signed-in caller: a dashboard specification about the
 *  platform itself rather than about a model, drawn exactly as a model's own `Dashboard` Thing. */
interface DeclaredPage {
  name: string;
  specification: unknown;
}

export const myceliumApi = {
  getPages: () => apiClient.get<DeclaredPage[]>('/api/mycelium/pages'),

  /** Asked before sign-in, so it carries no credential. */
  getStartupStatus: async (): Promise<StartupProgress> => {
    const baseUrl = import.meta.env.VITE_BROKER_URL || '';
    const res = await fetch(`${baseUrl}/api/mycelium/startup-status`);
    if (!res.ok) throw new Error(i18n.t('authentication.seedStatusFailed'));
    return res.json();
  },
  getServices: () => apiClient.get<RegisteredService[]>('/api/mycelium/services'),

  startService: (id: string) =>
    apiClient.action(`start service ${id}`, () => apiClient.post<{ message: string }>(`/api/mycelium/services/${id}/start`)),

  stopService: (id: string) =>
    apiClient.action(`stop service ${id}`, () => apiClient.post<{ message: string }>(`/api/mycelium/services/${id}/stop`)),

  shutdown: () => apiClient.action('shut the broker down', () => apiClient.post<{ message: string }>('/api/mycelium/shutdown')),

  getLibrarySeeds: () =>
    apiClient.get<{ name: string; sizeMb: number }[]>('/api/mycelium/library-seeds'),

  loadSeed: (name: string) =>
    apiClient.action(`load seed "${name}"`, () =>
      apiClient.post<{ message: string; modelId: string; modelName: string }>(
        `/api/mycelium/library-seeds/${encodeURIComponent(name)}/load`,
      ),
    ),

  saveSeed: (name: string) =>
    apiClient.action(`save the model as seed "${name}"`, () =>
      apiClient.put<{ message: string; name: string; sizeMb: number }>(
        `/api/mycelium/library-seeds/${encodeURIComponent(name)}`,
        {},
      ),
    ),

  reloadSeeds: () =>
    apiClient.action('reload the seeds', () => apiClient.post<{ message: string }>('/api/mycelium/seeds/reload')),
};
