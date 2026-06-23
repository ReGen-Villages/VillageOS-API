import { apiClient } from './client';
import { thingApi } from './thingApi';

/** Phloem's immediate ACK for an async spawn: the run id to animate over SSE while the DAG runs. */
export interface PipelineSpawnAccepted {
  success: boolean;
  accepted: boolean;
  runId: string;
  pipelineId: string;
}

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

  /** Spawn a run asynchronously: Phloem returns the run id immediately and runs the DAG in the background,
   * so the editor animates each node over the model SSE stream rather than blocking for the result (#5635). */
  spawnAsync: (pipelineId: string, params: Record<string, unknown> = {}) =>
    apiClient.post<PipelineSpawnAccepted>('/api/endpoints/phloem', { pipelineId, params, async: true }),

  /** Request cooperative cancellation of an in-flight run — Phloem polls this flag between dispatches. */
  cancel: (runId: string) => thingApi.setProperty(runId, 'cancelRequested', 'vos.String', 'true'),
};
