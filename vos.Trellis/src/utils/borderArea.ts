import { enclosesLand, type BoundaryPoint } from './parcelGeometry';

/** The words the intake's form names its area method by, one per method the platform implements. */
export type AreaMethod = 'meanSphereAreaMethod' | 'wgs84EllipsoidAreaMethod';

const MEAN_EARTH_RADIUS_METRES = 6371008.8;
const WGS84_EQUATORIAL_RADIUS_METRES = 6378137.0;
const WGS84_FLATTENING = 1 / 298.257223563;
const SQUARE_METRES_PER_HECTARE = 10_000;

const ECCENTRICITY = Math.sqrt(WGS84_FLATTENING * (2 - WGS84_FLATTENING));
const AUTHALIC_AT_THE_POLE = authalic(Math.PI / 2);
const EQUAL_AREA_RADIUS_METRES = WGS84_EQUATORIAL_RADIUS_METRES * Math.sqrt(AUTHALIC_AT_THE_POLE / 2);

type Vector = readonly [number, number, number];

/** Measured with vos.Service.Shared's BorderArea formulas, so a border drawn here reads what the intake would measure. */
export function borderAreaHectares(boundary: readonly BoundaryPoint[], method: AreaMethod): number {
  if (!enclosesLand(boundary)) return 0;
  const squareMetres = method === 'meanSphereAreaMethod' ? meanSphereSquareMetres(boundary) : wgs84EllipsoidSquareMetres(boundary);
  return squareMetres / SQUARE_METRES_PER_HECTARE;
}

function meanSphereSquareMetres(boundary: readonly BoundaryPoint[]): number {
  let total = 0;
  for (let corner = 0; corner < boundary.length; corner += 1) {
    const here = boundary[corner];
    const next = boundary[(corner + 1) % boundary.length];
    total += toRadians(next.longitude - here.longitude) * (2 + Math.sin(toRadians(here.latitude)) + Math.sin(toRadians(next.latitude)));
  }
  return (Math.abs(total) * MEAN_EARTH_RADIUS_METRES * MEAN_EARTH_RADIUS_METRES) / 2;
}

// Each latitude moves to the sphere whose area equals the ellipsoid's, and the border's area there is the sum of the
// spherical excess of a fan of triangles from its first corner, which counts a concave border's turns by sign.
function wgs84EllipsoidSquareMetres(boundary: readonly BoundaryPoint[]): number {
  const points = boundary.map(onTheEqualAreaSphere);
  const first = points[0];
  let excess = 0;
  for (let corner = 1; corner < points.length - 1; corner += 1) {
    const here = points[corner];
    const next = points[corner + 1];
    excess += 2 * Math.atan2(dot(first, cross(here, next)), 1 + dot(first, here) + dot(here, next) + dot(next, first));
  }
  return Math.abs(excess) * EQUAL_AREA_RADIUS_METRES * EQUAL_AREA_RADIUS_METRES;
}

function onTheEqualAreaSphere(point: BoundaryPoint): Vector {
  const equalAreaLatitude = Math.asin(Math.min(1, Math.max(-1, authalic(toRadians(point.latitude)) / AUTHALIC_AT_THE_POLE)));
  const radiansEast = toRadians(point.longitude);
  return [
    Math.cos(equalAreaLatitude) * Math.cos(radiansEast),
    Math.cos(equalAreaLatitude) * Math.sin(radiansEast),
    Math.sin(equalAreaLatitude),
  ];
}

function authalic(latitude: number): number {
  const sine = Math.sin(latitude);
  const squared = ECCENTRICITY * ECCENTRICITY;
  return (
    (1 - squared) *
    (sine / (1 - squared * sine * sine) - Math.log((1 - ECCENTRICITY * sine) / (1 + ECCENTRICITY * sine)) / (2 * ECCENTRICITY))
  );
}

function dot(left: Vector, right: Vector): number {
  return left[0] * right[0] + left[1] * right[1] + left[2] * right[2];
}

function cross(left: Vector, right: Vector): Vector {
  return [left[1] * right[2] - left[2] * right[1], left[2] * right[0] - left[0] * right[2], left[0] * right[1] - left[1] * right[0]];
}

function toRadians(degrees: number): number {
  return (degrees * Math.PI) / 180;
}
