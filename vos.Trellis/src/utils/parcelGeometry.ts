/**
 * What a drawn parcel boundary encloses, and how that compares with what the planner stated.
 *
 * The area is computed on the sphere, mirroring the intake service's own measure: treating latitude
 * and longitude as flat coordinates looks right near the equator and is meaningfully wrong away from
 * it, and every downstream figure is proportional to this area. The flat reading is used only to
 * place the draft square, where the error is invisible at parcel scale.
 */

export interface BoundaryPoint {
  readonly latitude: number;
  readonly longitude: number;
}

const EARTH_RADIUS_METRES = 6371008.8;
const SQUARE_METRES_PER_HECTARE = 10_000;
const METRES_PER_DEGREE = (EARTH_RADIUS_METRES * Math.PI) / 180;

/** How far apart drawn and stated may sit and still read as the same area: loose enough for
 *  hand-drawing, tight enough to catch a wrong unit. */
export const AREA_MATCH_TOLERANCE = 0.08;

export function sphericalAreaHectares(boundary: readonly BoundaryPoint[]): number {
  if (boundary.length < 3) return 0;
  let total = 0;
  for (let corner = 0; corner < boundary.length; corner += 1) {
    const here = boundary[corner];
    const next = boundary[(corner + 1) % boundary.length];
    total +=
      toRadians(next.longitude - here.longitude) *
      (2 + Math.sin(toRadians(here.latitude)) + Math.sin(toRadians(next.latitude)));
  }
  const squareMetres = (Math.abs(total) * EARTH_RADIUS_METRES * EARTH_RADIUS_METRES) / 2;
  return squareMetres / SQUARE_METRES_PER_HECTARE;
}

/** A square of the given area centred on the given point, for the planner to drag onto the real
 *  boundary. */
export function draftSquareAround(centre: BoundaryPoint, areaHectares: number): BoundaryPoint[] {
  if (areaHectares <= 0) return [];
  const halfSideMetres = Math.sqrt(areaHectares * SQUARE_METRES_PER_HECTARE) / 2;
  const latitudeHalf = halfSideMetres / METRES_PER_DEGREE;
  const longitudeHalf = halfSideMetres / (METRES_PER_DEGREE * Math.cos(toRadians(centre.latitude)));
  return [
    { latitude: centre.latitude - latitudeHalf, longitude: centre.longitude - longitudeHalf },
    { latitude: centre.latitude - latitudeHalf, longitude: centre.longitude + longitudeHalf },
    { latitude: centre.latitude + latitudeHalf, longitude: centre.longitude + longitudeHalf },
    { latitude: centre.latitude + latitudeHalf, longitude: centre.longitude - longitudeHalf },
  ];
}

export type AreaComparison =
  | { readonly kind: 'match' }
  | { readonly kind: 'differs'; readonly relativeDifference: number };

/**
 * Drawn against stated, as the planner should read it. The difference is relative to the stated
 * area, which is what was claimed — the design's worked example reads 9.7 drawn against 24 stated
 * as 60% apart. A stated area of nothing is matched only by drawing nothing, and anything drawn
 * beside it is infinitely far from it.
 */
export function areaMatch(drawnHectares: number, statedHectares: number): AreaComparison {
  const apart = Math.abs(drawnHectares - statedHectares);
  if (apart <= AREA_MATCH_TOLERANCE * statedHectares) return { kind: 'match' };
  return { kind: 'differs', relativeDifference: apart / statedHectares };
}

function toRadians(degrees: number): number {
  return (degrees * Math.PI) / 180;
}
