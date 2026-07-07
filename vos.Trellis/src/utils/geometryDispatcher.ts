/**
 * Geometry parser for inline IFC mesh data carried on VosThing properties.
 *
 * The legacy .NET IFC importer tessellated geometry server-side and stored
 * it as an IfcGeometry property: { positions, indices, normals, height,
 * localCenter }. The Mycelium sends it as
 * { typeInfo: "vos.IfcGeometry", value: {...} } which is unwrapped by
 * propertyMapper to just the IFC mesh data object.
 *
 * Newer models ingested via vos.Tools.IfcIngest do NOT carry inline
 * geometry — their geometry lives in the .frag artifact rendered by
 * BimFragmentsViewer. This parser remains only for the Graph page's
 * BuildingDetail3D tab, which displays a single element's inline mesh
 * when one is present.
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
 * Convert a pre-tessellated IFC mesh value into SolidMeshData, or null if
 * the value doesn't look like IFC mesh data or carries no geometry.
 */
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
