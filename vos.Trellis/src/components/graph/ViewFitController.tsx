import { useEffect } from 'react';
import { useSigma } from '@react-sigma/core';

// Measured on a model of tens of thousands of nodes: one layout step can throw out close to a hundred
// of them, while a settled drawing trimmed by this share is fitted within about five percent of every node.
const TRIMMED_SHARE_AT_EACH_EDGE = 0.005;

const POSITION_EVENTS = [
  'nodeAdded',
  'nodeDropped',
  'cleared',
  'nodeAttributesUpdated',
  'eachNodeAttributesUpdated',
] as const;

/** Sorts both arrays in place. */
function trimmedExtent(xs: Float32Array, ys: Float32Array): { x: [number, number]; y: [number, number] } {
  const trimmed = Math.floor(xs.length * TRIMMED_SHARE_AT_EACH_EDGE);
  const last = xs.length - 1 - trimmed;
  xs.sort();
  ys.sort();
  return { x: [xs[trimmed], xs[last]], y: [ys[trimmed], ys[last]] };
}

/**
 * Fits the view to the extent of nearly every node rather than every node. The force layout
 * occasionally throws a few nodes far out for a second or two; fitted to every node, Sigma shrinks
 * the whole drawing to make room for them.
 *
 * Must be rendered as a child of <SigmaContainer>.
 */
export function ViewFitController() {
  const sigma = useSigma();

  useEffect(() => {
    const graph = sigma.getGraph();
    let xs = new Float32Array(0);
    let ys = new Float32Array(0);
    let positionsChanged = true;

    const markPositionsChanged = () => { positionsChanged = true; };

    // Before the render, not on each graph event: one layout step moves every node, and Sigma
    // reprocesses once per render however many events led to it.
    const fitView = () => {
      if (!positionsChanged) return;
      positionsChanged = false;

      if (graph.order === 0) {
        sigma.setCustomBBox(null);
        return;
      }

      if (xs.length < graph.order) {
        xs = new Float32Array(graph.order);
        ys = new Float32Array(graph.order);
      }
      const nodeXs = xs.subarray(0, graph.order);
      const nodeYs = ys.subarray(0, graph.order);
      let index = 0;
      graph.forEachNode((_, attributes) => {
        nodeXs[index] = attributes.x;
        nodeYs[index] = attributes.y;
        index++;
      });

      sigma.setCustomBBox(trimmedExtent(nodeXs, nodeYs));
    };

    for (const event of POSITION_EVENTS) graph.on(event, markPositionsChanged);
    sigma.on('beforeRender', fitView);

    return () => {
      for (const event of POSITION_EVENTS) graph.off(event, markPositionsChanged);
      sigma.off('beforeRender', fitView);
    };
  }, [sigma]);

  return null;
}
