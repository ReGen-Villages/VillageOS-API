import type { BoundaryPoint } from '../utils/parcelGeometry';

export interface Elevation {
  readonly siteMetres: number;
  readonly lowestMetres: number | null;
  readonly highestMetres: number | null;
}

export function hectaresText(_hectares: number): string {
  throw new Error('not built yet');
}

export function coordinatesText(_position: BoundaryPoint): string {
  throw new Error('not built yet');
}

export function elevationText(_elevation: Elevation): { readonly site: string; readonly range: string | null } {
  throw new Error('not built yet');
}
