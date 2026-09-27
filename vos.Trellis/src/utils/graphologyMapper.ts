import Graph from 'graphology';
import { storedTextOf } from './ifcIdentity';
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
import { resolveClassColor } from './classPalette';

// Built in one pass over things and relationships, rather than a scan of every relationship per thing.
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
    const predicate = thingMap.get(r.PredicateId);
    if (predicate && predicate.Name.toLowerCase() === 'is') {
      isTypeTargets.add(r.TargetId);
      if (!isSubjectToTypeName.has(r.SubjectId)) {
        const targetName = thingMap.get(r.TargetId)?.Name;
        if (targetName) isSubjectToTypeName.set(r.SubjectId, targetName);
      }
    }
  }

  return { predicateIds, isTypeTargets, isSubjectToTypeName };
}

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

interface GraphNodeAttributes {
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
  [key: string]: unknown;
}

interface GraphEdgeAttributes {
  size: number;
  color: string;
  label: string;
  type: string; // 'arrow'
  predicateId: string;
  [key: string]: unknown;
}

export function buildGraph(
  things: VosThing[],
  relationships: VosRelationship[],
  predicateColors: Record<string, string> = {},
  /**
   * Per-value color overrides scoped to the configured classifying property
   * (LayoutSettings.classifyingProperty). Keys are values of that property
   * (e.g. "IfcWall" when classifyingProperty='ifcClass'). Empty by default;
   * future GUI_Settings panel will let users edit this map at runtime.
   */
  classColorOverrides: Record<string, string> = {},
  layoutSettings: LayoutSettings = LAYOUT_DEFAULTS,
): Graph {
  const classifyingProperty = layoutSettings.classifyingProperty;
  const sizeOptions = {
    min: layoutSettings.nodeSizeMin,
    max: layoutSettings.nodeSizeMax,
    slope: layoutSettings.nodeSizeSlope,
  };
  const edgeSize = layoutSettings.edgeSize;
  const graph = new Graph({ multi: true, type: 'directed' });
  const thingMap = new Map(things.map((t) => [t.Id, t]));

  const relationshipCounts = new Map<string, number>();
  for (const r of relationships) {
    relationshipCounts.set(r.TargetId, (relationshipCounts.get(r.TargetId) || 0) + 1);
  }

  const predicateColorMap = new Map<string, string>();
  for (const r of relationships) {
    if (predicateColorMap.has(r.PredicateId)) continue;
    const predicateName = thingMap.get(r.PredicateId)?.Name || r.PredicateId;
    predicateColorMap.set(r.PredicateId, resolvePredicateColor(predicateName, predicateColors));
  }

  const relationshipIndex = buildRelationshipIndex(relationships, thingMap);

  const angle = (2 * Math.PI) / Math.max(things.length, 1);
  things.forEach((t, i) => {
    const thingType = getThingType(t, relationshipIndex);
    const relationshipCount = relationshipCounts.get(t.Id) || 0;

    // Latitude and longitude are computed at import time, so every physical thing carries them.
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
      // A missing classifying value, or one with no curated bucket and no override, falls back to the
      // hash palette keyed by the `is`-target type name so a model without a class table still renders
      // distinctly. An imported instance stores its own class as an override because its type declares
      // the same name, so the own bag alone is empty here.
      const classifyingValue = storedTextOf(t, classifyingProperty);
      if (classifyingValue) {
        color = resolveClassColor(classifyingProperty, classifyingValue, classColorOverrides);
      } else {
        const typeName = getInstanceTypeName(t, relationshipIndex);
        const palette = hasGeometry ? INSTANCE_PALETTE : LOGICAL_PALETTE;
        color = typeName
          ? palette[hashStringToIndex(typeName, palette.length)]
          : ROLE_COLORS.noType;
      }
    }

    const size = computeNodeSize(relationshipCount, sizeOptions);

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
    } satisfies GraphNodeAttributes);
  });

  for (const r of relationships) {
    if (!graph.hasNode(r.SubjectId) || !graph.hasNode(r.TargetId)) continue;

    const predicate = thingMap.get(r.PredicateId);
    const edgeColor = predicateColorMap.get(r.PredicateId) || '#52525b';

    graph.addDirectedEdgeWithKey(r.Id, r.SubjectId, r.TargetId, {
      size: edgeSize,
      color: edgeColor,
      label: predicate?.Name || r.PredicateId,
      type: 'arrow',
      predicateId: r.PredicateId,
    } satisfies GraphEdgeAttributes);
  }

  graph.forEachNode((nodeId, attributes) => {
    if (attributes.hasGeometry) return; // physical node — already classified
    graph.setNodeAttribute(nodeId, 'isLogical', true);

    const geoParent = findGeoParent(graph, nodeId);
    if (geoParent) {
      graph.setNodeAttribute(nodeId, 'parentGeoNodeId', geoParent);
    }
  });

  return graph;
}

/**
 * Find the first geographic neighbour of a logical node.
 * Prefers "has" containment edges (geo parent → logical child),
 * then falls back to any neighbour with geometry.
 */
function findGeoParent(graph: Graph, logicalNodeId: string): string | null {
  for (const edge of graph.inEdges(logicalNodeId)) {
    const source = graph.source(edge);
    const edgeAttributes = graph.getEdgeAttributes(edge);
    const sourceAttributes = graph.getNodeAttributes(source);
    if (sourceAttributes.hasGeometry && (edgeAttributes.label === 'has')) {
      return source;
    }
  }

  for (const neighbor of graph.neighbors(logicalNodeId)) {
    const neighborAttributes = graph.getNodeAttributes(neighbor);
    if (neighborAttributes.hasGeometry) {
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
  graph.forEachNode((nodeId, attributes) => {
    if (attributes.isLogical && attributes.parentGeoNodeId === geoNodeId) {
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
  graph.forEachNode((_nodeId, attributes) => {
    if (attributes.isLogical && attributes.parentGeoNodeId === geoNodeId) {
      count++;
    }
  });
  return count;
}

