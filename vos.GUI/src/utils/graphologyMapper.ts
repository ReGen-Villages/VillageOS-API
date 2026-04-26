import Graph from 'graphology';
import type { VosThing, VosRelationship } from '../types/vos';
import {
  ROLE_COLORS,
  INSTANCE_PALETTE,
  LOGICAL_PALETTE,
  hashStringToIndex,
  resolvePredicateColor,
} from './colors';
import { computeNodeSize } from './nodeSize';
import { LAYOUT_DEFAULTS, type LayoutSettings } from './guiSettings';

// ── Relationship index (O(n+m) instead of O(n*m)) ────────────────────

interface RelationshipIndex {
  /** Set of all thing IDs used as predicates in any relationship. */
  predicateIds: Set<string>;
  /** Set of thing IDs that are targets of "is" relationships (type things). */
  isTypeTargets: Set<string>;
  /** Map from subject ID → target name for "is" relationships (instance type). */
  isSubjectToTypeName: Map<string, string>;
}

export function buildRelationshipIndex(
  relationships: VosRelationship[],
  thingMap: Map<string, VosThing>,
): RelationshipIndex {
  const predicateIds = new Set<string>();
  const isTypeTargets = new Set<string>();
  const isSubjectToTypeName = new Map<string, string>();

  for (const r of relationships) {
    predicateIds.add(r.PredicateId);
    const pred = thingMap.get(r.PredicateId);
    if (pred && pred.Name.toLowerCase() === 'is') {
      isTypeTargets.add(r.TargetId);
      if (!isSubjectToTypeName.has(r.SubjectId)) {
        const targetName = thingMap.get(r.TargetId)?.Name;
        if (targetName) isSubjectToTypeName.set(r.SubjectId, targetName);
      }
    }
  }

  return { predicateIds, isTypeTargets, isSubjectToTypeName };
}

// ── Classification helpers ─────────────────────────────────────────────

export function getThingType(
  thing: VosThing,
  index: RelationshipIndex,
): 'predicate' | 'type' | 'default' {
  if (
    thing.Properties &&
    ('ExecutablePath' in thing.Properties || 'ServicePort' in thing.Properties)
  ) {
    return 'predicate';
  }
  if (index.predicateIds.has(thing.Id)) return 'predicate';
  if (index.isTypeTargets.has(thing.Id)) return 'type';
  return 'default';
}

function getInstanceTypeName(
  thing: VosThing,
  index: RelationshipIndex,
): string | null {
  return index.isSubjectToTypeName.get(thing.Id) ?? null;
}

// ── Node attribute types ───────────────────────────────────────────────

interface GraphNodeAttrs {
  x: number;
  y: number;
  size: number;
  color: string;
  label: string;
  thingType: 'predicate' | 'type' | 'default';
  hasGeometry: boolean;
  lat?: number;
  lng?: number;
  hidden: boolean;
  isLogical: boolean;
  parentGeoNodeId?: string;
  // sigma uses `type` for renderer selection – keep it generic for now
  [key: string]: unknown;
}

interface GraphEdgeAttrs {
  size: number;
  color: string;
  label: string;
  type: string; // 'arrow'
  predicateId: string;
  [key: string]: unknown;
}

// ── Build graph ────────────────────────────────────────────────────────

export function buildGraph(
  things: VosThing[],
  relationships: VosRelationship[],
  predicateColors: Record<string, string> = {},
  layoutSettings: LayoutSettings = LAYOUT_DEFAULTS,
): Graph {
  const sizeOpts = {
    min: layoutSettings.nodeSizeMin,
    max: layoutSettings.nodeSizeMax,
    slope: layoutSettings.nodeSizeSlope,
  };
  const edgeSize = layoutSettings.edgeSize;
  const graph = new Graph({ multi: true, type: 'directed' });
  const thingMap = new Map(things.map((t) => [t.Id, t]));

  // Pre-compute incoming relationship counts (node size ∝ how many things point at it)
  const relCounts = new Map<string, number>();
  for (const r of relationships) {
    relCounts.set(r.TargetId, (relCounts.get(r.TargetId) || 0) + 1);
  }

  // Build predicate colour map — explicit overrides first, hash fallback
  const predicateColorMap = new Map<string, string>();
  for (const r of relationships) {
    if (predicateColorMap.has(r.PredicateId)) continue;
    const predName = thingMap.get(r.PredicateId)?.Name || r.PredicateId;
    predicateColorMap.set(r.PredicateId, resolvePredicateColor(predName, predicateColors));
  }

  // Pre-compute relationship index (O(m) one-time cost)
  const relIndex = buildRelationshipIndex(relationships, thingMap);

  // ── Add nodes ──────────────────────────────────────────────────────
  const angle = (2 * Math.PI) / Math.max(things.length, 1);
  things.forEach((t, i) => {
    const thingType = getThingType(t, relIndex);
    const relCount = relCounts.get(t.Id) || 0;

    // Geographic centroid from pre-computed latitude/longitude properties.
    // These are always present on physical things (computed at import time).
    const centroid = (typeof t.Properties?.latitude === 'number' && typeof t.Properties?.longitude === 'number')
      ? { lat: t.Properties.latitude as number, lng: t.Properties.longitude as number }
      : null;
    const hasGeometry = centroid !== null;

    let color: string;
    if (thingType === 'predicate') {
      color = ROLE_COLORS.predicate;
    } else if (thingType === 'type') {
      color = ROLE_COLORS.type;
    } else {
      // Instance node — derive colour from its "is" type name.
      // Physical (geo) nodes use the vibrant INSTANCE_PALETTE;
      // logical (non-geo) nodes use the softer LOGICAL_PALETTE.
      const typeName = getInstanceTypeName(t, relIndex);
      const palette = hasGeometry ? INSTANCE_PALETTE : LOGICAL_PALETTE;
      color = typeName
        ? palette[hashStringToIndex(typeName, palette.length)]
        : ROLE_COLORS.noType;
    }

    // Size: scale by relationship count (Bug #5361 — formula in computeNodeSize,
    // bounds drawn from LayoutSettings so they're runtime-tunable).
    const size = computeNodeSize(relCount, sizeOpts);

    // Initial circular layout (force supervisor will re-position)
    const radius = 100;
    const x = radius * Math.cos(angle * i);
    const y = radius * Math.sin(angle * i);

    graph.addNode(t.Id, {
      x,
      y,
      size,
      color,
      label: t.Name,
      thingType,
      hasGeometry,
      ...(centroid ? { lat: centroid.lat, lng: centroid.lng } : {}),
      hidden: false,
      isLogical: false,
    } satisfies GraphNodeAttrs);
  });

  // ── Add edges ──────────────────────────────────────────────────────
  for (const r of relationships) {
    // Only add edges when both endpoints exist in the graph
    if (!graph.hasNode(r.SubjectId) || !graph.hasNode(r.TargetId)) continue;

    const predicate = thingMap.get(r.PredicateId);
    const edgeColor = predicateColorMap.get(r.PredicateId) || '#52525b';

    graph.addDirectedEdgeWithKey(r.Id, r.SubjectId, r.TargetId, {
      size: edgeSize,
      color: edgeColor,
      label: predicate?.Name || r.PredicateId,
      type: 'arrow',
      predicateId: r.PredicateId,
    } satisfies GraphEdgeAttrs);
  }

  // ── Second pass: classify logical nodes & compute parent linkage ───
  graph.forEachNode((nodeId, attrs) => {
    if (attrs.hasGeometry) return; // physical node — already classified
    graph.setNodeAttribute(nodeId, 'isLogical', true);

    // Find the first geo-neighbour (via any edge direction) as parent
    const geoParent = findGeoParent(graph, nodeId);
    if (geoParent) {
      graph.setNodeAttribute(nodeId, 'parentGeoNodeId', geoParent);
    }
  });

  return graph;
}

// ── Logical node helpers ─────────────────────────────────────────────

/**
 * Find the first geographic neighbour of a logical node.
 * Prefers "has" containment edges (geo parent → logical child),
 * then falls back to any neighbour with geometry.
 */
function findGeoParent(graph: Graph, logicalNodeId: string): string | null {
  // First pass: look for a "has" edge where the geo node is the source
  // (parent "has" child pattern)
  for (const edge of graph.inEdges(logicalNodeId)) {
    const source = graph.source(edge);
    const edgeAttrs = graph.getEdgeAttributes(edge);
    const sourceAttrs = graph.getNodeAttributes(source);
    if (sourceAttrs.hasGeometry && (edgeAttrs.label === 'has')) {
      return source;
    }
  }

  // Second pass: any neighbour with geometry (in or out)
  for (const neighbor of graph.neighbors(logicalNodeId)) {
    const neighborAttrs = graph.getNodeAttributes(neighbor);
    if (neighborAttrs.hasGeometry) {
      return neighbor;
    }
  }

  return null;
}

/**
 * Get all logical child node IDs whose parentGeoNodeId matches the given geo node.
 */
export function getLogicalChildren(graph: Graph, geoNodeId: string): string[] {
  const children: string[] = [];
  graph.forEachNode((nodeId, attrs) => {
    if (attrs.isLogical && attrs.parentGeoNodeId === geoNodeId) {
      children.push(nodeId);
    }
  });
  return children;
}

/**
 * Count logical children of a geo node.
 */
export function countLogicalChildren(graph: Graph, geoNodeId: string): number {
  let count = 0;
  graph.forEachNode((_nodeId, attrs) => {
    if (attrs.isLogical && attrs.parentGeoNodeId === geoNodeId) {
      count++;
    }
  });
  return count;
}

