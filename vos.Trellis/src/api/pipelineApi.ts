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
  spawn: (pipelineId: string, parameters: Record<string, unknown> = {}) =>
    apiClient.action(`run pipeline ${pipelineId}`, () =>
      apiClient.post<PipelineRunResult>('/api/endpoints/phloem', { pipelineId, parameters }),
    ),

  /** Spawn a run asynchronously: Phloem returns the run id immediately and runs the DAG in the background,
   * so the editor animates each node over the model SSE stream rather than blocking for the result. */
  spawnAsync: (pipelineId: string, parameters: Record<string, unknown> = {}) =>
    apiClient.action(`start pipeline ${pipelineId}`, () =>
      apiClient.post<PipelineSpawnAccepted>('/api/endpoints/phloem', { pipelineId, parameters, async: true }),
    ),

  /** Request cooperative cancellation of an in-flight run — Phloem polls this flag between dispatches. */
  cancel: (runId: string) => thingApi.setProperty(runId, 'cancelRequested', 'vos.String', 'true'),
};
