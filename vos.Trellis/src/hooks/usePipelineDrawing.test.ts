import { describe, it, expect, vi } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { catalystFixture } from '../pipeline/catalysts.test.fixture';
import type { OutputRow } from '../pipeline/catalysts';
import type { PipelineNodeData } from '../components/pipeline/PipelineNodeView';
import { usePipelineDrawing } from './usePipelineDrawing';

function drawing() {
  const { model, id } = catalystFixture();
  const setError = vi.fn();
  const { result } = renderHook(() => usePipelineDrawing({ model, connections: model.connections(), setError }));
  return { result, setError, id };
}

const STOP = { kind: 'stop' } as OutputRow;
const portOf = (data: unknown, portName: string) => (data as PipelineNodeData).ports.find((port) => port.portName === portName)!.portId;

describe('usePipelineDrawing', () => {
  it('records an undo step at the start of a drag but keeps the saved pipeline, and undoing the drag marks it unsaved', () => {
    const { result, id } = drawing();
    act(() => { result.current.load(id.readingsArrive); });
    expect(result.current.savedId).toBe(id.readingsArrive);

    act(() => result.current.onNodeDragStart());
    expect(result.current.canUndo).toBe(true);
    expect(result.current.dirty).toBe(true);
    expect(result.current.savedId).toBe(id.readingsArrive);

    act(() => result.current.onUndo());
    expect(result.current.savedId).toBeNull();
  });

  it('records an undo step before a node or a wire is deleted, and marks the canvas unsaved', () => {
    const { result, id } = drawing();
    act(() => { result.current.load(id.readingsArrive); });
    act(() => result.current.onNodesDelete());
    expect(result.current.canUndo).toBe(true);
    expect(result.current.savedId).toBeNull();

    act(() => { result.current.load(id.readingsArrive); });
    act(() => result.current.onEdgesDelete());
    expect(result.current.canUndo).toBe(true);
    expect(result.current.savedId).toBeNull();
  });

  it('draws a wire between compatible ports, refuses one between incompatible ports saying why, and ignores one to nowhere', () => {
    const { result, setError } = drawing();
    act(() => result.current.placeStart(undefined));
    act(() => result.current.placeOutput(STOP));
    act(() => result.current.placeOutput(STOP));
    const [start, firstStop, secondStop] = result.current.nodes;

    act(() => result.current.onConnect({ source: 'nowhere', sourceHandle: null, target: firstStop.id, targetHandle: null }));
    expect(result.current.edges).toHaveLength(0);
    expect(setError).not.toHaveBeenCalled();

    act(() => result.current.onConnect({
      source: firstStop.id, sourceHandle: portOf(firstStop.data, 'requested'),
      target: secondStop.id, targetHandle: portOf(secondStop.data, 'reason'),
    }));
    expect(result.current.edges).toHaveLength(0);
    expect(setError).toHaveBeenLastCalledWith('Incompatible wire: number → string');

    act(() => result.current.onConnect({
      source: start.id, sourceHandle: portOf(start.data, 'payload'),
      target: firstStop.id, targetHandle: portOf(firstStop.data, 'things'),
    }));
    expect(result.current.edges).toHaveLength(1);
    expect(setError).toHaveBeenLastCalledWith(null);
    expect(result.current.savedId).toBeNull();
  });

  it('sets and clears a wire’s paths and what it is taken on, each as an undo step', () => {
    const { result, id } = drawing();
    act(() => { result.current.load(id.readingsArrive); });
    const wireId = result.current.edges[0].id;

    act(() => result.current.setEdgePath(wireId, 'fromPath', 'reading.level'));
    expect(result.current.edges[0].data?.fromPath).toBe('reading.level');
    expect(result.current.savedId).toBeNull();
    act(() => result.current.setEdgePath(wireId, 'fromPath', ''));
    expect(result.current.edges[0].data?.fromPath).toBeUndefined();

    act(() => result.current.setEdgeTakenOn(wireId, [id.success]));
    expect(result.current.edges[0].data?.takenOn).toEqual([id.success]);
    expect(result.current.canUndo).toBe(true);
  });

  it('leaves the canvas as it is when asked to open a pipeline the model does not hold', () => {
    const { result, id } = drawing();
    act(() => { result.current.load(id.readingsArrive); });
    let opened = true;
    act(() => { opened = result.current.load('not-in-the-model'); });
    expect(opened).toBe(false);
    expect(result.current.savedId).toBe(id.readingsArrive);
    expect(result.current.nodes).toHaveLength(2);
  });
});
