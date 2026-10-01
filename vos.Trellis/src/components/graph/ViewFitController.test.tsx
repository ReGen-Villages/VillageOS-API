import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render } from '@testing-library/react';
import Graph from 'graphology';

const liveGraph = new Graph({ multi: true, type: 'directed' });
const sigmaListeners = new Map<string, () => void>();
const setCustomBBox = vi.fn();

vi.mock('@react-sigma/core', () => ({
  useSigma: () => ({
    getGraph: () => liveGraph,
    on: (event: string, listener: () => void) => { sigmaListeners.set(event, listener); },
    off: (event: string) => { sigmaListeners.delete(event); },
    setCustomBBox,
  }),
}));

import { ViewFitController } from './ViewFitController';

function renderFrame() {
  sigmaListeners.get('beforeRender')?.();
}

function lastFittedBox() {
  return setCustomBBox.mock.calls[setCustomBBox.mock.calls.length - 1][0];
}

function addSquareOfNodes(perSide: number, halfWidth: number) {
  for (let row = 0; row < perSide; row++) {
    for (let column = 0; column < perSide; column++) {
      liveGraph.addNode(`n${row}-${column}`, {
        x: -halfWidth + (2 * halfWidth * column) / (perSide - 1),
        y: -halfWidth + (2 * halfWidth * row) / (perSide - 1),
      });
    }
  }
}

describe('ViewFitController', () => {
  beforeEach(() => {
    liveGraph.clear();
    sigmaListeners.clear();
    setCustomBBox.mockClear();
  });

  it('fits the view to the drawing, not to a few nodes the layout has thrown far out', () => {
    addSquareOfNodes(40, 100);
    liveGraph.addNode('thrown-left', { x: -340, y: 0 });
    liveGraph.addNode('thrown-right', { x: 340, y: 20 });
    liveGraph.addNode('thrown-up', { x: 10, y: 340 });

    render(<ViewFitController />);
    renderFrame();

    expect(lastFittedBox()).toEqual({ x: [-100, 100], y: [-100, 100] });
  });

  it('fits a graph too small to trim to every node, as Sigma does', () => {
    addSquareOfNodes(5, 100);
    liveGraph.addNode('far', { x: 340, y: 0 });

    render(<ViewFitController />);
    renderFrame();

    expect(lastFittedBox()).toEqual({ x: [-100, 340], y: [-100, 100] });
  });

  it('measures again only after a node moves', () => {
    addSquareOfNodes(5, 100);
    render(<ViewFitController />);

    renderFrame();
    renderFrame();
    expect(setCustomBBox).toHaveBeenCalledTimes(1);

    liveGraph.setNodeAttribute('n0-0', 'x', -200);
    renderFrame();

    expect(setCustomBBox).toHaveBeenCalledTimes(2);
    expect(lastFittedBox().x).toEqual([-200, 100]);
  });

  it('measures again after the layout moves every node at once', () => {
    addSquareOfNodes(5, 100);
    render(<ViewFitController />);
    renderFrame();

    liveGraph.updateEachNodeAttributes((_, attributes) => ({ ...attributes, x: attributes.x * 2 }));
    renderFrame();

    expect(lastFittedBox().x).toEqual([-200, 200]);
  });

  it('hands the fit back to Sigma when the graph empties', () => {
    addSquareOfNodes(5, 100);
    render(<ViewFitController />);
    renderFrame();

    liveGraph.clear();
    renderFrame();

    expect(lastFittedBox()).toBeNull();
  });
});
