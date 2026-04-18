/**
 * IFC mesh parser — handles pre-tessellated mesh data from the Xbim importer.
 *
 * The .NET IFC importer tessellates geometry server-side and stores it as
 * an IfcGeometry property: { positions, indices, normals, height, localCenter }
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
