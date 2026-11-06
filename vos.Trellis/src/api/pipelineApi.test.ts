import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('./client', () => ({
  apiClient: {
    action: async <T,>(_description: string, request: () => Promise<T>) => request(),
    post: vi.fn(),
  },
}));

import { apiClient } from './client';
import { pipelineApi } from './pipelineApi';

beforeEach(() => vi.clearAllMocks());

describe('asking the orchestrator about a run a restart stopped', () => {
  it.each([
    ['starts it again', () => pipelineApi.startAgain('run-1'), { startAgain: 'run-1' }],
    ['carries it on', () => pipelineApi.carryOn('run-1'), { carryOn: 'run-1' }],
  ] as const)('%s by naming the run, which is all the orchestrator reads', async (_ask, ask, body) => {
    await ask();

    expect(vi.mocked(apiClient.post)).toHaveBeenCalledWith('/api/endpoints/phloem', body);
  });
});
