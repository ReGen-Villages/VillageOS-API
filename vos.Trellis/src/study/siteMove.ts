import { middleOfExtent, type BoundaryPoint } from '../utils/parcelGeometry';

export interface SiteAfterImport {
  readonly position: BoundaryPoint;
  readonly moved: boolean;
}

/** Farther than this, an imported border is taken to be a different place from the one the site was found at. */
const SITE_MOVES_BEYOND_METRES = 300;
const MEAN_EARTH_RADIUS_METRES = 6371008.8;

export function siteAfterImport(site: BoundaryPoint | null, border: readonly BoundaryPoint[]): SiteAfterImport {
  const centre = middleOfExtent(border);
  if (site === null || metresBetween(site, centre) > SITE_MOVES_BEYOND_METRES) return { position: centre, moved: true };
  return { position: site, moved: false };
}

function metresBetween(from: BoundaryPoint, to: BoundaryPoint): number {
  const northward = toRadians(to.latitude - from.latitude);
  const eastward = toRadians(to.longitude - from.longitude);
  const haversine =
    Math.sin(northward / 2) ** 2 + Math.cos(toRadians(from.latitude)) * Math.cos(toRadians(to.latitude)) * Math.sin(eastward / 2) ** 2;
  return 2 * MEAN_EARTH_RADIUS_METRES * Math.asin(Math.sqrt(haversine));
}

function toRadians(degrees: number): number {
  return (degrees * Math.PI) / 180;
}
