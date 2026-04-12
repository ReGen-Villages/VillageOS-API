/**
 * Batch builder for the 3D village scene.
 *
 * Takes a list of VosThing[] and produces all mesh data the 3D
 * scene needs — no Three.js imports, just pure typed arrays.
 */

import type { VosThing, VosRelationship } from '../types/vos';
import { parseSolidMesh, type SolidMeshData } from './geometryDispatcher';
import { hashStringToIndex, ELEMENT_COLORS } from './colors';

// ── Types ────────────────────────────────────────────────────────────────

export interface BuildingMesh {
  thingId: string;
  thingName: string;
  mesh: SolidMeshData;
  color: string;
}

export interface SceneBounds {
  minX: number;
  maxX: number;
  minZ: number;
  maxZ: number;
  maxHeight: number;
}

export interface VillageSceneData {
  buildings: BuildingMesh[];
  bounds: SceneBounds;
}

function colorForName(name: string): string {
  return ELEMENT_COLORS[hashStringToIndex(name, ELEMENT_COLORS.length)];
}

// ── Scene builder ───────────────────────────────────────────────────────

/**
 * Build the scene data for all things with valid geometry.
 * Returns null if no buildings can be extracted.
 *
 * When `relationships` is provided, things without own geometry but with
 * `contains`/`aggregates` children that have geometry will include those
 * child meshes (useful for IFC containers like IfcBuilding/IfcStorey).
 */
export function prepareVillageScene(
  things: VosThing[],
  colorMap?: Map<string, string>,
  relationships?: VosRelationship[],
): VillageSceneData | null {
  const buildings: BuildingMesh[] = [];
  const thingsById = new Map(things.map((t) => [t.Id, t]));

  // Find spatial predicate IDs via __IsMapContainmentPredicate flag (not name)
  const spatialPredicateIds = new Set<string>();
  if (relationships) {
    for (const t of things) {
      if (t.Properties?.__IsMapContainmentPredicate === true) {
        spatialPredicateIds.add(t.Id);
      }
    }
  }

  // Track which things we've already added (avoid duplicates when child is also top-level)
  const added = new Set<string>();

  for (const thing of things) {
    const geoProp = thing.Properties?.geometry;

    if (geoProp != null && !added.has(thing.Id)) {
      const mesh = parseSolidMesh(geoProp);
      if (mesh) {
        buildings.push({
          thingId: thing.Id,
          thingName: thing.Name,
          mesh,
          color: colorMap?.get(thing.Id) ?? colorForName(thing.Name),
        });
        added.add(thing.Id);
      }
      continue;
    }

    // No own geometry — check for children with geometry (IFC containers)
    if (relationships && spatialPredicateIds.size > 0) {
      for (const rel of relationships) {
        if (rel.SubjectId !== thing.Id) continue;
        if (!spatialPredicateIds.has(rel.PredicateId)) continue;

        const child = thingsById.get(rel.TargetId);
        if (!child?.Properties?.geometry || added.has(child.Id)) continue;

        const childMesh = parseSolidMesh(child.Properties.geometry);
        if (!childMesh) continue;

        buildings.push({
          thingId: child.Id,
          thingName: child.Name,
          mesh: childMesh,
          color: colorMap?.get(child.Id) ?? colorForName(child.Name),
        });
        added.add(child.Id);
      }
    }
  }

  if (buildings.length === 0) return null;

  // Compute scene bounds
  let minX = Infinity, maxX = -Infinity;
  let minZ = Infinity, maxZ = -Infinity;
  let maxHeight = 0;

  for (const b of buildings) {
    // Compute world bounds from all vertex positions
    const pos = b.mesh.positions;
    for (let i = 0; i < pos.length; i += 3) {
      const x = pos[i], z = pos[i + 2];
      if (x < minX) minX = x;
      if (x > maxX) maxX = x;
      if (z < minZ) minZ = z;
      if (z > maxZ) maxZ = z;
    }
    if (b.mesh.height > maxHeight) maxHeight = b.mesh.height;
  }

  return {
    buildings,
    bounds: { minX, maxX, minZ, maxZ, maxHeight },
  };
}
