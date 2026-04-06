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
 * Loads a graphology Graph into Sigma whenever the source data changes.
 * Must be rendered as a child of <SigmaContainer>.
 *
 * Distinguishes structural changes (nodes/edges added or removed) from
 * property-only changes.  Full `loadGraph()` only runs for structural
 * changes; property changes update Sigma node attributes in-place to
 * avoid destroying the layout, resetting the camera, or triggering
 * MapLibre coordinate re-sync.
 */
export function GraphDataLoader({ things, relationships }: Props) {
  const sigma = useSigma();
  const loadGraph = useLoadGraph();
  const prevThingIdsRef = useRef<Set<string>>(new Set());
  const prevRelIdsRef = useRef<Set<string>>(new Set());

  useEffect(() => {
    // Extract GUI settings from the "GUI Settings" Thing (single traversal)
    const { flash, layout, predicateColors: predColors } = extractAllGuiSettings(things, relationships);
    useUiStore.getState().setFlashSettings(flash);
    useUiStore.getState().setLayoutSettings(layout);
    useUiStore.getState().setPredicateColors(predColors);

    // Build the graphology graph from domain data
    const graph = buildGraph(things, relationships, predColors);

    // Determine whether this is a structural change
    const newThingIds = new Set(things.map((t) => t.Id));
    const newRelIds = new Set(relationships.map((r) => r.Id));
    const prevThingIds = prevThingIdsRef.current;
    const prevRelIds = prevRelIdsRef.current;

    const countChanged =
      newThingIds.size !== prevThingIds.size ||
      newRelIds.size !== prevRelIds.size;

    const isStructural =
      prevThingIds.size === 0 || // first load
      countChanged ||
      [...newThingIds].some((id) => !prevThingIds.has(id)) ||
      [...prevThingIds].some((id) => !newThingIds.has(id)) ||
      [...newRelIds].some((id) => !prevRelIds.has(id)) ||
      [...prevRelIds].some((id) => !newRelIds.has(id));

    prevThingIdsRef.current = newThingIds;
    prevRelIdsRef.current = newRelIds;

    if (isStructural) {
      // Structural change — full reload
      loadGraph(graph);

      if (countChanged) {
        requestAnimationFrame(() => {
          sigma.getCamera().animatedReset({ duration: 300 });
        });
      }
    } else {
      // Property-only change — update attributes in-place.
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
