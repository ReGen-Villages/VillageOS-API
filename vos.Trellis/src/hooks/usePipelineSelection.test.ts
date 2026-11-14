import { describe, it, expect } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import type { Edge, Node } from '@xyflow/react';
import { catalystFixture } from '../pipeline/catalysts.test.fixture';
import { usePipelineSelection } from './usePipelineSelection';

const node = (id: string, ports: { portId: string; portName: string }[], choices: { id: string; name: string }[] = []): Node =>
  ({ id, position: { x: 0, y: 0 }, data: { label: id, ports: ports.map((port) => ({ ...port, direction: 'out', type: 'any', required: false })), choices } });

describe('usePipelineSelection', () => {
  it('names a selected wire’s ports, offers the results it may be taken on, and closes its inspector', () => {
    const { model, id } = catalystFixture();
    const nodes = [
      node('checker', [{ portId: 'checker.out', portName: 'verdict' }], [{ id: 'too-high', name: 'Too high' }]),
      node('recorder', [{ portId: 'recorder.in', portName: 'level' }]),
    ];
    const edges: Edge[] = [{ id: 'wire', source: 'checker', sourceHandle: 'checker.out', target: 'recorder', targetHandle: 'recorder.in' }];
    const { result } = renderHook(() => usePipelineSelection({ model, nodes, edges, outputs: [], currentNodes: () => nodes }));

    act(() => result.current.selectEdge('wire'));

    expect(result.current.selectedEdge?.id).toBe('wire');
    expect(result.current.portNameOf('checker', 'checker.out')).toBe('verdict');
    expect(result.current.portNameOf('recorder', 'not-a-port')).toBe('');
    expect(result.current.resultsOf('checker')).toEqual([
      { id: id.success, name: 'success' },
      { id: id.fail, name: 'fail' },
      { id: 'too-high', name: 'Too high' },
    ]);

    act(() => result.current.closeWireInspector());
    expect(result.current.selectedEdge).toBeUndefined();
  });

  it('selecting a node lets go of the selected wire, and the other way round', () => {
    const { model } = catalystFixture();
    const nodes = [node('checker', [])];
    const edges: Edge[] = [{ id: 'wire', source: 'checker', target: 'checker' }];
    const { result } = renderHook(() => usePipelineSelection({ model, nodes, edges, outputs: [], currentNodes: () => nodes }));

    act(() => result.current.selectEdge('wire'));
    act(() => result.current.selectNode('checker'));
    expect(result.current.selectedEdge).toBeUndefined();
    expect(result.current.selectedNode?.id).toBe('checker');

    act(() => result.current.selectEdge('wire'));
    expect(result.current.selectedNode).toBeUndefined();
  });
});
