/**
 * IFC mesh parser — handles pre-tessellated mesh data from the Xbim importer.
 *
 * The .NET IFC importer tessellates geometry server-side and stores it as
 * an IfcGeometry property: { positions, indices, normals, height, localCenter, centroid?, footprint? }
 *
 * Coordinates arrive in Three.js convention (y-up) — no swizzle needed.
 */

/**
 * Three.js-compatible mesh data.
 * Coordinates are in local meters with y-up (Three.js convention).
 */
export interface SolidMeshData {
  /** Flat vertex positions [x,y,z, ...] — x=east, y=up, z=south (local meters). */
  positions: Float32Array;
  /** Triangle indices (3 per triangle). */
  indices: Uint16Array;
  /** Flat face normals (one per vertex, duplicated for flat shading). */
  normals: Float32Array;
  /** Building height in meters. */
  height: number;
  /** Ground-plane centroid in Three.js coords [x, z] for scene positioning. */
  localCenter: [number, number];
}

interface IfcMeshData {
  positions: number[];
  indices: number[];
  normals: number[];
  height: number;
  localCenter: [number, number];
  centroid?: { lat: number; lng: number };
  footprint?: GeoJSON.Geometry;
}

function isIfcMeshData(data: unknown): data is IfcMeshData {
  if (typeof data !== 'object' || data === null) return false;
  const d = data as Record<string, unknown>;
  return (
    Array.isArray(d.positions) &&
    Array.isArray(d.indices) &&
    Array.isArray(d.normals) &&
    typeof d.height === 'number' &&
    Array.isArray(d.localCenter) &&
    (d.localCenter as unknown[]).length === 2
  );
}

/**
 * Extract centroid lat/lng from pre-tessellated IFC mesh data.
 * The importer pre-computes the WGS84 centroid from IfcSite georeferencing.
 */
export function parseIfcCentroid(data: unknown): { lat: number; lng: number } | null {
  if (!isIfcMeshData(data)) return null;
  if (!data.centroid) return null;
  if (typeof data.centroid.lat !== 'number' || typeof data.centroid.lng !== 'number') return null;
  return { lat: data.centroid.lat, lng: data.centroid.lng };
}

/**
 * Extract a GeoJSON footprint Feature from pre-tessellated IFC mesh data.
 * Uses the pre-computed footprint if available, otherwise returns null.
 */
export function parseIfcFootprint(
  data: unknown,
  thingName?: string,
  color?: string,
): GeoJSON.Feature | null {
  if (!isIfcMeshData(data)) return null;
  if (!data.footprint) return null;

  return {
    type: 'Feature',
    properties: {
      name: thingName || '',
      height: data.height,
      color: color || '#6d8ea8',
    },
    geometry: data.footprint,
  };
}

/**
 * Convert pre-tessellated IFC mesh arrays into SolidMeshData.
 * The importer already provides y-up coordinates — just wrap in typed arrays.
 */
export function parseIfcSolidMesh(data: unknown): SolidMeshData | null {
  if (!isIfcMeshData(data)) return null;
  if (data.positions.length === 0 || data.indices.length === 0) return null;

  return {
    positions: new Float32Array(data.positions),
    indices: new Uint16Array(data.indices),
    normals: new Float32Array(data.normals),
    height: data.height,
    localCenter: [data.localCenter[0], data.localCenter[1]],
  };
}
