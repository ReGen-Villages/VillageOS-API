/**
 * Geometry dispatcher — unified entry point for geometry parsing.
 *
 * All geometry properties are IFC mesh data (pre-tessellated by the Xbim importer).
 * The broker sends geometry as { typeInfo: "vos.IfcGeometry", value: {...} }
 * which is unwrapped by propertyMapper to just the IFC mesh data object.
 */

import { parseIfcSolidMesh } from './ifcMeshParser';

export type { SolidMeshData } from './ifcMeshParser';

/**
 * Extract Three.js-compatible mesh data from IFC geometry data.
 */
export function parseSolidMesh(geometryValue: unknown): import('./ifcMeshParser').SolidMeshData | null {
  return parseIfcSolidMesh(geometryValue);
}
