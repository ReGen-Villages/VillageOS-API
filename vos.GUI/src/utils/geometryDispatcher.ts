/**
 * Geometry dispatcher — unified entry point for geometry parsing.
 *
 * All geometry properties are IFC mesh data (pre-tessellated by the Xbim importer).
 * The broker sends geometry as { typeInfo: "vos.IfcGeometry", value: {...} }
 * which is unwrapped by propertyMapper to just the IFC mesh data object.
 */

import {
  parseIfcCentroid,
  parseIfcFootprint,
  parseIfcSolidMesh,
} from './ifcMeshParser';

export type { SolidMeshData } from './ifcMeshParser';

/**
 * Extract the centroid lat/lng from IFC geometry data.
 */
export function parseCentroid(geometryValue: unknown): { lat: number; lng: number } | null {
  return parseIfcCentroid(geometryValue);
}

/**
 * Extract a GeoJSON footprint Feature from IFC geometry data.
 */
export function parseFootprint(
  geometryValue: unknown,
  thingName?: string,
  color?: string,
): GeoJSON.Feature | null {
  return parseIfcFootprint(geometryValue, thingName, color);
}

/**
 * Extract Three.js-compatible mesh data from IFC geometry data.
 */
export function parseSolidMesh(geometryValue: unknown): import('./ifcMeshParser').SolidMeshData | null {
  return parseIfcSolidMesh(geometryValue);
}
