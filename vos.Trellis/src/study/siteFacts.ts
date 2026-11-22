import type { BoundaryPoint } from '../utils/parcelGeometry';

export interface Elevation {
  readonly siteMetres: number;
  readonly lowestMetres: number | null;
  readonly highestMetres: number | null;
}

export function hectaresText(hectares: number): string {
  return hectares.toFixed(1);
}

export function coordinatesText({ latitude, longitude }: BoundaryPoint): string {
  return `${Math.abs(latitude).toFixed(5)}°${latitude < 0 ? 'S' : 'N'}, ${Math.abs(longitude).toFixed(5)}°${longitude < 0 ? 'W' : 'E'}`;
}

export function elevationText({ siteMetres, lowestMetres, highestMetres }: Elevation): {
  readonly site: string;
  readonly range: string | null;
} {
  const site = Math.round(siteMetres).toString();
  if (lowestMetres === null || highestMetres === null) return { site, range: null };
  const lowest = Math.round(lowestMetres);
  const highest = Math.round(highestMetres);
  return { site, range: lowest === highest ? null : `${lowest}–${highest}` };
}
