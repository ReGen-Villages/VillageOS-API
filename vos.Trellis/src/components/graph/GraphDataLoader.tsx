import { useEffect } from 'react';
import { useLoadGraph, useSigma } from '@react-sigma/core';
import type { VosThing, VosRelationship } from '../../types/vos';
import { buildGraph } from '../../utils/graphologyMapper';
import { reconcileGraph } from '../../utils/graphReconcile';
import { extractAllGuiSettings } from '../../utils/guiSettings';
import { useUiStore } from '../../stores/uiStore';

interface Props {
  things: VosThing[];
  relationships: VosRelationship[];
}

/**
 * Syncs the Sigma graph with the model store on every data change.
 * The first load (empty graph) does a full loadGraph() and fits the camera;
 * every later change — including creates and deletes — is reconciled in place,
 * which preserves settled node positions and never resets the camera.
 */
export function GraphDataLoader({ things, relationships }: Props) {
  const sigma = useSigma();
  const loadGraph = useLoadGraph();

  useEffect(() => {
    const { flash, layout, predicateColors: predColors } = extractAllGuiSettings(things, relationships);
    useUiStore.getState().setFlashSettings(flash);
    useUiStore.getState().setLayoutSettings(layout);
    useUiStore.getState().setPredicateColors(predColors);

    // classColorOverrides is empty today (future GUI_Settings panel populates it).
    const nextGraph = buildGraph(things, relationships, predColors, {}, layout);
    const liveGraph = sigma.getGraph();

    // An empty live graph means first mount or a post-clear repopulate: do a full
    // load and fit. Otherwise reconcile incrementally so the running layout and
    // camera are left undisturbed.
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
