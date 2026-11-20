import { useEffect } from 'react';
import { useLoadGraph, useSigma } from '@react-sigma/core';
import type { VosThing, VosRelationship } from '@tendril-ui/tendril';
import { buildGraph } from '../../utils/graphologyMapper';
import { reconcileGraph } from '../../utils/graphReconcile';
import { extractAllGuiSettings } from '../../utils/guiSettings';
import { useUiStore } from '../../stores/uiStore';

interface Props {
  things: VosThing[];
  relationships: VosRelationship[];
}

/**
 * An empty graph, on the first load or after a clear, is loaded whole and the camera fitted. Every later
 * change, creates and deletes included, is reconciled in place, so settled node positions, the running layout
 * and the camera are left alone.
 */
export function GraphDataLoader({ things, relationships }: Props) {
  const sigma = useSigma();
  const loadGraph = useLoadGraph();

  useEffect(() => {
    const { flash, layout, predicateColors: predicateColors } = extractAllGuiSettings(things, relationships);
    useUiStore.getState().setFlashSettings(flash);
    useUiStore.getState().setLayoutSettings(layout);
    useUiStore.getState().setPredicateColors(predicateColors);

    const nextGraph = buildGraph(things, relationships, predicateColors, layout);
    const liveGraph = sigma.getGraph();

    if (liveGraph.order === 0) {
      loadGraph(nextGraph);
      if (nextGraph.order > 0) {
        requestAnimationFrame(() => {
          sigma.getCamera().animatedReset({ duration: 300 });
        });
      }
    } else {
      reconcileGraph(liveGraph, nextGraph);
    }
  }, [things, relationships, loadGraph, sigma]);

  return null;
}
