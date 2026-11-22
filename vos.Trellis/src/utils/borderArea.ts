import type { BoundaryPoint } from './parcelGeometry';

export type AreaMethod = 'meanSphereAreaMethod' | 'wgs84EllipsoidAreaMethod';

export function borderAreaHectares(_boundary: readonly BoundaryPoint[], _method: AreaMethod): number {
  throw new Error('not built yet');
}
