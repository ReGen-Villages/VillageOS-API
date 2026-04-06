/**
 * Shared constants and helpers for geometry mesh generation.
 * Used by cityjsonParser.ts and geojsonParser.ts.
 */

/** Meters per degree of latitude (approximate, constant at all latitudes). */
export const M_PER_DEG_LAT = 111_320.0;

/** Minimum extrusion height for flat geometry (meters). */
export const FLAT_EXTRUDE_HEIGHT = 0.15;

/** Half-width of the marker cube generated for Point/MultiPoint geometry (meters). */
export const POINT_MARKER_HALF = 0.5;

/**
 * Compute the cross product of vectors (b-a) and (c-a).
 */
export function crossProduct(
  ax: number, ay: number, az: number,
  bx: number, by: number, bz: number,
  cx: number, cy: number, cz: number,
): [number, number, number] {
  const ux = bx - ax, uy = by - ay, uz = bz - az;
  const vx = cx - ax, vy = cy - ay, vz = cz - az;
  return [
    uy * vz - uz * vy,
    uz * vx - ux * vz,
    ux * vy - uy * vx,
  ];
}
