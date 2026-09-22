import { apiClient } from './client';
import type { EngineMetricsSummary } from '../types/engineMetrics';

export const engineMetricsApi = {
  getSummary: () => apiClient.get<EngineMetricsSummary>('/api/engines/metrics'),
};
