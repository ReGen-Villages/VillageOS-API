/** Older models carry ready-made meshes in an IfcGeometry property; models from vos.Tools.ModelIngest keep
 *  their geometry in the .frag file instead, so this serves only the Graph page's BuildingDetail3D tab.
 *  The value arrives already taken out of its type wrapper, and its coordinates already have y up, as
 *  three.js expects. */

export interface SolidMeshData {
  /** Flat vertex positions [x,y,z, ...] — x=east, y=up, z=south (local meters). */
  positions: Float32Array;
  /** Three per triangle. */
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

export function parseSolidMesh(geometryValue: unknown): SolidMeshData | null {
  if (!isIfcMeshData(geometryValue)) return null;
  if (geometryValue.positions.length === 0 || geometryValue.indices.length === 0) return null;

  return {
    positions: new Float32Array(geometryValue.positions),
    indices: new Uint16Array(geometryValue.indices),
    normals: new Float32Array(geometryValue.normals),
    height: geometryValue.height,
    localCenter: [geometryValue.localCenter[0], geometryValue.localCenter[1]],
  };
}
