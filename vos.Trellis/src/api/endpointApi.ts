import { apiClient } from './client';
import type { EndpointServiceInformation } from '../types/mycelium';

export const endpointApi = {
  getAll: () => apiClient.get<EndpointServiceInformation[]>('/api/endpoints'),
};
