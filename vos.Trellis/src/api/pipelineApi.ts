import { apiClient } from './client';

/** Answered at once with the run's identifier, which the editor follows over the model stream. */
interface PipelineSpawnAccepted {
  success: boolean;
  accepted: boolean;
  runId: string;
  pipelineId: string;
}

interface NodeRunResult {
  nodeId: string;
  name: string;
  status: string;
  outputs: Record<string, unknown>;
  error: string | null;
}

interface PipelineRunResult {
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

  /** Phloem answers at once and runs the pipeline behind, so the editor shows each step over the model
   *  stream rather than waiting for the result. */
  spawnAsync: (pipelineId: string, parameters: Record<string, unknown> = {}) =>
    apiClient.action(`start pipeline ${pipelineId}`, () =>
      apiClient.post<PipelineSpawnAccepted>('/api/endpoints/phloem', { pipelineId, parameters, async: true }),
    ),

  startAgain: (runId: string) =>
    apiClient.action(`start run ${runId} again`, () =>
      apiClient.post<{ runId: string }>('/api/endpoints/phloem', { startAgain: runId }),
    ),

  carryOn: (runId: string) =>
    apiClient.action(`carry run ${runId} on`, () =>
      apiClient.post<{ runId: string }>('/api/endpoints/phloem', { carryOn: runId }),
    ),
};
