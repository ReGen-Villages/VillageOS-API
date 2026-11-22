import type { BoundaryPoint } from '../utils/parcelGeometry';

export interface SiteAfterImport {
  readonly position: BoundaryPoint;
  readonly moved: boolean;
}

export function siteAfterImport(_site: BoundaryPoint | null, _border: readonly BoundaryPoint[]): SiteAfterImport {
  throw new Error('not built yet');
}
