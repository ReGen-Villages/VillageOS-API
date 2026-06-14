import { apiClient } from './client';
import type { EndpointServiceInfo } from '../types/mycelium';

export const endpointApi = {
  getAll: () => apiClient.get<EndpointServiceInfo[]>('/api/endpoints'),
};
