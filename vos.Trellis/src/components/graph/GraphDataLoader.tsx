import { useEffect, useRef } from 'react';
import { useLoadGraph, useSigma } from '@react-sigma/core';
import type { VosThing, VosRelationship } from '../../types/vos';
import { buildGraph } from '../../utils/graphologyMapper';
import { extractAllGuiSettings } from '../../utils/guiSettings';
import { useUiStore } from '../../stores/uiStore';

interface Props {
  things: VosThing[];
  relationships: VosRelationship[];
}

/**
 * Loads a graphology Graph into Sigma when source data changes.
 * Property-only changes update node attributes in-place rather than calling
 * loadGraph(), to avoid destroying the layout or resetting the camera.
 */
export function GraphDataLoader({ things, relationships }: Props) {
  const sigma = useSigma();
  const loadGraph = useLoadGraph();
  const prevThingIdsRef = useRef<Set<string>>(new Set());
  const prevRelIdsRef = useRef<Set<string>>(new Set());

  useEffect(() => {
    const { flash, layout, predicateColors: predColors } = extractAllGuiSettings(things, relationships);
    useUiStore.getState().setFlashSettings(flash);
    useUiStore.getState().setLayoutSettings(layout);
    useUiStore.getState().setPredicateColors(predColors);

    // classColorOverrides is empty today (future GUI_Settings panel populates it).
    const graph = buildGraph(things, relationships, predColors, {}, layout);

    const newThingIds = new Set(things.map((t) => t.Id));
    const newRelIds = new Set(relationships.map((r) => r.Id));
    const prevThingIds = prevThingIdsRef.current;
    const prevRelIds = prevRelIdsRef.current;

    const countChanged =
      newThingIds.size !== prevThingIds.size ||
      newRelIds.size !== prevRelIds.size;

    const isStructural =
      prevThingIds.size === 0 ||
      countChanged ||
      [...newThingIds].some((id) => !prevThingIds.has(id)) ||
      [...prevThingIds].some((id) => !newThingIds.has(id)) ||
      [...newRelIds].some((id) => !prevRelIds.has(id)) ||
      [...prevRelIds].some((id) => !newRelIds.has(id));

    prevThingIdsRef.current = newThingIds;
    prevRelIdsRef.current = newRelIds;

    if (isStructural) {
      loadGraph(graph);

      if (countChanged) {
        requestAnimationFrame(() => {
          sigma.getCamera().animatedReset({ duration: 300 });
        });
      }
    } else {
      // Skip positional attrs (x, y) to avoid disrupting the layout.
      const currentGraph = sigma.getGraph();
      graph.forEachNode((nodeId, attrs) => {
        if (currentGraph.hasNode(nodeId)) {
          for (const [key, value] of Object.entries(attrs)) {
            if (key !== 'x' && key !== 'y') {
              currentGraph.setNodeAttribute(nodeId, key, value);
            }
          }
        }
      });
    }
  }, [things, relationships, loadGraph, sigma]);

  return null;
}
