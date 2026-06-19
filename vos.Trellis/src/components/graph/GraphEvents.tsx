import { useEffect, useRef } from 'react';
import { useRegisterEvents, useSigma } from '@react-sigma/core';
import type Sigma from 'sigma';
import { useUiStore } from '../../stores/uiStore';
import { countLogicalChildren } from '../../utils/graphologyMapper';

/** Pixel distance threshold for detecting overlapping nodes. */
const OVERLAP_PX = 20;
/** Fan-out radius in graph units. */
const FAN_RADIUS = 50;

/** Expands the cluster only if nodeId is its collapsed representative. */
function tryExpandCluster(nodeId: string): boolean {
  const state = useUiStore.getState();
  if (!state.clusterMap || state.collapsedClusters.size === 0) return false;

  const clusterIndex = state.clusterMap.nodeCluster.get(nodeId);
  if (clusterIndex === undefined || clusterIndex === -1 || !state.collapsedClusters.has(clusterIndex)) return false;

  if (nodeId === state.clusterMap.representatives.get(clusterIndex)) {
    state.toggleClusterCollapsed(clusterIndex);
    return true;
  }
  return false;
}

function tryToggleLogicalExpansion(nodeId: string, sigma: Sigma): void {
  const state = useUiStore.getState();
  const graph = sigma.getGraph();
  if (!graph.hasNode(nodeId)) return;

  const attrs = graph.getNodeAttributes(nodeId);
  if (attrs.hasGeometry && countLogicalChildren(graph, nodeId) > 0) {
    state.toggleLogicalExpansion(nodeId);
  }
}

function fanOutOverlappingNodes(nodeId: string, sigma: Sigma): void {
  const display = sigma.getNodeDisplayData(nodeId);
  if (!display) return;

  const graph = sigma.getGraph();
  const overlapping: string[] = [];
  graph.forEachNode((n) => {
    if (n === nodeId) return;
    const d = sigma.getNodeDisplayData(n);
    if (!d || d.hidden) return;
    const dx = d.x - display.x;
    const dy = d.y - display.y;
    if (Math.sqrt(dx * dx + dy * dy) < OVERLAP_PX) {
      overlapping.push(n);
    }
  });

  if (overlapping.length === 0) return;

  const cx = graph.getNodeAttribute(nodeId, 'x') as number;
  const cy = graph.getNodeAttribute(nodeId, 'y') as number;
  const allNodes = [nodeId, ...overlapping];
  const angleStep = (2 * Math.PI) / allNodes.length;
  allNodes.forEach((id, i) => {
    const angle = angleStep * i - Math.PI / 2;
    graph.setNodeAttribute(id, 'x', cx + FAN_RADIUS * Math.cos(angle));
    graph.setNodeAttribute(id, 'y', cy + FAN_RADIUS * Math.sin(angle));
  });
}

function toContainerCoords(event: MouseEvent, container: HTMLElement) {
  const rect = container.getBoundingClientRect();
  return { x: event.clientX - rect.left, y: event.clientY - rect.top };
}

/** Wires Sigma pointer events to the UI store. Child of <SigmaContainer>. */
export function GraphEvents() {
  const sigma = useSigma();
  const registerEvents = useRegisterEvents();
  const selectNodeRef = useRef(useUiStore.getState().selectNode);
  const selectEdgeRef = useRef(useUiStore.getState().selectEdge);

  useEffect(() => {
    const unsub = useUiStore.subscribe((state) => {
      selectNodeRef.current = state.selectNode;
      selectEdgeRef.current = state.selectEdge;
    });
    return unsub;
  }, []);

  useEffect(() => {
    registerEvents({
      enterNode: (event) => useUiStore.getState().setHoveredNodeId(event.node),
      leaveNode: () => useUiStore.getState().setHoveredNodeId(null),

      clickNode: (event) => {
        if (tryExpandCluster(event.node)) return;
        tryToggleLogicalExpansion(event.node, sigma);
        fanOutOverlappingNodes(event.node, sigma);
        selectEdgeRef.current(null);
        selectNodeRef.current(event.node);
      },

      clickEdge: (event) => {
        selectNodeRef.current(null);
        selectEdgeRef.current(event.edge);
      },

      clickStage: () => {
        selectNodeRef.current(null);
        selectEdgeRef.current(null);
        const state = useUiStore.getState();
        if (state.radialMenuOpen) state.closeRadialMenu();
        if (state.nodeContextMenuOpen) state.closeNodeContextMenu();
      },

      rightClickNode: (event) => {
        event.event.original.preventDefault();
        event.preventSigmaDefault();
        useUiStore.getState().openNodeContextMenu({
          nodeId: event.node,
          position: toContainerCoords(event.event.original as MouseEvent, sigma.getContainer()),
        });
      },

      rightClickStage: (event) => {
        event.event.original.preventDefault();
        event.preventSigmaDefault();
        useUiStore.getState().openRadialMenu(
          toContainerCoords(event.event.original as MouseEvent, sigma.getContainer()),
        );
      },

      doubleClickNode: (event) => {
        event.preventSigmaDefault();
        useUiStore.getState().toggleNodeExpanded(event.node);
      },
    });
  }, [registerEvents, sigma]);

  return null;
}
