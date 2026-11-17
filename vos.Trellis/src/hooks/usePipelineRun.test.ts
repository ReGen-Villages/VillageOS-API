import { describe, it, expect, vi } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import type { Node } from '@xyflow/react';

vi.mock('../api/pipelineApi', () => ({ pipelineApi: { spawnAsync: vi.fn(), cancel: vi.fn() } }));
vi.mock('../pipeline/stop', () => ({ stopRun: vi.fn() }));

import { PipelineModel, ROLE } from '../pipeline/model';
import { catalystFixture } from '../pipeline/catalysts.test.fixture';
import { pipelineApi } from '../api/pipelineApi';
import { stopRun } from '../pipeline/stop';
import { usePipelineRun } from './usePipelineRun';

const node = (id: string, label: string): Node => ({ id, position: { x: 0, y: 0 }, data: { label, ports: [] } });

describe('usePipelineRun', () => {
  it('runs nothing before the pipeline is saved, and cancels nothing before a run is chosen', async () => {
    const { model } = catalystFixture();
    const { result } = renderHook(() => usePipelineRun({
      model, savedId: null, nodes: [], setNodes: vi.fn(), thingIdByCanvasId: {}, setError: vi.fn(),
    }));

    await act(() => result.current.onRun());
    await act(() => result.current.onCancel());

    expect(vi.mocked(pipelineApi.spawnAsync)).not.toHaveBeenCalled();
    expect(vi.mocked(stopRun)).not.toHaveBeenCalled();
  });

  it('paints the status the run holds for a node, and leaves a node the run holds nothing for as it was', () => {
    const { things, relationships, id } = catalystFixture();
    const relate = (subject: string, predicate: string, target: string) =>
      relationships.push({ Id: `${subject}-${predicate}-${target}`, Name: '', SubjectId: subject, PredicateId: predicate, TargetId: target, Properties: {} });
    things.push(
      { Id: 'of', Name: 'of', Properties: {} },
      { Id: 'run-kind', Name: 'Execution', IsArchetype: true, Properties: {} },
      { Id: ROLE.PipelineRun, Name: 'pipelineRun role', Properties: {} },
      { Id: 'held-up', Name: 'run 3', Properties: { status: 'waiting', startedUtc: '2026-10-15T09:00:00Z' } },
      { Id: 'held-up-at-the-end', Name: 'The office is told run', Properties: { nodeId: id.digestEnd, status: 'waiting' } },
    );
    relate(ROLE.PipelineRun, id.binds, 'run-kind');
    relate('held-up', id.is, 'run-kind');
    relate('held-up', 'of', id.digest);
    relate('held-up', id.has, 'held-up-at-the-end');
    const model = new PipelineModel(things, relationships);
    const nodes = [node(id.digestEnd, 'The office is told'), node('untouched', 'A node the run never reached')];
    const setNodes = vi.fn();
    const { result } = renderHook(() => usePipelineRun({
      model, savedId: id.digest, nodes, setNodes, thingIdByCanvasId: {}, setError: vi.fn(),
    }));

    act(() => result.current.onSelectRun('held-up'));
    const paint = setNodes.mock.calls.at(-1)![0] as (current: Node[]) => Node[];
    const painted = paint(nodes);

    expect(painted[0].data.status).toBe('waiting');
    expect(painted[1]).toBe(nodes[1]);
  });
});
