import { useEffect, useRef } from 'react';
import { useSigma } from '@react-sigma/core';
import { useUiStore } from '../../stores/uiStore';
import { useModelStore } from '../../stores/modelStore';
import type { VosRelationship } from '../../types/vos';
import { hashStringToIndex, LOGICAL_PALETTE, resolvePredicateColor } from '../../utils/colors';
import { computeOrbitPosition, ORBIT_RADIUS_CONTAINMENT, CONTAINMENT_NAMES } from '../../utils/nodeVisibility';

/** Max containment children to inject (avoids overwhelming WebGL). */
const MAX_CHILDREN = 50;

/**
 * On-demand containment expansion for map mode.
 *
 * When a geo node is selected, this component finds its `contains`/`aggregates`
 * children in the full model store data, injects them as temporary nodes + edges
 * into the graphology graph, and removes them on deselection.
 *
 * Must be rendered as a child of <SigmaContainer>, BEFORE <NodeReducer>.
 */
export function ContainmentExpander() {
  const sigma = useSigma();
  const selectedNodeId = useUiStore((s) => s.selectedNodeId);
  const mapEnabled = useUiStore((s) => s.mapEnabled);
  const predicateColors = useUiStore((s) => s.predicateColors);
  const things = useModelStore((s) => s.things);
  const relationships = useModelStore((s) => s.relationships);

  /** IDs of nodes/edges we added — for cleanup. */
  const addedNodesRef = useRef<Set<string>>(new Set());
  const addedEdgesRef = useRef<Set<string>>(new Set());

  useEffect(() => {
    const graph = sigma.getGraph();
    const addedNodes = addedNodesRef.current;
    const addedEdges = addedEdgesRef.current;

    // ── Cleanup previous expansion ────────────────────────────────────
    for (const edgeId of addedEdges) {
      if (graph.hasEdge(edgeId)) graph.dropEdge(edgeId);
    }
    addedEdges.clear();
    for (const nodeId of addedNodes) {
      if (graph.hasNode(nodeId)) graph.dropNode(nodeId);
    }
    addedNodes.clear();

    if (!mapEnabled || !selectedNodeId || !graph.hasNode(selectedNodeId)) return;

    const selAttrs = graph.getNodeAttributes(selectedNodeId);
    if (!selAttrs.hasGeometry) return;

    // ── Identify containment predicate IDs ────────────────────────────
    const thingMap = new Map(things.map((t) => [t.Id, t]));
    const containsPredicateIds = new Set<string>();
    for (const t of things) {
      if (CONTAINMENT_NAMES.has(t.Name.toLowerCase())) {
        containsPredicateIds.add(t.Id);
      }
    }
    if (containsPredicateIds.size === 0) return;

    // Build "is" type name index for consistent color derivation with buildGraph
    const isPredicateIds = new Set<string>();
    for (const t of things) {
      if (t.Name.toLowerCase() === 'is') isPredicateIds.add(t.Id);
    }
    const isTypeNameMap = new Map<string, string>();
    for (const r of relationships) {
      if (isPredicateIds.has(r.PredicateId)) {
        const target = thingMap.get(r.TargetId);
        if (target) isTypeNameMap.set(r.SubjectId, target.Name);
      }
    }

    // ── Find containment relationships touching the selected node ─────
    // Check both directions:
    //   outgoing: selectedNode --[contains]--> child  (selected is container)
    //   incoming: parent --[contains]--> selectedNode  (selected is contained)
    const childRels: VosRelationship[] = [];
    const parentRels: VosRelationship[] = [];
    for (const r of relationships) {
      if (!containsPredicateIds.has(r.PredicateId)) continue;
      if (r.SubjectId === selectedNodeId) childRels.push(r);
      if (r.TargetId === selectedNodeId) parentRels.push(r);
    }
    if (childRels.length === 0 && parentRels.length === 0) return;

    // Selected node position for orbital placement
    const anchorX = selAttrs.x as number;
    const anchorY = selAttrs.y as number;
    const anchorLat = selAttrs.lat as number;
    const anchorLng = selAttrs.lng as number;

    // Collect all rels to inject (children capped, parents always included)
    childRels.sort((a, b) => {
      const na = thingMap.get(a.TargetId)?.Name || '';
      const nb = thingMap.get(b.TargetId)?.Name || '';
      return na.localeCompare(nb);
    });
    const allRels = [...parentRels, ...childRels.slice(0, MAX_CHILDREN)];

    // ── Inject nodes ──────────────────────────────────────────────────
    const injectedIds: string[] = [];
    for (const r of allRels) {
      // The "other" node is the one not already in the graph
      const otherId = r.SubjectId === selectedNodeId ? r.TargetId : r.SubjectId;
      const otherThing = thingMap.get(otherId);
      if (!otherThing) continue;
      if (graph.hasNode(otherId)) continue; // already in graph

      // Derive color from "is" type name (consistent with buildGraph)
      const typeName = isTypeNameMap.get(otherId);
      const palette = LOGICAL_PALETTE;
      const color = typeName
        ? palette[hashStringToIndex(typeName, palette.length)]
        : palette[hashStringToIndex(otherThing.Name, palette.length)];

      // All containment-expanded nodes are treated as non-geo/logical —
      // they orbit the selected node to show the relationship, not their
      // actual geographic position (which could be far away for building
      // storeys, levels, etc.).
      graph.addNode(otherId, {
        x: anchorX,
        y: anchorY,
        size: 3,
        color,
        label: otherThing.Name,
        thingType: 'default',
        hasGeometry: false,
        hidden: false,
        isLogical: true,
      });
      addedNodes.add(otherId);
      injectedIds.push(otherId);
    }

    // ── Inject containment edges ──────────────────────────────────────
    for (const r of allRels) {
      if (!graph.hasNode(r.SubjectId) || !graph.hasNode(r.TargetId)) continue;
      if (graph.hasEdge(r.Id)) continue;
      const predName = thingMap.get(r.PredicateId)?.Name || '';
      const edgeColor = resolvePredicateColor(predName, predicateColors);
      graph.addDirectedEdgeWithKey(r.Id, r.SubjectId, r.TargetId, {
        size: 1,
        color: edgeColor,
        label: predName,
        type: 'arrow',
        predicateId: r.PredicateId,
      });
      addedEdges.add(r.Id);
    }

    // ── Position all injected nodes orbitally ───────────────────────────
    if (injectedIds.length > 0 && typeof anchorLat === 'number') {
      injectedIds.forEach((id, i) => {
        const pos = computeOrbitPosition(anchorLat, anchorLng, i, injectedIds.length, ORBIT_RADIUS_CONTAINMENT);
        graph.mergeNodeAttributes(id, {
          x: anchorX,
          y: anchorY,
          _orbitLat: pos.lat,
          _orbitLng: pos.lng,
        });
      });
    }

  }, [mapEnabled, selectedNodeId, sigma, things, relationships, predicateColors]);

  return null;
}
