import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render } from '@testing-library/react';
import Graph from 'graphology';
import type { VosThing } from '../../types/vos';

// A real graphology graph stands in for Sigma's internal graph so reconcileGraph
// actually mutates it; loadGraph is simulated as clear + import (as Sigma does).
const liveGraph = new Graph({ multi: true, type: 'directed' });
const animatedReset = vi.fn();
const loadGraphMock = vi.fn((g: Graph) => {
  liveGraph.clear();
  liveGraph.import(g.export());
});

vi.mock('@react-sigma/core', () => ({
  useSigma: () => ({
    getGraph: () => liveGraph,
    getCamera: () => ({ animatedReset }),
  }),
  useLoadGraph: () => loadGraphMock,
}));

import { GraphDataLoader } from './GraphDataLoader';

function thing(id: string): VosThing {
  return { Id: id, Name: id, Properties: {} };
}

describe('GraphDataLoader', () => {
  beforeEach(() => {
    liveGraph.clear();
    loadGraphMock.mockClear();
    animatedReset.mockClear();
    // Fire rAF synchronously so the camera-fit assertion is deterministic.
    vi.stubGlobal('requestAnimationFrame', (cb: FrameRequestCallback) => { cb(0); return 0; });
  });

  it('does a full load and fits the camera on first load', () => {
    render(<GraphDataLoader things={[thing('t1'), thing('t2')]} relationships={[]} />);

    expect(loadGraphMock).toHaveBeenCalledTimes(1);
    expect(animatedReset).toHaveBeenCalledTimes(1);
    expect(liveGraph.order).toBe(2);
  });

  it('adds a created node incrementally without reloading or resetting the camera', () => {
    const { rerender } = render(<GraphDataLoader things={[thing('t1'), thing('t2')]} relationships={[]} />);
    expect(loadGraphMock).toHaveBeenCalledTimes(1);

    rerender(<GraphDataLoader things={[thing('t1'), thing('t2'), thing('t3')]} relationships={[]} />);

    expect(loadGraphMock).toHaveBeenCalledTimes(1); // no second full load
    expect(animatedReset).toHaveBeenCalledTimes(1); // no camera jump on create
    expect(liveGraph.hasNode('t3')).toBe(true);
    expect(liveGraph.order).toBe(3);
  });

  it('drops a deleted node incrementally without reloading or resetting the camera', () => {
    const { rerender } = render(<GraphDataLoader things={[thing('t1'), thing('t2')]} relationships={[]} />);

    rerender(<GraphDataLoader things={[thing('t1')]} relationships={[]} />);

    expect(loadGraphMock).toHaveBeenCalledTimes(1);
    expect(animatedReset).toHaveBeenCalledTimes(1);
    expect(liveGraph.hasNode('t2')).toBe(false);
    expect(liveGraph.order).toBe(1);
  });

  it('preserves a settled node position across an incremental update', () => {
    const { rerender } = render(<GraphDataLoader things={[thing('t1'), thing('t2')]} relationships={[]} />);
    // Simulate the force layout settling t1 somewhere.
    liveGraph.setNodeAttribute('t1', 'x', 123);
    liveGraph.setNodeAttribute('t1', 'y', 456);

    rerender(<GraphDataLoader things={[thing('t1'), thing('t2'), thing('t3')]} relationships={[]} />);

    expect(liveGraph.getNodeAttribute('t1', 'x')).toBe(123);
    expect(liveGraph.getNodeAttribute('t1', 'y')).toBe(456);
  });
});
