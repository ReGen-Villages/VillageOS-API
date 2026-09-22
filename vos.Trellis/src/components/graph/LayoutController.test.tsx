import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, render } from '@testing-library/react';
import Graph from 'graphology';

const liveGraph = new Graph({ multi: true, type: 'directed' });

vi.mock('@react-sigma/core', () => ({
  useSigma: () => ({
    getGraph: () => liveGraph,
    setSetting: vi.fn(),
    refresh: vi.fn(),
  }),
}));

interface SpawnedSupervisor {
  startCalls: number;
  killCalls: number;
}

const spawned: SpawnedSupervisor[] = [];

vi.mock('graphology-layout-forceatlas2/worker', () => ({
  default: class {
    private record: SpawnedSupervisor = { startCalls: 0, killCalls: 0 };
    constructor() {
      spawned.push(this.record);
    }
    start = () => { this.record.startCalls++; };
    stop = () => {};
    kill = () => { this.record.killCalls++; };
  },
}));

import { LayoutController } from './LayoutController';

function addNode(id: string) {
  liveGraph.addNode(id, { x: 0, y: 0, size: 5, label: id });
}

describe('LayoutController', () => {
  beforeEach(() => {
    liveGraph.clear();
    spawned.length = 0;
  });

  it('runs no layout while the graph is empty', () => {
    render(<LayoutController />);

    expect(spawned).toHaveLength(0);
  });

  it('runs the layout once the graph has nodes', () => {
    addNode('n1');

    render(<LayoutController />);

    expect(spawned).toHaveLength(1);
    expect(spawned[0].startCalls).toBeGreaterThan(0);
  });

  it('kills the layout and spawns no replacement when the graph is emptied', () => {
    addNode('n1');
    render(<LayoutController />);

    act(() => { liveGraph.clear(); });

    expect(spawned).toHaveLength(1);
    expect(spawned[0].killCalls).toBeGreaterThan(0);
  });

  it('starts a layout when nodes arrive in an empty graph', () => {
    render(<LayoutController />);
    expect(spawned).toHaveLength(0);

    act(() => { addNode('n1'); });

    expect(spawned).toHaveLength(1);
    expect(spawned[0].startCalls).toBeGreaterThan(0);
  });
});
