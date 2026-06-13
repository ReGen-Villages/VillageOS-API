import { apiClient } from './client';
import type { EndpointServiceInfo } from '../types/broker';

export const endpointApi = {
  getAll: () => apiClient.get<EndpointServiceInfo[]>('/api/endpoints'),
};
