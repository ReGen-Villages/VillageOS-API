import { apiClient } from './client';

export interface NodeRunResult {
  nodeId: string;
  name: string;
  status: string;
  outputs: Record<string, unknown>;
  error: string | null;
}

export interface PipelineRunResult {
  runId: string;
  pipelineId: string;
  success: boolean;
  nodes: NodeRunResult[];
  error: string | null;
}

export const pipelineApi = {
  /** Spawn a run synchronously through Mycelium's endpoint-forward to Phloem and wait for the result. */
  spawn: (pipelineId: string, params: Record<string, unknown> = {}) =>
    apiClient.post<PipelineRunResult>('/api/endpoints/phloem', { pipelineId, params }),
};
