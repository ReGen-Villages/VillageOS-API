import { apiClient } from './client';
import type { ThingsInStateResponse } from '../types/vos';

export const stateApi = {
  getThingsInState: (stateName: string) =>
    apiClient.get<ThingsInStateResponse>(`/api/states/${encodeURIComponent(stateName)}/things`),
};
