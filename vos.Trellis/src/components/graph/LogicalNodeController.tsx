import { useEffect, useRef } from 'react';
import { useSigma } from '@react-sigma/core';
import { useUiStore } from '../../stores/uiStore';
import { getLogicalChildren } from '../../utils/graphologyMapper';

/**
 * Renderless Sigma child component handling two responsibilities:
 *
 * A) **Radial positioning** — when a geo parent is expanded, positions its
 *    logical children in a circle around the parent's current (x, y).
 *
 * B) **Semantic zoom** — watches camera ratio and auto-expands nearby
 *    geo parents when zoomed in, collapses all when zoomed out.
 *
 * Must be rendered as a child of <SigmaContainer>.
 */
export function LogicalNodeController() {
  const sigma = useSigma();
  const expandedLogicalParents = useUiStore((s) => s.expandedLogicalParents);
  const semanticZoomEnabled = useUiStore((s) => s.semanticZoomEnabled);
  const lastRatioRef = useRef<number | null>(null);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // ── A) Radial positioning when parents expand ──────────────────────
  useEffect(() => {
    const graph = sigma.getGraph();

    for (const parentId of expandedLogicalParents) {
      if (!graph.hasNode(parentId)) continue;

      const children = getLogicalChildren(graph, parentId);
      if (children.length === 0) continue;

      const parentAttrs = graph.getNodeAttributes(parentId);
      const px = parentAttrs.x as number;
      const py = parentAttrs.y as number;

      // Radius scales with child count so they don't overlap
      const radius = Math.max(30, children.length * 5);
      const angleStep = (2 * Math.PI) / children.length;

      children.forEach((childId, i) => {
        if (!graph.hasNode(childId)) return;
        const angle = angleStep * i - Math.PI / 2;
        graph.setNodeAttribute(childId, 'x', px + radius * Math.cos(angle));
        graph.setNodeAttribute(childId, 'y', py + radius * Math.sin(angle));
      });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [[...expandedLogicalParents].sort().join(','), sigma]);

  // ── B) Semantic zoom ───────────────────────────────────────────────
  useEffect(() => {
    if (!semanticZoomEnabled) return;

    const camera = sigma.getCamera();

    const handler = () => {
      if (debounceRef.current) clearTimeout(debounceRef.current);
      debounceRef.current = setTimeout(() => {
        const ratio = camera.getState().ratio;
        const prev = lastRatioRef.current;
        lastRatioRef.current = ratio;

        if (prev !== null && Math.abs(ratio - prev) < 0.05) return;

        const graph = sigma.getGraph();
        const state = useUiStore.getState();

        if (ratio < 0.3) {
          const viewCenter = sigma.viewportToGraph({ x: sigma.getContainer().clientWidth / 2, y: sigma.getContainer().clientHeight / 2 });
          const gcx = viewCenter.x;
          const gcy = viewCenter.y;

          const searchRadius = 150 * ratio;
          graph.forEachNode((nodeId, attrs) => {
            if (!attrs.hasGeometry) return;
            const dx = (attrs.x as number) - gcx;
            const dy = (attrs.y as number) - gcy;
            if (dx * dx + dy * dy < searchRadius * searchRadius) {
              const children = getLogicalChildren(graph, nodeId);
              if (children.length > 0 && !state.expandedLogicalParents.has(nodeId)) {
                state.expandLogicalParent(nodeId);
              }
            }
          });
        }

        if (ratio > 1.5 && state.expandedLogicalParents.size > 0) {
          state.clearLogicalExpansions();
        }
      }, 200);
    };

    sigma.on('afterRender', handler);

    return () => {
      sigma.off('afterRender', handler);
      if (debounceRef.current) clearTimeout(debounceRef.current);
    };
  }, [sigma, semanticZoomEnabled]);

  return null;
}
