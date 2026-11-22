/** A border's area is measured in borderArea.ts, as the intake service measures it, because treating latitude and
 *  longitude as flat is wrong away from the equator and every later figure is proportional to it. The flat
 *  reading only places the draft square, where the error cannot be seen. */

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

/** Fewer than three corners describe no land, which is also why their area is nought and a submission
 *  carrying them is refused. */
export function enclosesLand(boundary: readonly BoundaryPoint[]): boolean {
  return boundary.length >= 3;
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

const CIRCLE_CORNERS = 64;

export function circleOfAreaAround(centre: BoundaryPoint, areaHectares: number): BoundaryPoint[] {
  if (areaHectares <= 0) return [];
  const radiusMetres = Math.sqrt((areaHectares * SQUARE_METRES_PER_HECTARE) / Math.PI);
  const latitudeRadius = radiusMetres / METRES_PER_DEGREE;
  const longitudeRadius = radiusMetres / (METRES_PER_DEGREE * Math.cos(toRadians(centre.latitude)));
  return Array.from({ length: CIRCLE_CORNERS }, (_, corner) => {
    const angle = (2 * Math.PI * corner) / CIRCLE_CORNERS;
    return {
      latitude: centre.latitude + latitudeRadius * Math.sin(angle),
      longitude: centre.longitude + longitudeRadius * Math.cos(angle),
    };
  });
}

type AreaComparison =
  | { readonly kind: 'match' }
  | { readonly kind: 'differs'; readonly relativeDifference: number };

/** Relative to the stated area, which is what was claimed: 9.7 drawn against 24 stated is 60% apart. A
 *  stated area of nothing is matched only by drawing nothing. */
export function areaMatch(drawnHectares: number, statedHectares: number): AreaComparison {
  const apart = Math.abs(drawnHectares - statedHectares);
  if (apart <= AREA_MATCH_TOLERANCE * statedHectares) return { kind: 'match' };
  return { kind: 'differs', relativeDifference: apart / statedHectares };
}

function toRadians(degrees: number): number {
  return (degrees * Math.PI) / 180;
}
